# -*- coding: utf-8 -*-
"""
Тестирование всех моделей Google 4 поколения в официальном Google Generative Language API
для нахождения доступных по бесплатному тарифу ключа.
"""
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

# Получаем все модели из API и отбираем те, что содержат '4', 'antigravity' или 'gemini-4'
def get_target_models():
    url = f"https://generativelanguage.googleapis.com/v1beta/models?key={KEY}"
    try:
        req = urllib.request.Request(url)
        with urllib.request.urlopen(req, timeout=10) as r:
            data = json.loads(r.read().decode("utf-8"))
            models = []
            for m in data.get("models", []):
                mid = m.get("name", "").replace("models/", "")
                methods = m.get("supportedGenerationMethods", [])
                if "generateContent" in methods:
                    if "4" in mid or "antigravity" in mid:
                        models.append(mid)
            return models
    except Exception as e:
        print(f"[-] Ошибка получения списка моделей: {e}")
        return []

def test_model(model_id):
    url = f"https://generativelanguage.googleapis.com/v1beta/models/{model_id}:generateContent?key={KEY}"
    payload = json.dumps({
        "contents": [{"parts": [{"text": "Сколько будет 2+2? Ответь одним словом."}]}]
    }).encode("utf-8")

    req = urllib.request.Request(url, data=payload, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            text = data["candidates"][0]["content"]["parts"][0]["text"].strip()
            print(f"[✓] РАБОТАЕТ: {model_id} -> '{text}'")
            return True
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="replace")
        try:
            err = json.loads(body).get("error", {}).get("message", body)
        except Exception:
            err = body
        print(f"[-] {model_id} (HTTP {e.code}): {err[:80]}")
        return False
    except Exception as e:
        print(f"[-] {model_id}: {e}")
        return False

def main():
    print("=" * 70)
    print("СКАНИРОВАНИЕ БЕСПЛАТНОЙ ДОСТУПНОСТИ МОДЕЛЕЙ GOOGLE 4 ПОКОЛЕНИЯ")
    print("=" * 70)
    targets = get_target_models()
    print(f"Найдено кандидатов 4 поколения: {len(targets)}")
    for t in targets:
        print(" -", t)
    print("\nЗапуск проверочных запросов...\n")

    working = []
    for t in targets:
        if test_model(t):
            working.append(t)

    print("\n" + "=" * 70)
    print(f"ИТОГ: Успешно работают {len(working)} моделей:")
    for w in working:
        print(f" -> {w}")
    print("=" * 70)

if __name__ == "__main__":
    main()

