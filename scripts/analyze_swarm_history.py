# -*- coding: utf-8 -*-
import os
import glob
import json
import sys

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

def analyze_live_statuses():
    files = glob.glob("logs/**/live_status*.json", recursive=True) + glob.glob("logs/live_status*.json")
    files = list(set(files))
    print(f"Найдено файлов статуса: {len(files)}\n")
    model_stats = {}

    for fpath in files:
        try:
            with open(fpath, "r", encoding="utf-8") as f:
                data = json.load(f)
                agents = data.get("agents", {})
                if not agents and isinstance(data, dict):
                    # проверка структуры где агенты прямо в корне или по именам
                    agents = {k: v for k, v in data.items() if isinstance(v, dict) and ("model" in v or "status" in v)}

                for agent_name, info in agents.items():
                    model = info.get("model", info.get("name", "unknown"))
                    elapsed = info.get("elapsed_seconds", info.get("elapsed", 0))
                    status = info.get("status", "unknown")
                    
                    if model not in model_stats:
                        model_stats[model] = {
                            "runs": 0,
                            "completed": 0,
                            "total_elapsed": 0,
                            "elapsed_list": [],
                            "statuses": []
                        }
                    model_stats[model]["runs"] += 1
                    if status in ["COMPLETED", "DONE", "finished"]:
                        model_stats[model]["completed"] += 1
                        model_stats[model]["total_elapsed"] += elapsed
                        model_stats[model]["elapsed_list"].append(elapsed)
                    model_stats[model]["statuses"].append(status)
        except Exception:
            pass

    print(f"{'Модель':<42} | {'Запусков':<8} | {'Успех':<8} | {'Среднее время':<16} | {'Мин-Макс'}")
    print("-" * 95)
    for model, s in sorted(model_stats.items(), key=lambda x: x[1]["runs"], reverse=True):
        runs = s["runs"]
        comp = s["completed"]
        avg = round(s["total_elapsed"] / comp, 1) if comp > 0 else 0
        min_max = f"{min(s['elapsed_list'])}s - {max(s['elapsed_list'])}s" if s['elapsed_list'] else "N/A"
        print(f"{model:<42} | {runs:<8} | {comp:<8} | {str(avg) + 's':<16} | {min_max}")

if __name__ == "__main__":
    analyze_live_statuses()
