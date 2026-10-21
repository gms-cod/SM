# -*- coding: utf-8 -*-
import os
import sys
import json
import time
import urllib.request

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

def main():
    key = os.environ.get("OPENROUTER_API_KEY")
    auth_file = os.path.expanduser(r"~/.local/share/opencode/auth.json")
    if not key and os.path.exists(auth_file):
        try:
            with open(auth_file, "r", encoding="utf-8") as f:
                d = json.load(f)
                key = d.get("openrouter", {}).get("key") or d.get("openrouter", {}).get("apiKey")
        except Exception:
            pass

    req = urllib.request.Request(
        "https://openrouter.ai/api/v1/models",
        headers={"Authorization": f"Bearer {key}", "User-Agent": "StartupMonitor/1.0"}
    )
    with urllib.request.urlopen(req, timeout=15) as resp:
        data = json.loads(resp.read().decode("utf-8"))

    free_models = [m for m in data.get("data", []) if ":free" in m.get("id", "")]
    print(f"Всего бесплатных моделей на OpenRouter: {len(free_models)}\n")
    for m in free_models:
        mid = m.get("id")
        name = m.get("name")
        ctx = m.get("context_length")
        arch = m.get("architecture", {})
        modality = arch.get("modality", "text")
        instruct_type = arch.get("instruct_type", "N/A")
        print(f"• ID: {mid}")
        print(f"  Название: {name} | Контекст: {ctx} токенов | Модальность: {modality}")
        print(f"  Архитектура/Инструкт: {instruct_type}")
        print("-" * 60)

if __name__ == "__main__":
    main()

