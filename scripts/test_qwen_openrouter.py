# -*- coding: utf-8 -*-
"""
Скрипт проверки и запуска моделей семейства Qwen (включая Qwen 3.8 27B / Qwen 2.5 Coder)
в OpenCode через бесплатный облачный шлюз OpenRouter.
"""
import os
import sys
import subprocess

# Настройка вывода UTF-8 для Windows
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

MODEL_CANDIDATES = [
    "openrouter/qwen/qwen-2.5-coder-32b-instruct:free",
    "openrouter/qwen/qwen-2.5-72b-instruct:free",
    "openrouter/qwen/qwen3.8-27b",
]

def check_openrouter_key():
    key = os.environ.get("OPENROUTER_API_KEY") or os.environ.get("OPENROUTER_KEY")
    if not key:
        auth_file = os.path.expanduser(r"~/.local/share/opencode/auth.json")
        if os.path.exists(auth_file):
            try:
                import json
                with open(auth_file, "r", encoding="utf-8") as f:
                    data = json.load(f)
                    key = data.get("openrouter", {}).get("apiKey")
            except Exception:
                pass
    return key

def main():
    print("=" * 70)
    print("ПРОВЕРКА ПОДКЛЮЧЕНИЯ QWEN К OPENCODE (OPENROUTER CLOUD)")
    print("=" * 70)

    api_key = check_openrouter_key()
    if not api_key:
        print("\n[!] Ключ OpenRouter API не обнаружен.")
        print("\nИнструкция для бесплатного подключения Qwen 3.8 / Qwen 2.5:")
        print("1. Перейдите на https://openrouter.ai/keys (бесплатно, вход через GitHub/Google).")
        print("2. Нажмите 'Create Key' и скопируйте сгенерированный ключ (начинается на sk-or-v1-...).")
        print("3. Установите ключ в системе (в PowerShell):")
        print("   [System.Environment]::SetEnvironmentVariable('OPENROUTER_API_KEY', 'sk-or-v1-...', 'User')")
        print("   или передайте в текущей сессии: $env:OPENROUTER_API_KEY='sk-or-v1-...'")
        print("4. После этого запустите данный скрипт повторно.")
        print("=" * 70)
        return

    print("[+] Ключ OpenRouter обнаружен в окружении.")
    target_model = sys.argv[1] if len(sys.argv) > 1 else MODEL_CANDIDATES[0]
    print(f"[*] Тестирование модели: {target_model}...")

    prompt = "Привет! Ты запущен в среде StartupMonitor. Подтверди готовность к работе и назови свою архитектуру."
    cmd = ["opencode", "run", "--pure", "-m", target_model]

    try:
        proc = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace"
        )
        proc.stdin.write(prompt)
        proc.stdin.close()

        print("\n--- Ответ модели Qwen ---")
        for line in proc.stdout:
            print(line, end="")
        proc.wait(timeout=60)
        print("\n-------------------------")
        print("[+] Тест модели Qwen успешно завершен!")
    except Exception as e:
        print(f"\n[-] Ошибка выполнения: {e}")

if __name__ == "__main__":
    main()
