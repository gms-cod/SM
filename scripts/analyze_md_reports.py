# -*- coding: utf-8 -*-
import os
import glob
import sys

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

def analyze_markdown_reports():
    files = glob.glob("logs/**/*.md", recursive=True) + glob.glob("logs/*.md")
    files = list(set([f for f in files if not f.endswith("_dump.md")]))
    
    models = {
        "DEEPSEEK": {"files": 0, "total_kb": 0, "largest_kb": 0, "failures": 0},
        "BEAM": {"files": 0, "total_kb": 0, "largest_kb": 0, "failures": 0},
        "LONGCAT": {"files": 0, "total_kb": 0, "largest_kb": 0, "failures": 0},
        "NEMOTRON": {"files": 0, "total_kb": 0, "largest_kb": 0, "failures": 0},
        "GLM": {"files": 0, "total_kb": 0, "largest_kb": 0, "failures": 0},
        "SPACE_BUNNY": {"files": 0, "total_kb": 0, "largest_kb": 0, "failures": 0},
        "MUSE_SPARK": {"files": 0, "total_kb": 0, "largest_kb": 0, "failures": 0},
    }

    for fpath in files:
        fname = os.path.basename(fpath).upper()
        size_kb = round(os.path.getsize(fpath) / 1024, 1)
        for key in models.keys():
            if key in fname:
                models[key]["files"] += 1
                models[key]["total_kb"] += size_kb
                if size_kb > models[key]["largest_kb"]:
                    models[key]["largest_kb"] = size_kb
                if size_kb < 1.0: # Меньше 1 КБ — пустой или сорванный ответ
                    models[key]["failures"] += 1
                break

    print("=" * 80)
    print("СТАТИСТИКА ГЕНЕРАЦИИ ОТЧЕТОВ И ОБЪЕМА ПОЛЕЗНОГО ВЫХОДА ПО МОДЕЛЯМ")
    print("=" * 80)
    print(f"{'Модель':<16} | {'Отчетов':<8} | {'Сбоев (<1КБ)':<13} | {'Суммарно (КБ)':<15} | {'Макс (КБ)':<10} | {'Ср. объем (КБ)'}")
    print("-" * 80)
    for k, v in sorted(models.items(), key=lambda x: x[1]["total_kb"], reverse=True):
        if v["files"] == 0:
            continue
        avg = round(v["total_kb"] / v["files"], 1)
        print(f"{k:<16} | {v['files']:<8} | {v['failures']:<13} | {str(v['total_kb']) + ' KB':<15} | {str(v['largest_kb']) + ' KB':<10} | {avg} KB")

if __name__ == "__main__":
    analyze_markdown_reports()

