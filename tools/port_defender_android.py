#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Port Defender & Firewall Tester Mobile (Android / Cross-Platform)
Автономный мобильный инструмент с графическим веб-интерфейсом для Android.
Поддерживает реальные сокетные TCP/UDP подключения к портам 445 (SMB), 3389 (RDP), 135, 53.
"""

import sys
import os
import time
import socket
import threading
import json
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, parse_qs

PORT = 8080

# Глобальное состояние сессии тестирования
test_state = {
    "is_running": False,
    "should_stop": False,
    "target_ip": "192.168.1.100",
    "ports": [445],
    "protocol": "TCP",
    "rate": 10,
    "total": 100,
    "sent": 0,
    "success": 0,
    "refused": 0,
    "timeout": 0,
    "ban_detected": False,
    "consecutive_drops": 0,
    "logs": []
}

lock = threading.Lock()

def parse_ports(port_str):
    res = []
    parts = port_str.replace(';', ',').replace(' ', ',').split(',')
    for p in parts:
        p = p.strip()
        if not p:
            continue
        if '-' in p:
            try:
                start, end = p.split('-')
                s, e = int(start.strip()), int(end.strip())
                for port_num in range(min(s, e), max(s, e) + 1):
                    if 1 <= port_num <= 65535 and len(res) < 300:
                        res.append(port_num)
            except:
                pass
        else:
            try:
                val = int(p)
                if 1 <= val <= 65535:
                    res.append(val)
            except:
                pass
    return res if res else [445]

def add_log(msg):
    t_str = time.strftime("%H:%M:%S")
    line = f"[{t_str}] {msg}"
    with lock:
        test_state["logs"].append(line)
        if len(test_state["logs"]) > 300:
            test_state["logs"].pop(0)

def probe_tcp(ip, port, idx):
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.settimeout(0.35)
    try:
        s.connect((ip, port))
        with lock:
            test_state["success"] += 1
            test_state["consecutive_drops"] = 0
        if idx <= 5 or idx % 20 == 0:
            add_log(f"[#{idx:04d}] TCP {ip}:{port} -> ОТКРЫТ (Connected)")
    except socket.timeout:
        with lock:
            test_state["timeout"] += 1
            test_state["consecutive_drops"] += 1
            if test_state["consecutive_drops"] >= 3 and not test_state["ban_detected"]:
                test_state["ban_detected"] = True
                add_log(f"🚨 [#{idx:04d}] ОБНАРУЖЕН БАН: Серия сбросов пакетов! Port Defender заблокировал соединения.")
        if idx <= 5 or idx % 20 == 0:
            add_log(f"[#{idx:04d}] TCP {ip}:{port} -> Таймаут (Drop/Фильтрация)")
    except ConnectionRefusedError:
        with lock:
            test_state["refused"] += 1
        if idx <= 5 or idx % 20 == 0:
            add_log(f"[#{idx:04d}] TCP {ip}:{port} -> Отказ сокета (Refused)")
    except Exception as ex:
        with lock:
            test_state["refused"] += 1
        if idx <= 5 or idx % 20 == 0:
            add_log(f"[#{idx:04d}] TCP {ip}:{port} -> Сбой: {ex}")
    finally:
        try:
            s.close()
        except:
            pass

def probe_udp(ip, port, idx):
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.settimeout(0.35)
    try:
        payload = f"SMON_ANDROID_{idx}_{int(time.time()*1000)}".encode()
        s.sendto(payload, (ip, port))
        with lock:
            test_state["success"] += 1
        if idx <= 5 or idx % 20 == 0:
            add_log(f"[#{idx:04d}] UDP {ip}:{port} -> Отправлен датаграмм ({len(payload)} б)")
    except Exception as ex:
        with lock:
            test_state["refused"] += 1
        if idx <= 5 or idx % 20 == 0:
            add_log(f"[#{idx:04d}] UDP {ip}:{port} -> Ошибка UDP: {ex}")
    finally:
        try:
            s.close()
        except:
            pass

def worker_thread(target_ip, ports, is_udp, rate, total):
    add_log(f"=== Старт теста: {target_ip}:[{','.join(map(str, ports))}] ({'UDP' if is_udp else 'TCP'}) | {rate}/сек | Всего: {total} ===")
    start_time = time.time()
    interval = 1.0 / rate

    threads = []
    for i in range(1, total + 1):
        with lock:
            if test_state["should_stop"]:
                break
        
        port = ports[(i - 1) % len(ports)]
        target_func = probe_udp if is_udp else probe_tcp
        t = threading.Thread(target=target_func, args=(target_ip, port, i), daemon=True)
        t.start()
        threads.append(t)

        with lock:
            test_state["sent"] += 1

        time.sleep(interval)

    # Ожидание завершения
    for t in threads[-20:]:
        t.join(timeout=0.4)

    elapsed = time.time() - start_time
    with lock:
        test_state["is_running"] = False
        test_state["should_stop"] = False
        total_dropped = test_state["refused"] + test_state["timeout"]
        add_log(f"=== Завершено за {elapsed:.2f} сек | Отправлено: {test_state['sent']} | Открыто: {test_state['success']} | Сброшено: {total_dropped} ===")
        if test_state["ban_detected"]:
            add_log("🛡️ ВЕРДИКТ: Port Defender успешно распознал аномальную активность и активировал блокировку брандмауэра!")

HTML_PAGE = """<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
<title>Port Defender Tester Mobile</title>
<style>
* { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
body { background: #121316; color: #F8FAFC; padding: 12px; }
.card { background: #1E2025; border: 1px solid #2E323B; border-radius: 10px; padding: 12px; margin-bottom: 10px; }
.header { display: flex; align-items: center; justify-content: space-between; }
.header-left { display: flex; align-items: center; }
.icon-box { width: 44px; height: 44px; border-radius: 8px; margin-right: 10px; display: flex; align-items: center; justify-content: center; }
.title { font-size: 15px; font-weight: bold; }
.subtitle { font-size: 11px; color: #94A3B8; }
.badge-ban { background: #26EF4444; border: 1px solid #EF4444; color: #EF4444; font-size: 11px; font-weight: bold; padding: 4px 8px; border-radius: 6px; display: none; }
.row { display: flex; gap: 8px; margin-bottom: 8px; }
.col { flex: 1; display: flex; flex-direction: column; }
label { font-size: 11px; color: #94A3B8; margin-bottom: 4px; }
input, select { background: #121316; border: 1px solid #2E323B; color: #FFF; padding: 8px; border-radius: 6px; font-size: 13px; outline: none; }
input:focus, select:focus { border-color: #00D2D3; }
.presets { display: flex; gap: 6px; overflow-x: auto; padding-bottom: 4px; margin-bottom: 8px; }
.btn-preset { background: rgba(255,255,255,0.08); border: 1px solid rgba(255,255,255,0.15); color: #FFF; padding: 5px 9px; border-radius: 6px; font-size: 11px; white-space: nowrap; cursor: pointer; }
.calc-bar { background: rgba(0, 210, 211, 0.08); border: 1px solid rgba(0, 210, 211, 0.2); border-radius: 6px; padding: 8px; color: #00D2D3; font-size: 12px; font-weight: 600; margin-top: 4px; }
.btn-group { display: flex; gap: 8px; margin-bottom: 10px; }
.btn { flex: 1; padding: 12px; border: none; border-radius: 8px; font-size: 13px; font-weight: bold; cursor: pointer; }
.btn-start { background: #00D2D3; color: #000; }
.btn-stop { background: #EF4444; color: #FFF; opacity: 0.5; }
.stats { display: flex; justify-content: space-between; text-align: center; }
.stat-box { flex: 1; }
.stat-val { font-size: 14px; font-weight: bold; margin-top: 2px; }
.progress-bar { width: 100%; height: 6px; background: #121316; border-radius: 3px; overflow: hidden; margin-bottom: 8px; }
.progress-fill { height: 100%; width: 0%; background: #00D2D3; transition: width 0.15s; }
.log-box { background: #0A0B0D; border: 1px solid #2E323B; border-radius: 6px; padding: 8px; height: 180px; overflow-y: auto; font-family: monospace; font-size: 10.5px; color: #94A3B8; white-space: pre-wrap; word-break: break-all; }
</style>
</head>
<body>

<div class="card header">
  <div class="header-left">
    <div class="icon-box">
      <svg viewBox="0 0 24 24" width="38" height="38">
        <path fill="#E11D48" d="M12.45,2.15 C14.992,4.057 17.587,5 20.25,5 A0.75,0.75 0 0,1 21,5.75 V11 C21,16.001 18.042,19.676 12.275,21.948 A0.75,0.75 0 0,1 11.725,21.948 C5.958,19.676 3,16 3,11 V5.75 A0.75,0.75 0 0,1 3.75,5 C6.413,5 9.008,4.057 11.55,2.15 A0.75,0.75 0 0,1 12.45,2.15 Z"/>
        <path fill="#FBBF24" stroke="#B45309" stroke-width="0.2" d="M13.3,4.8 L7.8,12.2 H12.2 L10.1,19.2 L16.4,11.0 H12.0 Z"/>
      </svg>
    </div>
    <div>
      <div class="title">Port Defender Mobile</div>
      <div class="subtitle">Стенд тестирования брандмауэра</div>
    </div>
  </div>
  <div id="badgeBan" class="badge-ban">🚨 БАН ОБНАРУЖЕН!</div>
</div>

<div class="card">
  <div class="row">
    <div class="col" style="flex: 0.8;">
      <label>Протокол</label>
      <select id="protocol" onchange="recalc()">
        <option value="TCP" selected>TCP</option>
        <option value="UDP">UDP</option>
      </select>
    </div>
    <div class="col" style="flex: 2;">
      <label>IP-адрес цели</label>
      <input type="text" id="targetIp" value="192.168.1.100" oninput="recalc()">
    </div>
  </div>

  <div class="row">
    <div class="col" style="flex: 1.2;">
      <label>Порт(ы)</label>
      <input type="text" id="targetPort" value="445" oninput="recalc()">
    </div>
    <div class="col">
      <label>Поп / сек</label>
      <input type="number" id="rate" value="10" oninput="recalc()">
    </div>
    <div class="col">
      <label>Всего</label>
      <input type="number" id="total" value="100" oninput="recalc()">
    </div>
  </div>

  <div class="presets">
    <button class="btn-preset" onclick="setPreset('445', 'TCP')">SMB 445</button>
    <button class="btn-preset" onclick="setPreset('3389', 'TCP')">RDP 3389</button>
    <button class="btn-preset" onclick="setPreset('135', 'TCP')">RPC 135</button>
    <button class="btn-preset" onclick="setPreset('80', 'TCP')">HTTP 80</button>
    <button class="btn-preset" onclick="setPreset('53', 'UDP')">DNS 53</button>
    <button class="btn-preset" style="color:#00D2D3; border-color:#00D2D3;" onclick="setWanPreset()">WAN 1.1.1.1</button>
  </div>

  <div id="calcText" class="calc-bar">Расчётное время: 10.0 сек (100 попыток при 10 поп/сек)</div>
</div>

<div class="btn-group">
  <button id="btnStart" class="btn btn-start" onclick="startTest()">🚀 Начать тест</button>
  <button id="btnStop" class="btn btn-stop" onclick="stopTest()" disabled>⏹ Стоп</button>
</div>

<div class="card">
  <div class="progress-bar"><div id="progressFill" class="progress-fill"></div></div>
  <div class="stats">
    <div class="stat-box">
      <div style="font-size:10px; color:#94A3B8;">Отправлено</div>
      <div id="statSent" class="stat-val">0 / 100</div>
    </div>
    <div class="stat-box">
      <div style="font-size:10px; color:#10B981;">Открыт</div>
      <div id="statSuccess" class="stat-val" style="color:#10B981;">0</div>
    </div>
    <div class="stat-box">
      <div style="font-size:10px; color:#F59E0B;">Отказ</div>
      <div id="statRefused" class="stat-val" style="color:#F59E0B;">0</div>
    </div>
    <div class="stat-box">
      <div style="font-size:10px; color:#EF4444;">Drop/Бан</div>
      <div id="statTimeout" class="stat-val" style="color:#EF4444;">0</div>
    </div>
  </div>
</div>

<div class="card">
  <div style="font-size:11px; color:#94A3B8; margin-bottom:6px;">Журнал сетевых испытаний:</div>
  <div id="logBox" class="log-box"></div>
</div>

<script>
let polling = null;

function recalc() {
  const r = parseInt(document.getElementById('rate').value) || 0;
  const t = parseInt(document.getElementById('total').value) || 0;
  const p = document.getElementById('protocol').value;
  if (r > 0 && t > 0) {
    const s = (t / r).toFixed(1);
    document.getElementById('calcText').innerText = `Расчётное время: ${s} сек (${t} поп при ${r} поп/сек, ${p})`;
  }
}

function setPreset(port, proto) {
  document.getElementById('targetPort').value = port;
  document.getElementById('protocol').value = proto;
  recalc();
}

function setWanPreset() {
  document.getElementById('targetIp').value = '1.1.1.1';
  document.getElementById('targetPort').value = '80';
  document.getElementById('rate').value = '1';
  document.getElementById('total').value = '3';
  document.getElementById('protocol').value = 'TCP';
  recalc();
}

async function startTest() {
  const ip = document.getElementById('targetIp').value;
  const port = document.getElementById('targetPort').value;
  const proto = document.getElementById('protocol').value;
  const rate = document.getElementById('rate').value;
  const total = document.getElementById('total').value;

  const res = await fetch(`/api/start?ip=${encodeURIComponent(ip)}&port=${encodeURIComponent(port)}&proto=${proto}&rate=${rate}&total=${total}`);
  if (res.ok) {
    document.getElementById('btnStart').disabled = true;
    document.getElementById('btnStart').style.opacity = '0.5';
    document.getElementById('btnStop').disabled = false;
    document.getElementById('btnStop').style.opacity = '1';
    document.getElementById('badgeBan').style.display = 'none';
    if (!polling) polling = setInterval(pollStatus, 300);
  }
}

async function stopTest() {
  await fetch('/api/stop');
}

async function pollStatus() {
  try {
    const res = await fetch('/api/status');
    const data = await res.json();
    
    document.getElementById('statSent').innerText = `${data.sent} / ${data.total}`;
    document.getElementById('statSuccess').innerText = data.success;
    document.getElementById('statRefused').innerText = data.refused;
    document.getElementById('statTimeout').innerText = data.timeout;

    const pct = data.total > 0 ? (data.sent / data.total) * 100 : 0;
    document.getElementById('progressFill').style.width = `${pct}%`;

    if (data.ban_detected) {
      document.getElementById('badgeBan').style.display = 'block';
    }

    const logBox = document.getElementById('logBox');
    logBox.innerText = data.logs.join('\\n');
    logBox.scrollTop = logBox.scrollHeight;

    if (!data.is_running) {
      document.getElementById('btnStart').disabled = false;
      document.getElementById('btnStart').style.opacity = '1';
      document.getElementById('btnStop').disabled = true;
      document.getElementById('btnStop').style.opacity = '0.5';
    }
  } catch (e) {}
}

setInterval(pollStatus, 1000);
</script>
</body>
</html>
"""

class RequestHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        url = urlparse(self.path)
        if url.path == "/" or url.path == "/index.html":
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.end_headers()
            self.wfile.write(HTML_PAGE.encode("utf-8"))
        elif url.path == "/api/status":
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.end_headers()
            with lock:
                payload = json.dumps(test_state).encode("utf-8")
            self.wfile.write(payload)
        elif url.path == "/api/start":
            qs = parse_qs(url.query)
            target_ip = qs.get("ip", ["127.0.0.1"])[0]
            port_str = qs.get("port", ["445"])[0]
            proto = qs.get("proto", ["TCP"])[0].upper()
            rate = int(qs.get("rate", ["10"])[0])
            total = int(qs.get("total", ["100"])[0])
            ports = parse_ports(port_str)

            with lock:
                if not test_state["is_running"]:
                    test_state["is_running"] = True
                    test_state["should_stop"] = False
                    test_state["target_ip"] = target_ip
                    test_state["ports"] = ports
                    test_state["protocol"] = proto
                    test_state["rate"] = rate
                    test_state["total"] = total
                    test_state["sent"] = 0
                    test_state["success"] = 0
                    test_state["refused"] = 0
                    test_state["timeout"] = 0
                    test_state["ban_detected"] = False
                    test_state["consecutive_drops"] = 0
                    test_state["logs"] = []

                    t = threading.Thread(
                        target=worker_thread,
                        args=(target_ip, ports, proto == "UDP", rate, total),
                        daemon=True
                    )
                    t.start()

            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(b'{"status":"started"}')
        elif url.path == "/api/stop":
            with lock:
                test_state["should_stop"] = True
                add_log("⏹ Остановка теста по запросу пользователя...")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(b'{"status":"stopping"}')
        else:
            self.send_response(404)
            self.end_headers()

    def log_message(self, format, *args):
        # Отключаем мусорный лог HTTP запросов в терминале
        pass

def main():
    server = HTTPServer(("0.0.0.0", PORT), RequestHandler)
    local_ip = "127.0.0.1"
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        local_ip = s.getsockname()[0]
        s.close()
    except:
        pass

    print("=" * 65)
    print("🛡️  PORT DEFENDER & FIREWALL TESTER MOBILE (ANDROID)")
    print("=" * 65)
    print(f"Сервер мобильного интерфейса запущен!")
    print(f"Локально на телефоне:   http://localhost:{PORT}")
    print(f"По сети Wi-Fi:          http://{local_ip}:{PORT}")
    print("-" * 65)
    print("Откройте эту ссылку в браузере телефона для управления тестом.")
    print("Для завершения работы нажмите Ctrl+C.")
    print("=" * 65)

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nОстановка сервера...")
        server.server_close()

if __name__ == "__main__":
    main()

