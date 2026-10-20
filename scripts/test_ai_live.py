# Скрипт проверки доступности AI-оценщика StartupMonitor
import urllib.request
import json
import os

# Считываем конфигурацию из settings.json без сохранения ключей в исходном коде
settings_path = r"C:\Program Files\StartupMonitor\settings.json"
key = ""
model = "openai/gpt-oss-120b"
if os.path.exists(settings_path):
    with open(settings_path, "r", encoding="utf-8") as f:
        data = json.load(f)
        ai = data.get("AiSettings", {})
        key = ai.get("ApiKey", "")
        model = ai.get("Model", model)

if not key:
    print("API ключ не найден в конфигурации")
    exit(0)

os.makedirs('logs', exist_ok=True)
url = 'https://api.groq.com/openai/v1/chat/completions'
headers = {
    'Authorization': f'Bearer {key}',
    'Content-Type': 'application/json',
    'User-Agent': 'StartupMonitor/1.9.2 (Windows NT 10.0; Win64; x64)'
}
prompt = 'Оцени программу в автозагрузке Windows: Telegram Desktop (telegram.exe). Ответь строго JSON c полями score, category, risk, recommendation.'

payload = {
    'model': model,
    'messages': [
        {'role': 'system', 'content': 'You are a cybersecurity expert analyzing Windows startup programs. Always respond with raw valid JSON only.'},
        {'role': 'user', 'content': prompt}
    ],
    'temperature': 0.2,
    'max_tokens': 300
}
req = urllib.request.Request(url, data=json.dumps(payload).encode('utf-8'), headers=headers)
with urllib.request.urlopen(req, timeout=15) as resp:
    res = resp.read().decode('utf-8')
    with open('logs/ai_live_result.json', 'w', encoding='utf-8') as f:
        f.write(res)
    print("Успешно выполнен тестовый запрос к AI API")
