import open3d as o3d
import glob
import time

# Pobierz posortowaną listę plików
files = sorted(glob.glob("../Lidar_chuj/VelodyneReader/frames/*.ply"))
vis = o3d.visualization.Visualizer()
vis.create_window()

print("Ładowanie pierwszej klatki...")
pcd = o3d.io.read_point_cloud(files[0])
vis.add_geometry(pcd)

for file in files[1:]:
    # Wczytaj nową geometrię
    new_pcd = o3d.io.read_point_cloud(file)
    # Zaktualizuj punkty w istniejącym obiekcie (szybciej niż usuwanie i dodawanie)
    pcd.points = new_pcd.points
    
    vis.update_geometry(pcd)
    vis.poll_events()
    vis.update_renderer()
    time.sleep(0.05) # Szybkość animacji

vis.destroy_window()