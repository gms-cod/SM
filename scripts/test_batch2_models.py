# -*- coding: utf-8 -*-
import os
import sys
import time
import subprocess
import json

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

PROMPT = "Напиши метод C# для проверки валидности IPv4 адреса без регулярок (через Span или IPAddress.TryParse) и объясни его."

MODELS = [
    ("opencode/longcat-2.5-preview-free", "OpenCode Native"),
    ("openrouter/cohere/north-mini-code:free", "OpenRouter Free"),
    ("openrouter/poolside/laguna-s-2.1:free", "OpenRouter Free"),
]

for m, cat in MODELS:
    print(f"[*] Тестирование: {m}...")
    t0 = time.time()
    try:
        p = subprocess.Popen(
            ["opencode", "run", "--pure", "-m", m],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, encoding="utf-8", errors="replace"
        )
        out, err = p.communicate(input=PROMPT, timeout=60)
        dur = round(time.time() - t0, 2)
        has_code = "bool" in out or "IPAddress" in out
        print(f"    Время: {dur}s | Символов: {len(out)} | Код сгенерирован: {has_code}")
        first_line = [l for l in out.splitlines() if l.strip() and not l.startswith(">")]
        print(f"    Вывод: {first_line[0] if first_line else 'None'}")
    except Exception as e:
        print(f"    [!] Ошибка: {e}")

