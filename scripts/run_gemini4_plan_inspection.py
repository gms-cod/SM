# -*- coding: utf-8 -*-
"""
Экспертиза комплексного плана редизайна, 0.0% CPU и headless-защиты
через Google Gemini 4 Argon на OpenRouter.
"""
import os
import sys
import json
import http.client

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

BASE_DIR = r"d:\Nas_sync\Coding\StartupMonitor"
PLAN_FILE = os.path.join(BASE_DIR, "docs", "GLOBAL_AUDIT_AND_FIREWALL_MASTER_PLAN.md")
RESULT_FILE = os.path.join(BASE_DIR, "docs", "GEMINI_4_PLAN_INSPECTION_REPORT.md")

with open(PLAN_FILE, "r", encoding="utf-8") as f:
    plan_content = f.read()

auth_file = os.path.expanduser(r"~/.local/share/opencode/auth.json")
key = None
if os.path.exists(auth_file):
    try:
        with open(auth_file, "r", encoding="utf-8") as f:
            d = json.load(f)
            key = d.get("openrouter", {}).get("key") or d.get("openrouter", {}).get("apiKey")
    except Exception:
        pass

if not key:
    print("Ошибка: OpenRouter ключ не найден в auth.json")
    sys.exit(1)

prompt = f"""Ты — главный системный архитектор операционных систем Windows и ведущий эксперт по кибербезопасности и .NET 8 (Google Gemini 4 Argon).
Проведи строгую, критическую и детальную инспекцию генерального плана доработок проекта StartupMonitor:

{plan_content}

Требования к твоей инспекции:
1. Анализ 0.0% CPU Idle и фонового энергосбережения:
   - Реалистичность и надежность перехода в глубокий сон при закрытом UI.
   - Кеширование COM правил Windows Firewall (TTL 60 с): есть ли подводные камни при стороннем изменении правил другими программами?
2. Headless-защита и безопасность без запущенного UI:
   - Надежность автозапуска UI в пользовательской сессии (Session 0 Isolation / WTSEnumerateSessions / CreateProcessAsUser).
   - Приоритет черного списка над белым: отсутствие лазеек при совпадении путей или многопроцессных браузерах (Chrome, Opera, Edge).
   - Жёсткое правило: белый список = 0 алертов, черный список = 0 алертов + немедленный сброс сокета / блокировка в брандмауэре.
3. Оценка редизайна страницы Firewall:
   - Архитектура 5 вкладок (Соединения, Белый список, Чёрный список, Системные правила, Port Defender).
   - Эргономика управления списками (добавление через диалог, удаление, реактивное обновление).
4. Проверка возврата «Снимков системы»:
   - Интеграция пункта меню NavItemSnapshots в левую панель навигации MainWindow.xaml.
5. Финальный вердикт:
   - Одобрен ли план к реализации?
   - Какие критические рекомендации или штрихи необходимо добавить перед написанием кода?

Отвечай развернуто, структурированно, профессионально и на чистом русском языке.
"""

print("Отправка запроса на детальную инспекцию в Google Gemini 4 Argon...")
conn = http.client.HTTPSConnection("openrouter.ai", timeout=120)
payload = json.dumps({
    "model": "google/gemini-4-argon",
    "messages": [
        {"role": "system", "content": "You are Google Gemini 4 Argon, an elite Windows OS kernel, security, and .NET architecture reviewer."},
        {"role": "user", "content": prompt}
    ],
    "max_tokens": 2400,
    "temperature": 0.2
})

conn.request("POST", "/api/v1/chat/completions", payload, {
    "Authorization": f"Bearer {key}",
    "Content-Type": "application/json"
})

res = conn.getresponse()
raw_data = res.read().decode("utf-8")
if res.status != 200:
    print(f"Ошибка API {res.status}: {raw_data}")
    sys.exit(1)

data = json.loads(raw_data)
review_text = data["choices"][0]["message"]["content"]

with open(RESULT_FILE, "w", encoding="utf-8") as f:
    f.write(review_text)

print(f"Экспертное заключение Gemini 4 Argon успешно получено и сохранено в {RESULT_FILE}!")
