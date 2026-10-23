# -*- coding: utf-8 -*-
"""
Скрипт прямого запуска и тестирования модели Google Gemini 4 Argon
через OpenCode и облачный шлюз OpenRouter.
"""
import os
import sys
import subprocess

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

MODEL_ID = "openrouter/google/gemini-4-argon"

def run_direct_api(prompt, key):
    import http.client
    import json

    conn = http.client.HTTPSConnection("openrouter.ai")
    payload = json.dumps({
        "model": "google/gemini-4-argon",
        "messages": [{"role": "user", "content": prompt}],
        "max_tokens": 1500
    })
    conn.request("POST", "/api/v1/chat/completions", payload, {
        "Authorization": f"Bearer {key}",
        "Content-Type": "application/json"
    })
    res = conn.getresponse()
    data = json.loads(res.read().decode("utf-8"))
    if res.status == 200:
        return data["choices"][0]["message"]["content"]
    else:
        err = data.get("error", {}).get("message", str(data))
        raise RuntimeError(f"OpenRouter HTTP {res.status}: {err}")

def main():
    print("=" * 70)
    print("ЗАПУСК GOOGLE GEMINI 4 ARGON ЧЕРЕЗ OPENROUTER")
    print(f"Модель: {MODEL_ID}")
    print("=" * 70)

    key = os.environ.get("OPENROUTER_API_KEY")
    if not key:
        auth_file = os.path.expanduser(r"~/.local/share/opencode/auth.json")
        if os.path.exists(auth_file):
            try:
                import json
                with open(auth_file, "r", encoding="utf-8") as f:
                    d = json.load(f)
                    key = d.get("openrouter", {}).get("key") or d.get("openrouter", {}).get("apiKey")
            except Exception:
                pass

    if not key:
        print("[-] API ключ OpenRouter не обнаружен в OPENROUTER_API_KEY!")
        return

    prompt = sys.argv[1] if len(sys.argv) > 1 else (
        "Привет! Ты Gemini 4 Argon. Проанализируй этот фрагмент кода C#:\n"
        "public void Process() { lock(this) { Thread.Sleep(100); } }\n"
        "Объясни, почему блокировка lock(this) является плохой практикой и предложи правильный вариант с private readonly object."
    )

    print(f"\n[*] Промпт:\n{prompt}\n")
    print("[*] Вызов Gemini 4 Argon через прямой OpenRouter API шлюз...")

    try:
        reply = run_direct_api(prompt, key)
        print("\n--- Ответ Gemini 4 Argon ---")
        print(reply)
        print("\n----------------------------")
        print("[+] Выполнение успешно завершено!")
    except Exception as e:
        print(f"[-] Ошибка выполнения: {e}")

if __name__ == "__main__":
    main()
