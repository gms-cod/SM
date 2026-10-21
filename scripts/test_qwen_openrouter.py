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
    "openrouter/qwen/qwen3.8-27b",
    "openrouter/qwen/qwen3.8-flash",
    "openrouter/nvidia/nemotron-3.5-super-vl:free",
]

def check_openrouter_key():
    """
    Проверяет наличие API-ключа OpenRouter в переменных окружения текущего процесса,
    в пользовательском реестре Windows и в файле конфигурации OpenCode auth.json.
    """
    # 1. Проверка текущих переменных окружения
    key = os.environ.get("OPENROUTER_API_KEY") or os.environ.get("OPENROUTER_KEY")
    if key:
        return key

    # 2. Проверка пользовательских переменных окружения Windows через реестр
    if sys.platform == "win32":
        try:
            import winreg
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Environment") as reg_key:
                val, _ = winreg.QueryValueEx(reg_key, "OPENROUTER_API_KEY")
                if val:
                    os.environ["OPENROUTER_API_KEY"] = val
                    return val
        except Exception:
            pass

    # 3. Проверка локального хранилища OpenCode auth.json
    auth_file = os.path.expanduser(r"~/.local/share/opencode/auth.json")
    if os.path.exists(auth_file):
        try:
            import json
            with open(auth_file, "r", encoding="utf-8") as f:
                data = json.load(f)
                key = data.get("openrouter", {}).get("apiKey") or data.get("openrouter", {}).get("key")
                if key:
                    os.environ["OPENROUTER_API_KEY"] = key
                    return key
        except Exception:
            pass
    return key

def validate_openrouter_api(api_key):
    """
    Выполняет прямую валидацию ключа через REST API OpenRouter (GET /api/v1/auth/key),
    возвращает данные лимитов, баланса и статуса токена.
    """
    import urllib.request
    import json

    url = "https://openrouter.ai/api/v1/auth/key"
    req = urllib.request.Request(url, headers={
        "Authorization": f"Bearer {api_key}",
        "User-Agent": "StartupMonitor-Qwen-Tester/1.0"
    })
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            return True, data.get("data", {})
    except Exception as e:
        return False, str(e)

def query_openrouter_models(api_key):
    """
    Запрашивает список доступных моделей Qwen на OpenRouter для определения точных ID моделей.
    """
    import urllib.request
    import json

    url = "https://openrouter.ai/api/v1/models"
    req = urllib.request.Request(url, headers={
        "Authorization": f"Bearer {api_key}",
        "User-Agent": "StartupMonitor-Qwen-Tester/1.0"
    })
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            models = data.get("data", [])
            qwen_models = [m for m in models if "qwen" in m.get("id", "").lower()]
            return qwen_models
    except Exception as e:
        print(f"[!] Ошибка запроса списка моделей: {e}")
        return []

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

    print(f"[+] Ключ OpenRouter обнаружен: {api_key[:12]}...{api_key[-4:]}")

    # Валидация ключа через официальный REST API OpenRouter
    print("[*] Валидация ключа через OpenRouter API...")
    ok, auth_info = validate_openrouter_api(api_key)
    if ok:
        label = auth_info.get("label", "Без метки")
        usage = auth_info.get("usage", 0)
        limit = auth_info.get("limit", "Без лимита")
        is_free = auth_info.get("is_free_tier", True)
        print(f"[+] Ключ активен! Название: '{label}', Использовано: ${usage}, Лимит: {limit}, Free Tier: {is_free}")
    else:
        print(f"[-] Ошибка валидации ключа: {auth_info}")
        return

    # Поиск доступных моделей Qwen
    print("[*] Поиск доступных моделей Qwen на OpenRouter...")
    qwen_models = query_openrouter_models(api_key)
    free_qwen = [m for m in qwen_models if ":free" in m.get("id", "")]
    print(f"[+] Найдено {len(qwen_models)} моделей Qwen (из них {len(free_qwen)} с бесплатным доступом :free):")
    for m in free_qwen[:5]:
        print(f"    - {m.get('id')} ({m.get('context_length', 'N/A')} ctx, {m.get('name')})")

    target_model = sys.argv[1] if len(sys.argv) > 1 else (free_qwen[0].get("id") if free_qwen else MODEL_CANDIDATES[0])
    # Форматирование под синтаксис opencode (префикс openrouter/)
    opencode_model = target_model if target_model.startswith("openrouter/") else f"openrouter/{target_model}"
    print(f"\n[*] Тестирование модели через OpenCode: {opencode_model}...")

    prompt = "Привет! Ты модель Qwen. Подтверди готовность к работе в экосистеме StartupMonitor, назови свою версию и кратко поприветствуй на русском языке."
    cmd = ["opencode", "run", "--pure", "-m", opencode_model]

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
        proc.wait(timeout=90)
        print("\n-------------------------")
        print("[+] Тест модели Qwen успешно завершен!")
    except Exception as e:
        print(f"\n[-] Ошибка выполнения: {e}")

if __name__ == "__main__":
    main()

