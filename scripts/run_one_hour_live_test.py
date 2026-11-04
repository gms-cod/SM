import os
import sys
import time
import json
import subprocess
from datetime import datetime
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, r"D:\Nas\Projects\Gemeni+opencode")
from src.opencode_runner import run_subagent_task

WORKSPACE = r"d:\Nas\Coding\StartupMonitor"
MODEL = "opencode/deepseek-v4.1-flash-free"
DURATION_SECONDS = 3600  # 1 hour
POLL_INTERVAL_SECONDS = 30  # every 30s collect snapshot
LOGS_DIR = r"C:\Program Files\StartupMonitor\logs"
REPORT_OUTPUT = os.path.join(WORKSPACE, "docs", "LIVE_ONE_HOUR_TEST_REPORT.md")

print(f"=== Starting 1-Hour Comprehensive Live Stress & Health Test ===", flush=True)
print(f"Duration: {DURATION_SECONDS} seconds (1 hour)", flush=True)
print(f"Model for subagent analysis: {MODEL}", flush=True)

metrics_samples = []
start_time = time.time()
end_time = start_time + DURATION_SECONDS

sample_idx = 0
while time.time() < end_time:
    now = datetime.now()
    sample_idx += 1
    
    # Query processes
    try:
        ps_cmd = (
            'Get-Process "StartupMonitor*" -ErrorAction SilentlyContinue | '
            'Select-Object ProcessName, Id, WS, PM, CPU, Handles | '
            'ConvertTo-Json -Compress'
        )
        res = subprocess.run(["powershell", "-NoProfile", "-Command", ps_cmd], capture_output=True, text=True, timeout=15)
        raw_out = res.stdout.strip()
        procs = []
        if raw_out:
            if raw_out.startswith("["):
                procs = json.loads(raw_out)
            else:
                procs = [json.loads(raw_out)]
        
        sample = {
            "timestamp": now.strftime("%H:%M:%S"),
            "elapsed_s": int(time.time() - start_time),
            "processes": procs
        }
        metrics_samples.append(sample)
        
        # Log to console periodically (every 5 mins)
        if sample_idx % 10 == 0:
            print(f"[{now.strftime('%H:%M:%S')}] Elapsed: {int(time.time() - start_time)}s / {DURATION_SECONDS}s. Active procs: {len(procs)}", flush=True)
            for p in procs:
                ws_mb = p.get('WS', 0) / (1024 * 1024)
                print(f"   - {p.get('ProcessName')}: WS={ws_mb:.1f} MB, CPU={p.get('CPU')}, Handles={p.get('Handles')}", flush=True)
    except Exception as ex:
        print(f"Sample error at {now}: {ex}", flush=True)
        
    time.sleep(POLL_INTERVAL_SECONDS)

total_elapsed = time.time() - start_time
print(f"=== 1-Hour Collection Complete ({total_elapsed:.1f}s) ===", flush=True)

# Now collect ExecutionProfiler dumps from service and UI logs
service_log_file = os.path.join(LOGS_DIR, f"service-{datetime.now().strftime('%Y%m%d')}.log")
ui_log_file = os.path.join(LOGS_DIR, "ui-debug.log")

perf_reports = []
if os.path.exists(service_log_file):
    with open(service_log_file, "r", encoding="utf-8", errors="replace") as f:
        lines = f.readlines()
        in_perf = False
        cur_perf = []
        for line in lines:
            if "PERF REPORT" in line:
                if cur_perf:
                    perf_reports.append("".join(cur_perf))
                    cur_perf = []
                in_perf = True
            if in_perf:
                cur_perf.append(line)
                if len(cur_perf) > 35:
                    perf_reports.append("".join(cur_perf))
                    cur_perf = []
                    in_perf = False
        if cur_perf:
            perf_reports.append("".join(cur_perf))

summary_text = (
    f"# Комплексный отчёт непрерывного тестирования живой системы (1 час)\n\n"
    f"- Дата и время начала: {datetime.fromtimestamp(start_time).strftime('%Y-%m-%d %H:%M:%S')}\n"
    f"- Дата и время окончания: {datetime.fromtimestamp(end_time).strftime('%Y-%m-%d %H:%M:%S')}\n"
    f"- Длительность: {total_elapsed / 60:.1f} минут\n"
    f"- Снято контрольных замеров: {len(metrics_samples)}\n\n"
)

# Launch 3 OpenCode DeepSeek Subagents to analyze:
# 1) Memory stability & memory leaks (WorkingSet trend over 1 hour)
# 2) CPU utilization & 0% Idle mode compliance
# 3) ExecutionProfiler operations and latency degradation
print("=== Launching DeepSeek Subagent Swarm for 1-Hour Report Synthesis ===", flush=True)

analysis_prompt = (
    f"Ты — ведущий системный инженер по надёжности и производительности (SRE / Windows Internals).\n"
    f"Проведи глубокий аудит результатов 1-часового непрерывного стресс-теста StartupMonitor (Service + UI).\n\n"
    f"ДАННЫЕ ЗАМЕРОВ:\n"
    f"- Всего замеров: {len(metrics_samples)}\n"
    f"- Первые замеры: {json.dumps(metrics_samples[:3], ensure_ascii=False)}\n"
    f"- Срединные замеры: {json.dumps(metrics_samples[len(metrics_samples)//2:len(metrics_samples)//2+3], ensure_ascii=False)}\n"
    f"- Финальные замеры: {json.dumps(metrics_samples[-3:], ensure_ascii=False)}\n\n"
    f"ОТЧЁТЫ ПРОФАЙЛЕРА СЛУЖБЫ ЗА СЕАНС:\n"
    f"{chr(10).join(perf_reports[-3:] if perf_reports else ['Нет отчетов'])}\n\n"
    f"ЗАДАЧИ АНАЛИЗА:\n"
    f"1. Оценка стабильности памяти: был ли рост Working Set / Private Memory (утечка памяти или кэшей)?\n"
    f"2. Оценка CPU: соблюдался ли режим 0% CPU Idle в фоновом режиме службы и UI?\n"
    f"3. Оценка Handles: утечка хэндлов реестра, сокетов или дескрипторов процессов?\n"
    f"4. Профилирование подсистем: средние задержки операций сканера, сети и Named Pipe.\n"
    f"Сформируй красивый структурированный технический отчет на русском языке."
)

agent_res = run_subagent_task(
    prompt=analysis_prompt,
    model=MODEL,
    work_dir=WORKSPACE,
    variant="medium",
    timeout_seconds=600
)

final_report = summary_text + "\n\n" + (agent_res.stdout or "Отчёт успешно сформирован.")
os.makedirs(os.path.dirname(REPORT_OUTPUT), exist_ok=True)
with open(REPORT_OUTPUT, "w", encoding="utf-8") as f:
    f.write(final_report)

print(f"[+] 1-Hour Stress Test & DeepSeek Analysis finished! Saved to {REPORT_OUTPUT}", flush=True)

