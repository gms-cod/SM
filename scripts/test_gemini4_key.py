# -*- coding: utf-8 -*-
import os
import urllib.request
import urllib.error
import json
import sys

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

KEY = os.environ.get("GEMINI_API_KEY", "")

CANDIDATES = [
    "gemini-3.8-flash",
    "gemini-3.5-flash",
    "gemini-3.5-flash-lite",
    "gemini-3.1-flash-lite",
    "gemini-3.1-pro-preview",
]

def test_model(model_id):
    url = f"https://generativelanguage.googleapis.com/v1beta/models/{model_id}:generateContent?key={KEY}"
    payload = json.dumps({
        "contents": [{"parts": [{"text": "Привет! Назови свою модель и подтверди готовность к работе."}]}]
    }).encode("utf-8")

    req = urllib.request.Request(url, data=payload, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            text = data["candidates"][0]["content"]["parts"][0]["text"]
            print(f"[+] {model_id}: УСПЕХ!")
            print(f"    Ответ: {text[:150]}...")
            return True
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="replace")
        try:
            err_json = json.loads(body)
            msg = err_json.get("error", {}).get("message", body)
        except Exception:
            msg = body
        print(f"[-] {model_id}: HTTP {e.code} -> {msg[:120]}")
        return False
    except Exception as e:
        print(f"[-] {model_id}: Ошибка {e}")
        return False

def main():
    print("=" * 70)
    print("ТЕСТИРОВАНИЕ КЛЮЧА GOOGLE GEMINI НА МОДЕЛЯХ СЕМЕЙСТВА")
    print("=" * 70)
    for m in CANDIDATES:
        test_model(m)

if __name__ == "__main__":
    main()
