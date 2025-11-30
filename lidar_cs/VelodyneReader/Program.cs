using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using SharpPcap;
using SharpPcap.LibPcap;
using PacketDotNet;

// === KONFIGURACJA ===
const string PcapPath = "2v.pcap";
const int MaxPackets = 10000;         // Zwiększyłem limit, żeby złapać więcej klatek
const string OutputFolder = "frames"; // Folder na klatki

// === STAŁE VLP-16 ===
double[] VerticalAngles = [-15, 1, -13, -3, -11, 5, -9, 7, -7, 9, -5, 11, -3, 13, -1, 15];
double[] VerticalAnglesRad = VerticalAngles.Select(deg => deg * Math.PI / 180.0).ToArray();
const double DistanceResolution = 0.002;
const int BlocksPerPacket = 12;
const int ChannelsPerBlock = 32;

// Przygotowanie folderu na klatki
if (Directory.Exists(OutputFolder)) Directory.Delete(OutputFolder, true);
Directory.CreateDirectory(OutputFolder);

List<Vector3> currentFramePoints = new(50000); // Bufor na jedną klatkę
int frameCount = 0;
double lastAzimuth = -1.0;

Console.WriteLine($"📂 Otwieranie pliku: {PcapPath}");

try
{
    using var device = new CaptureFileReaderDevice(PcapPath);
    device.Open();

    int packetCount = 0;
    PacketCapture pCapture;

    // Pętla po pakietach
    while (device.GetNextPacket(out pCapture) == GetPacketStatus.PacketRead && packetCount < MaxPackets)
    {
        RawCapture rawCapture = pCapture.GetPacket();
        packetCount++;

        if (packetCount % 100 == 0)
            Console.Write($"\r🔍 Przetworzono pakietów: {packetCount} | Zapisano klatek: {frameCount}");

        var packet = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
        var udpPacket = packet.Extract<UdpPacket>();

        if (udpPacket == null || udpPacket.DestinationPort != 2368) continue;

        ReadOnlySpan<byte> raw = udpPacket.PayloadData;
        if (raw.Length != 1206) continue;

        // Pętla po blokach w pakiecie
        for (int block = 0; block < BlocksPerPacket; block++)
        {
            int baseOffset = block * 100;

            ushort flag = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(baseOffset, 2));
            if (flag != 0xEEFF) continue;

            ushort azimuthRaw = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(baseOffset + 2, 2));
            double azimuth = azimuthRaw / 100.0;

            // === WYKRYWANIE NOWEJ KLATKI (OBROTU) ===
            // Jeśli kąt był duży (np. > 350) i nagle jest mały (np. < 10), to znaczy, że minęliśmy 0 stopni.
            if (lastAzimuth > 350.0 && azimuth < 10.0)
            {
                // Zapisz obecną chmurę jako plik
                string filename = Path.Combine(OutputFolder, $"frame_{frameCount:D4}.ply");
                SaveToPly(filename, currentFramePoints);
                
                // Resetujemy bufor na nową klatkę
                currentFramePoints.Clear();
                frameCount++;
            }
            lastAzimuth = azimuth;
            // ========================================

            double azimuthRad = azimuth * (Math.PI / 180.0);
            double cosAzimuth = Math.Cos(azimuthRad);
            double sinAzimuth = Math.Sin(azimuthRad);

            for (int channel = 0; channel < ChannelsPerBlock; channel++)
            {
                int offset = baseOffset + 4 + (channel * 3);
                ushort distanceRaw = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(offset, 2));

                if (distanceRaw == 0) continue;

                double distanceM = distanceRaw * DistanceResolution;
                double vertAngle = VerticalAnglesRad[channel % 16];
                double xyDistance = distanceM * Math.Cos(vertAngle);

                float x = (float)(xyDistance * sinAzimuth);
                float y = (float)(xyDistance * cosAzimuth);
                float z = (float)(distanceM * Math.Sin(vertAngle));

                currentFramePoints.Add(new Vector3(x, y, z));
            }
        }
    }

    // Zapisz ostatnią, niedomkniętą klatkę (jeśli coś zostało)
    if (currentFramePoints.Count > 0)
    {
        string filename = Path.Combine(OutputFolder, $"frame_{frameCount:D4}.ply");
        SaveToPly(filename, currentFramePoints);
        frameCount++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Błąd: {ex.Message}");
}

Console.WriteLine($"\n✅ Gotowe! Wygenerowano {frameCount} klatek w folderze '{OutputFolder}'.");

// --- METODA ZAPISU ---
static void SaveToPly(string path, List<Vector3> points)
{
    using StreamWriter writer = new(path);
    writer.WriteLine("ply");
    writer.WriteLine("format ascii 1.0");
    writer.WriteLine($"element vertex {points.Count}");
    writer.WriteLine("property float x");
    writer.WriteLine("property float y");
    writer.WriteLine("property float z");
    writer.WriteLine("end_header");

    var culture = CultureInfo.InvariantCulture;
    foreach (var p in points)
    {
        writer.WriteLine($"{p.X.ToString(culture)} {p.Y.ToString(culture)} {p.Z.ToString(culture)}");
    }
}