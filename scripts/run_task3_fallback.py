# -*- coding: utf-8 -*-
import subprocess, os, sys

BASE_DIR = r"d:\Nas_sync\Coding\StartupMonitor"
bench_dir = os.path.join(BASE_DIR, "bench_audit3")
doc_file = os.path.join(BASE_DIR, "docs", "AUDIT_SWARM_3_SNAPSHOTS_NAVIGATION.md")

PROMPT = """Ты — ведущий UI-инженер и аналитик интерфейса StartupMonitor.
Проведи аудит проблемы пропажи «Снимков системы» (Snapshots) и эргономики навигации:
1. Выясни, почему пользователи жалуются, что в UI «не стало снимков системы». Изучи MainWindow.xaml, MainWindow.xaml.cs, SnapshotsWindow.xaml и SnapshotManager.cs.
2. Проблема: сейчас кнопка «Снимки системы» спрятана как второстепенная кнопка в правом верхнем углу страницы «Обзор» и маленькой иконкой над списком записей. Отдельного раздела в левом боковом меню навигации нет!
3. Разработай добавление полноценного пункта меню в левую навигационную панель MainWindow.xaml:
   - NavItemSnapshots с иконкой фотоаппарата (Camera24 / IconCamera), счетчиком сохраненных снимков и переходом либо на отдельную страницу снимков, либо открытием SnapshotsWindow.
4. Проверь также отображение баннера активного сравнения со снимком («Было — Стало»).
Выдай точные фрагменты XAML и C# с русскими комментариями.
"""

cmd = ["opencode", "run", "--pure", "--dir", bench_dir, "-m", "opencode/beam-free"]
proc = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", cwd=bench_dir)
proc.stdin.write(PROMPT)
proc.stdin.close()
out = proc.stdout.read()
proc.wait()

with open(doc_file, "w", encoding="utf-8") as f:
    f.write(out)

print("Beam-free успешно выполнил задачу 3 и записал отчет.")

