# -*- coding: utf-8 -*-
"""
Генератор высокодетализированных реалистичных PNG-макетов (1920x1080) для 8 вариантов редизайна Startup Monitor.
Использует библиотеку Pillow для векторной отрисовки интерфейсов, HUD-элементов, карточек и графиков.
"""
import os
import math
from PIL import Image, ImageDraw, ImageFont

BASE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUTPUT_DIR = os.path.join(BASE_DIR, "docs", "images", "designs")
os.makedirs(OUTPUT_DIR, exist_ok=True)

WIDTH = 1920
HEIGHT = 1080

def get_font(size, bold=False):
    font_paths = [
        "C:\\Windows\\Fonts\\segoeui.ttf",
        "C:\\Windows\\Fonts\\arial.ttf",
        "C:\\Windows\\Fonts\\tahoma.ttf"
    ]
    if bold:
        font_paths = [
            "C:\\Windows\\Fonts\\segoeuib.ttf",
            "C:\\Windows\\Fonts\\arialbd.ttf"
        ] + font_paths

    for fp in font_paths:
        if os.path.exists(fp):
            try:
                return ImageFont.truetype(fp, size)
            except Exception:
                pass
    return ImageFont.load_default()

def draw_header(draw, title, subtitle, bg_color, text_color, sub_color):
    draw.rectangle([0, 0, WIDTH, 70], fill=bg_color)
    draw.text((30, 18), "STARTUP MONITOR  v1.9.7", font=get_font(20, bold=True), fill="#00F0FF")
    draw.text((360, 20), f"|  {title}", font=get_font(18, bold=True), fill=text_color)
    draw.text((WIDTH - 420, 22), subtitle, font=get_font(14), fill=sub_color)
    draw.line([0, 70, WIDTH, 70], fill="#1F2937", width=2)

def draw_badge(draw, x, y, text, bg, fg, font_size=12):
    font = get_font(font_size, bold=True)
    bbox = font.getbbox(text)
    w = bbox[2] - bbox[0] + 16
    h = 24
    draw.rounded_rectangle([x, y, x + w, y + h], radius=6, fill=bg)
    draw.text((x + 8, y + 4), text, font=font, fill=fg)
    return x + w + 8

# ==============================================================================
# 1. CYBER COMMAND CENTER (EDR HUD)
# ==============================================================================
def render_variant_1():
    img = Image.new("RGB", (WIDTH, HEIGHT), "#080B10")
    draw = ImageDraw.Draw(img)
    draw_header(draw, "КОНЦЕПТ 1: CYBER COMMAND CENTER (EDR HUD)", "ТАКТИЧЕСКИЙ РЕЖИМ: АКТИВЕН", "#0E131F", "#FFFFFF", "#9CA3AF")

    # Верхний HUD-бар (Телеметрия)
    draw.rounded_rectangle([30, 90, WIDTH - 30, 200], radius=12, fill="#0F172A", outline="#00F0FF", width=2)
    draw.text((50, 105), "СИСТЕМНЫЙ СЕНСОР В РЕАЛЬНОМ ВРЕМЕНИ", font=get_font(14, bold=True), fill="#00F0FF")

    metrics = [
        ("ЦПУ ЯДРО", "1.4%", "Стабильно (Eco-Sensing)", "#10B981"),
        ("ОПЕРАТИВНАЯ ПАМЯТЬ", "64 МБ", "Working Set (Без утечек)", "#3B82F6"),
        ("СЕТЕВЫЕ СОКЕТЫ", "18 активных", "0 заблокировано", "#F59E0B"),
        ("ТОЧКИ СТАРТА", "312 объектов", "100% крипто-валидировано", "#8B5CF6"),
        ("СТАТУС ЗАЩИТЫ", "ARMED 24/7", "Авто-политики активны", "#10B981")
    ]
    for i, (m_title, m_val, m_desc, m_color) in enumerate(metrics):
        bx = 50 + i * 360
        draw.text((bx, 130), m_title, font=get_font(12, bold=True), fill="#64748B")
        draw.text((bx, 150), m_val, font=get_font(26, bold=True), fill=m_color)
        draw.text((bx, 180), m_desc, font=get_font(11), fill="#94A3B8")

    # Левая колонка: тактическая карта автозапуска и процессов
    draw.rounded_rectangle([30, 220, 1200, 1040], radius=12, fill="#0F172A", outline="#1E293B", width=1)
    draw.text((50, 240), "АКТИВНЫЙ МОНИТОРИНГ УГРОЗ И ПРОЦЕССОВ", font=get_font(16, bold=True), fill="#FFFFFF")
    
    headers = ["PID", "ИМЯ ПРОЦЕССА / ПУТЬ", "ПОДПИСЬ", "KASPERSKY", "AI РЕЙТИНГ", "ДЕЙСТВИЯ"]
    hx_offsets = [50, 120, 520, 710, 890, 1050]
    for h, ox in zip(headers, hx_offsets):
        draw.text((ox, 275), h, font=get_font(12, bold=True), fill="#64748B")
    draw.line([50, 298, 1180, 298], fill="#334155", width=1)

    rows = [
        ("4812", "Telegram.exe (C:\\Program Files\\Telegram\\...)", "Valid (Telegram FZ)", "Clean", "5.0 ★ Безопасен", "#10B981"),
        ("1204", "Discord.exe (C:\\Users\\...\\Discord\\...)", "Valid (Discord Inc)", "Clean", "4.8 ★ Безопасен", "#10B981"),
        ("8920", "powershell.exe -enc JAB4ACAAPQ... (TEMP)", "Unsigned / Base64", "Malware", "1.0 ★ КРИТИЧНО", "#EF4444"),
        ("3140", "Steam.exe (Delayed 45s)", "Valid (Valve Corp)", "Clean", "4.9 ★ Отложен", "#F59E0B"),
        ("7108", "svchost.exe (System Host)", "Valid (Microsoft OS)", "Clean", "5.0 ★ Ядро", "#3B82F6"),
        ("6244", "python.exe (run_swarm.py)", "Unsigned (Local)", "Clean", "4.5 ★ Скрипт", "#10B981")
    ]
    for idx, (pid, name, sig, kasp, ai, status_color) in enumerate(rows):
        ry = 315 + idx * 60
        draw.rounded_rectangle([45, ry - 6, 1185, ry + 42], radius=6, fill="#1E293B" if idx % 2 == 0 else "#151F32")
        draw.text((50, ry + 8), pid, font=get_font(13), fill="#94A3B8")
        draw.text((120, ry + 8), name[:48], font=get_font(13, bold=True), fill="#FFFFFF")
        
        # Бейджи
        sig_bg = "#064E3B" if "Valid" in sig else "#7F1D1D"
        sig_fg = "#34D399" if "Valid" in sig else "#F87171"
        draw_badge(draw, 520, ry + 5, sig, sig_bg, sig_fg, font_size=11)
        
        k_bg = "#064E3B" if kasp == "Clean" else "#7F1D1D"
        k_fg = "#34D399" if kasp == "Clean" else "#F87171"
        draw_badge(draw, 710, ry + 5, kasp, k_bg, k_fg, font_size=11)

        draw_badge(draw, 890, ry + 5, ai, "#1E3A8A" if "5.0" in ai or "4." in ai else "#7F1D1D", "#93C5FD" if "5.0" in ai or "4." in ai else "#FCA5A5", font_size=11)
        
        # Кнопка действия
        act_bg = "#EF4444" if "КРИТИЧНО" in ai else "#0284C7"
        act_text = "БЛОКИРОВАТЬ" if "КРИТИЧНО" in ai else "ИНСПЕКТОР"
        draw_badge(draw, 1050, ry + 5, act_text, act_bg, "#FFFFFF", font_size=10)

    # Правая панель: Спец-консоль управления EDR и Сетевым Экраном
    draw.rounded_rectangle([1230, 220, WIDTH - 30, 1040], radius=12, fill="#0F172A", outline="#00F0FF", width=1)
    draw.text((1250, 240), "ТАКТИЧЕСКИЙ ЦЕНТР УПРАВЛЕНИЯ EDR", font=get_font(16, bold=True), fill="#00F0FF")
    
    # Кнопки боевых режимов
    draw.rounded_rectangle([1250, 280, 1860, 340], radius=8, fill="#1E293B", outline="#10B981", width=2)
    draw.text((1270, 292), "РЕЖИМ: ECO-SENSING (0% CPU)", font=get_font(14, bold=True), fill="#10B981")
    draw.text((1270, 314), "Таймеры заморожены, защита работает на событиях ядра", font=get_font(11), fill="#94A3B8")

    draw.rounded_rectangle([1250, 360, 1860, 420], radius=8, fill="#1E293B", outline="#3B82F6", width=1)
    draw.text((1270, 372), "PORT DEFENDER: АКТИВЕН (RDP 3389, SSH 22)", font=get_font(14, bold=True), fill="#3B82F6")
    draw.text((1270, 394), "0 попыток вторжения за последние 24 часа", font=get_font(11), fill="#94A3B8")

    draw.rounded_rectangle([1250, 440, 1860, 500], radius=8, fill="#1E293B", outline="#F59E0B", width=1)
    draw.text((1270, 452), "БЕЛЫЙ / ЧЕРНЫЙ СПИСКИ FIREWALL", font=get_font(14, bold=True), fill="#F59E0B")
    draw.text((1270, 474), "Whitelist: 42 программы | Blacklist: 3 заблокировано", font=get_font(11), fill="#94A3B8")

    # Консоль событий внизу
    draw.text((1250, 530), "ЖИВОЙ ТЕРМИНАЛ СОБЫТИЙ ЯДРА:", font=get_font(13, bold=True), fill="#E2E8F0")
    draw.rounded_rectangle([1250, 560, 1860, 1010], radius=8, fill="#050811")
    logs = [
        "[11:50:02] [KERNEL] Служба StartupMonitor запущена в Session 0 (SYSTEM)",
        "[11:50:04] [FW] Загружен O(1) FrozenDictionary кэш правил Firewall",
        "[11:50:05] [AUTOGUARD] Эвристики CLI активированы (temp, enc, LOLBAS)",
        "[11:50:12] [SOCKET] Соединение 149.154.167.92:443 (Telegram) -> Whitelisted",
        "[11:50:18] [SWARM] Рой из 5 субагентов приступил к глобальному аудиту",
        "[11:50:24] [WATCHDOG] Сторожевой контур активен, 0 утечек памяти",
        "[11:50:31] [SNAPSHOT] Создан контрольный снимок Baseline v1.9.7"
    ]
    for li, line in enumerate(logs):
        draw.text((1265, 580 + li * 36), line, font=get_font(11), fill="#38BDF8" if "SWARM" in line else "#94A3B8")

    path = os.path.join(OUTPUT_DIR, "variant_1_cyber_command_center.png")
    img.save(path, "PNG")
    print(f"[+] Сгенерирован Вариант 1: {path}")

# ==============================================================================
# 2. SECURITY MATRIX BOARD
# ==============================================================================
def render_variant_2():
    img = Image.new("RGB", (WIDTH, HEIGHT), "#0D1117")
    draw = ImageDraw.Draw(img)
    draw_header(draw, "КОНЦЕПТ 2: SECURITY MATRIX BOARD (MITRE ATT&CK)", "МАТРИЦА РИСКОВ И КАТЕГОРИЙ", "#161B22", "#FFFFFF", "#8B949E")

    # 4 Столбца по категориям риска
    col_w = 445
    columns = [
        ("CRITICAL RISKS (0)", "#EF4444", "Недоверенные скрипты, LOLBAS, Base64"),
        ("SUSPICIOUS / AUDIT (2)", "#F59E0B", "Без подписи, запуск из Temp, отложенные"),
        ("TRUSTED SERVICES (184)", "#3B82F6", "Системные службы Windows и драйверы"),
        ("VERIFIED USER APPS (58)", "#10B981", "Подписанные пользовательские программы")
    ]

    for c_idx, (c_name, c_color, c_desc) in enumerate(columns):
        cx = 30 + c_idx * 470
        draw.rounded_rectangle([cx, 90, cx + col_w, 1040], radius=10, fill="#161B22", outline=c_color, width=2)
        draw.rounded_rectangle([cx, 90, cx + col_w, 150], radius=10, fill="#21262D")
        draw.text((cx + 15, 102), c_name, font=get_font(14, bold=True), fill=c_color)
        draw.text((cx + 15, 126), c_desc, font=get_font(11), fill="#8B949E")

        # Карточки внутри колонки
        if c_idx == 0:
            draw.text((cx + 80, 300), "КРИТИЧЕСКИХ УГРОЗ НЕТ", font=get_font(14, bold=True), fill="#34D399")
            draw.text((cx + 45, 330), "Все активные точки старта проверены", font=get_font(12), fill="#6EE7B7")
        elif c_idx == 1:
            cards = [
                ("powershell.exe (Encoded)", "CLI Threat: -enc", "MITRE T1059.001", "Изолировать"),
                ("TempUpdater.exe", "Path: AppData\\Local\\Temp", "MITRE T1547.001", "Заблокировать")
            ]
            for ki, (k_title, k_sub, k_mitre, k_btn) in enumerate(cards):
                cy = 170 + ki * 140
                draw.rounded_rectangle([cx + 12, cy, cx + col_w - 12, cy + 120], radius=8, fill="#0D1117", outline="#F59E0B", width=1)
                draw.text((cx + 24, cy + 12), k_title, font=get_font(13, bold=True), fill="#FFFFFF")
                draw.text((cx + 24, cy + 36), k_sub, font=get_font(11), fill="#FBBF24")
                draw_badge(draw, cx + 24, cy + 62, k_mitre, "#78350F", "#FDE68A", font_size=10)
                draw_badge(draw, cx + 260, cy + 62, k_btn, "#DC2626", "#FFFFFF", font_size=11)
        elif c_idx == 2:
            services = [
                ("svchost.exe (DcomLaunch)", "NT AUTHORITY\\SYSTEM", "Authenticode: Valid", "5.0 ★"),
                ("spoolsv.exe (Print Spooler)", "Локальная служба", "Authenticode: Valid", "5.0 ★"),
                ("services.exe (SCM Core)", "Управление службами", "Authenticode: Valid", "5.0 ★"),
                ("dwm.exe (Desktop Window)", "Диспетчер окон", "Authenticode: Valid", "5.0 ★"),
                ("SearchIndexer.exe (Index)", "Индексатор Windows", "Authenticode: Valid", "5.0 ★")
            ]
            for si, (s_title, s_sub, s_auth, s_rate) in enumerate(services):
                sy = 170 + si * 105
                draw.rounded_rectangle([cx + 12, sy, cx + col_w - 12, sy + 90], radius=8, fill="#0D1117")
                draw.text((cx + 24, sy + 10), s_title, font=get_font(13, bold=True), fill="#FFFFFF")
                draw.text((cx + 24, sy + 32), s_sub, font=get_font(11), fill="#8B949E")
                draw_badge(draw, cx + 24, sy + 54, s_auth, "#064E3B", "#34D399", font_size=10)
                draw_badge(draw, cx + 320, sy + 54, s_rate, "#1E3A8A", "#93C5FD", font_size=10)
        elif c_idx == 3:
            user_apps = [
                ("Telegram.exe", "Delayed 15s | Порты: 443", "Authenticode: Telegram FZ", "5.0 ★"),
                ("Discord.exe", "Delayed 30s | Порты: 443", "Authenticode: Discord Inc", "4.8 ★"),
                ("Steam.exe", "Delayed 45s | Порты: 27015", "Authenticode: Valve Corp", "4.9 ★"),
                ("Chrome.exe", "Фоновые службы обновлений", "Authenticode: Google LLC", "5.0 ★"),
                ("StartupMonitor.UI.exe", "Основное приложение EDR", "Authenticode: Valid GMS", "5.0 ★")
            ]
            for ui, (u_title, u_sub, u_auth, u_rate) in enumerate(user_apps):
                uy = 170 + ui * 105
                draw.rounded_rectangle([cx + 12, uy, cx + col_w - 12, uy + 90], radius=8, fill="#0D1117")
                draw.text((cx + 24, uy + 10), u_title, font=get_font(13, bold=True), fill="#FFFFFF")
                draw.text((cx + 24, uy + 32), u_sub, font=get_font(11), fill="#8B949E")
                draw_badge(draw, cx + 24, uy + 54, u_auth, "#064E3B", "#34D399", font_size=10)
                draw_badge(draw, cx + 320, uy + 54, u_rate, "#1E3A8A", "#93C5FD", font_size=10)

    path = os.path.join(OUTPUT_DIR, "variant_2_security_matrix.png")
    img.save(path, "PNG")
    print(f"[+] Сгенерирован Вариант 2: {path}")

# ==============================================================================
# 3. FLUENT STUDIO 2.0 (WINDOWS 11/12 CLEAN)
# ==============================================================================
def render_variant_3():
    img = Image.new("RGB", (WIDTH, HEIGHT), "#18181B")
    draw = ImageDraw.Draw(img)
    draw_header(draw, "КОНЦЕПТ 3: FLUENT STUDIO 2.0 (MICA & ACRYLIC)", "ЧИСТЫЙ СТУДИЙНЫЙ СТИЛЬ WINDOWS 11/12", "#27272A", "#FFFFFF", "#A1A1AA")

    # 4 Верхние KPI карточки
    cards = [
        ("ЗДОРОВЬЕ СИСТЕМЫ", "98%", "Отличное состояние (0 угроз)", "#10B981"),
        ("ПРОЦЕССЫ ПОД КОНТРОЛЕМ", "248", "Поядерный баланс активен", "#3B82F6"),
        ("ПРАВИЛА БРАНДМАУЭРА", "142", "Кэш O(1) FrozenDictionary", "#8B5CF6"),
        ("КОНТРОЛЬНЫЕ СНИМКИ", "4 снимка", "Сравнение «Было — Стало»", "#F59E0B")
    ]
    for ci, (c_t, c_v, c_s, c_col) in enumerate(cards):
        cx = 30 + ci * 470
        draw.rounded_rectangle([cx, 90, cx + 445, 195], radius=16, fill="#27272A")
        draw.text((cx + 24, 105), c_t, font=get_font(12, bold=True), fill="#A1A1AA")
        draw.text((cx + 24, 128), c_v, font=get_font(30, bold=True), fill=c_col)
        draw.text((cx + 24, 168), c_s, font=get_font(11), fill="#71717A")

    # Основная область: слева таблица автозапуска, справа панель инспектора
    draw.rounded_rectangle([30, 215, 1240, 1040], radius=16, fill="#27272A")
    draw.text((55, 235), "Объекты автозапуска и запланированные задачи", font=get_font(17, bold=True), fill="#FFFFFF")
    draw.text((55, 260), "Фильтрация: Все (312) | Включенные (280) | Отложенные (12) | Отключенные (20)", font=get_font(12), fill="#A1A1AA")

    rows = [
        ("Telegram Desktop", "HKCU\\Run", "Telegram FZ-LLC", "15 сек", "Безопасен 5.0 ★"),
        ("Discord", "HKCU\\Run", "Discord Inc.", "30 сек", "Безопасен 4.8 ★"),
        ("Steam Client Bootstrapper", "HKCU\\Run", "Valve Corporation", "45 сек", "Безопасен 4.9 ★"),
        ("OneDrive Sync Engine", "HKCU\\Run", "Microsoft Corporation", "Без задержки", "Безопасен 5.0 ★"),
        ("NVIDIA GeForce Experience", "HKLM\\Run", "NVIDIA Corporation", "Без задержки", "Безопасен 5.0 ★"),
        ("Spotify Music Player", "HKCU\\Run", "Spotify AB", "60 сек", "Безопасен 4.7 ★"),
        ("Google Chrome Updater", "Tasks\\GoogleUpdate", "Google LLC", "Фон (15 мин)", "Безопасен 5.0 ★")
    ]
    for ri, (r_app, r_loc, r_pub, r_del, r_ai) in enumerate(rows):
        ry = 300 + ri * 68
        draw.rounded_rectangle([45, ry, 1225, ry + 56], radius=10, fill="#18181B" if ri != 0 else "#3F3F46")
        draw.text((65, ry + 12), r_app, font=get_font(14, bold=True), fill="#FFFFFF")
        draw.text((65, ry + 34), f"{r_loc}  •  {r_pub}", font=get_font(11), fill="#A1A1AA")
        draw_badge(draw, 800, ry + 16, f"⏱ {r_del}", "#1E293B", "#94A3B8", font_size=11)
        draw_badge(draw, 960, ry + 16, r_ai, "#064E3B", "#34D399", font_size=11)

    # Правая панель инспектора
    draw.rounded_rectangle([1270, 215, WIDTH - 30, 1040], radius=16, fill="#27272A")
    draw.text((1295, 235), "ДЕТАЛЬНЫЙ ИНСПЕКТОР ОБЪЕКТА", font=get_font(16, bold=True), fill="#FFFFFF")
    draw.text((1295, 260), "Выбран: Telegram Desktop (v5.6.3)", font=get_font(13), fill="#60A5FA")

    props = [
        ("Исполняемый файл:", "C:\\Users\\Gms\\AppData\\Roaming\\Telegram Desktop\\Telegram.exe"),
        ("Цифровая подпись:", "Подлинная (Telegram FZ-LLC, DigiCert SHA256)"),
        ("Kaspersky OpenTIP:", "Clean (Хэш SHA256 подтвержден в облаке)"),
        ("Отложенный запуск:", "Включен (Запуск через 15 сек после логина)"),
        ("Сетевой статус:", "Белый список Firewall (Все соединения разрешены)"),
        ("Потребление памяти:", "48.2 МБ Working Set | 0.0% CPU в простое"),
        ("Снимок системы:", "Присутствует в снимке Baseline от 2026-10-23")
    ]
    for pi, (p_lbl, p_val) in enumerate(props):
        py = 305 + pi * 70
        draw.text((1295, py), p_lbl, font=get_font(12, bold=True), fill="#A1A1AA")
        draw.text((1295, py + 22), p_val, font=get_font(13), fill="#FFFFFF")
        draw.line([1295, py + 52, 1860, py + 52], fill="#3F3F46", width=1)

    # Кнопки быстрых действий
    draw.rounded_rectangle([1295, 820, 1560, 870], radius=8, fill="#2563EB")
    draw.text((1340, 836), "ЗАПУСТИТЬ СЕЙЧАС", font=get_font(12, bold=True), fill="#FFFFFF")

    draw.rounded_rectangle([1580, 820, 1850, 870], radius=8, fill="#DC2626")
    draw.text((1640, 836), "ОТКЛЮЧИТЬ АВТОСТАРТ", font=get_font(12, bold=True), fill="#FFFFFF")

    path = os.path.join(OUTPUT_DIR, "variant_3_fluent_studio_clean.png")
    img.save(path, "PNG")
    print(f"[+] Сгенерирован Вариант 3: {path}")

# ==============================================================================
# 4. MODULAR TILES GRID (WIDGET BOARD)
# ==============================================================================
def render_variant_4():
    img = Image.new("RGB", (WIDTH, HEIGHT), "#0F172A")
    draw = ImageDraw.Draw(img)
    draw_header(draw, "КОНЦЕПТ 4: MODULAR TILES GRID (СЕТКА ВИДЖЕТОВ)", "КАСТОМИЗИРУЕМЫЙ РАБОЧИЙ СТОЛ", "#1E293B", "#FFFFFF", "#94A3B8")

    # Сетка 3x2 модульных блоков
    tiles = [
        ("ВИДЖЕТ 1: СВОДКА АВТОЗАГРУЗКИ", 30, 90, 620, 460, "#1E293B", "#38BDF8"),
        ("ВИДЖЕТ 2: СЕТЕВОЙ РАДАР (FIREWALL)", 660, 90, 620, 460, "#1E293B", "#10B981"),
        ("ВИДЖЕТ 3: ДИСПЕТЧЕР ПРОЦЕССОВ", 1290, 90, 600, 460, "#1E293B", "#F59E0B"),
        ("ВИДЖЕТ 4: ПОРТ ДЕФЕНДЕР (FAIL2BAN)", 30, 560, 620, 470, "#1E293B", "#EF4444"),
        ("ВИДЖЕТ 5: СНИМКИ «БЫЛО — СТАЛО»", 660, 560, 620, 470, "#1E293B", "#8B5CF6"),
        ("ВИДЖЕТ 6: НАБОР ИНСТРУМЕНТОВ UTILITY", 1290, 560, 600, 470, "#1E293B", "#EC4899")
    ]
    for t_title, tx, ty, tw, th, t_bg, t_acc in tiles:
        draw.rounded_rectangle([tx, ty, tx + tw, ty + th], radius=14, fill=t_bg, outline=t_acc, width=2)
        draw.text((tx + 20, ty + 18), t_title, font=get_font(13, bold=True), fill=t_acc)
        draw.line([tx + 20, ty + 46, tx + tw - 20, ty + 46], fill="#334155", width=1)

    # Заполнение содержимого виджетов
    # Виджет 1: Автозагрузка
    draw.text((50, 160), "Всего объектов: 312", font=get_font(15, bold=True), fill="#FFFFFF")
    draw.text((50, 190), "• В реестре (HKLM/HKCU): 184\n• Запланированные задачи: 96\n• Службы Windows: 32", font=get_font(13), fill="#94A3B8")
    draw_badge(draw, 50, 310, "98.7% с подписью Authenticode", "#064E3B", "#34D399", font_size=12)

    # Виджет 2: Сеть
    draw.text((680, 160), "18 Активных TCP/UDP соединений", font=get_font(15, bold=True), fill="#FFFFFF")
    draw.text((680, 190), "• Telegram (149.154.167.92:443) -> Whitelist\n• Discord (162.159.130.233:443) -> Whitelist\n• Edge (13.107.42.16:443) -> Безопасен", font=get_font(12), fill="#94A3B8")
    draw_badge(draw, 680, 310, "Eco-Sensing: 0% CPU Idle", "#1E3A8A", "#93C5FD", font_size=12)

    # Виджет 3: Процессы
    draw.text((1310, 160), "ТОП-3 Процесса по памяти:", font=get_font(15, bold=True), fill="#FFFFFF")
    draw.text((1310, 190), "1. msedge.exe (840 МБ)\n2. Telegram.exe (124 МБ)\n3. StartupMonitor.Service (64 МБ)", font=get_font(13), fill="#94A3B8")

    # Виджет 4: Port Defender
    draw.text((50, 630), "Защищаемые порты:", font=get_font(15, bold=True), fill="#FFFFFF")
    draw.text((50, 660), "• RDP Порт 3389: Активен (Попыток: 0)\n• SSH Порт 22: Активен (Попыток: 0)\n• SMB Порт 445: Блокировка внешних IP", font=get_font(13), fill="#94A3B8")
    draw_badge(draw, 50, 780, "Auto-Ban: 5 неудачных попыток -> Блок 30 мин", "#7F1D1D", "#FCA5A5", font_size=11)

    # Виджет 5: Снимки
    draw.text((680, 630), "Контрольный эталон системы:", font=get_font(15, bold=True), fill="#FFFFFF")
    draw.text((680, 660), "Снимок: Baseline_2026-10-23.json (312 объектов)\nСтатус: Изменений в системе не зафиксировано\nНовых неизвестных программ: 0", font=get_font(13), fill="#94A3B8")
    draw_badge(draw, 680, 780, "Сравнить с эталоном «Было — Стало»", "#4C1D95", "#C4B5FD", font_size=12)

    # Виджет 6: Инструменты
    draw.text((1310, 630), "Быстрые утилиты:", font=get_font(15, bold=True), fill="#FFFFFF")
    draw.text((1310, 660), "[+] File Locksmith (Разблокировка файлов)\n[+] Keep-Awake (Предотвращение сна)\n[+] Junk Cleaner (Очистка Temp диска)\n[+] LAN IP Scanner (Сканер подсети)", font=get_font(13), fill="#EC4899")

    path = os.path.join(OUTPUT_DIR, "variant_4_modular_tiles_grid.png")
    img.save(path, "PNG")
    print(f"[+] Сгенерирован Вариант 4: {path}")

# ==============================================================================
# 5. SPLIT DUAL-PANE PRO
# ==============================================================================
def render_variant_5():
    img = Image.new("RGB", (WIDTH, HEIGHT), "#111827")
    draw = ImageDraw.Draw(img)
    draw_header(draw, "КОНЦЕПТ 5: SPLIT DUAL-PANE PRO (ДВУХПАНЕЛЬНЫЙ ИНСПЕКТОР)", "ПРОФЕССИОНАЛЬНЫЙ РЕЖИМ СИСАДМИНА", "#1F2937", "#FFFFFF", "#9CA3AF")

    # Левая панель (45% экрана = 830px)
    draw.rounded_rectangle([30, 90, 840, 1040], radius=10, fill="#1F2937", outline="#374151")
    draw.text((50, 110), "ИЕРАРХИЧЕСКОЕ ДЕРЕВО ПРОЦЕССОВ И АВТОСТАРТА", font=get_font(14, bold=True), fill="#60A5FA")

    items = [
        ("▼ System (PID 4)", "Ядро Windows NT", "0.0% CPU"),
        ("   ▼ smss.exe (PID 480)", "Диспетчер сеансов", "0.0% CPU"),
        ("      ▼ csrss.exe (PID 620)", "Клиент/сервер Win32", "0.1% CPU"),
        ("      ▼ wininit.exe (PID 712)", "Инициализация служб", "0.0% CPU"),
        ("         ▼ services.exe (PID 830)", "SCM Менеджер", "0.2% CPU"),
        ("            ► svchost.exe (PID 1120)", "Службы сети (Dcom)", "0.1% CPU"),
        ("            ► StartupMonitor.Service (PID 2140)", "Системная служба EDR", "0.1% CPU"),
        ("   ▼ explorer.exe (PID 4810)", "Оболочка Рабочего стола", "0.4% CPU"),
        ("      ► Telegram.exe (PID 6120)", "Мессенджер (Delayed 15s)", "0.0% CPU"),
        ("      ► Discord.exe (PID 7410)", "Голосовой чат (Delayed 30s)", "0.2% CPU"),
        ("      ► StartupMonitor.UI.exe (PID 8920)", "Графический интерфейс", "0.2% CPU")
    ]
    for ii, (i_tree, i_sub, i_cpu) in enumerate(items):
        iy = 150 + ii * 50
        is_sel = "StartupMonitor.Service" in i_tree
        draw.rounded_rectangle([40, iy, 830, iy + 42], radius=6, fill="#2563EB" if is_sel else ("#111827" if ii % 2 == 0 else "#1A2234"))
        draw.text((55, iy + 10), i_tree, font=get_font(13, bold=True), fill="#FFFFFF")
        draw.text((450, iy + 12), i_sub, font=get_font(11), fill="#93C5FD" if is_sel else "#9CA3AF")
        draw.text((740, iy + 12), i_cpu, font=get_font(12, bold=True), fill="#34D399" if is_sel else "#10B981")

    # Правая панель (55% экрана = 1000px): 4 Таба инспектора
    draw.rounded_rectangle([860, 90, WIDTH - 30, 1040], radius=10, fill="#1F2937", outline="#3B82F6")
    
    # Табы
    tabs = ["1. СЕТЕВЫЕ СОКЕТЫ", "2. МОДУЛИ И DLL", "3. БЕЗОПАСНОСТЬ ИИ", "4. СНИМКИ И ИСТОРИЯ"]
    for ti, t_name in enumerate(tabs):
        tx = 875 + ti * 245
        draw.rounded_rectangle([tx, 105, tx + 235, 145], radius=6, fill="#2563EB" if ti == 0 else "#111827")
        draw.text((tx + 25, 118), t_name, font=get_font(12, bold=True), fill="#FFFFFF" if ti == 0 else "#9CA3AF")

    # Содержимое Таба 1: Сетевые сокеты
    draw.text((885, 175), "Активные сетевые сокеты: StartupMonitor.Service", font=get_font(15, bold=True), fill="#FFFFFF")
    draw.text((885, 205), "Протокол: TCP / UDP  •  Локальный Named Pipe: \\\\.\\pipe\\StartupMonitorPipe", font=get_font(12), fill="#9CA3AF")

    sockets = [
        ("TCPv4", "0.0.0.0:0", "149.154.167.92:443", "ESTABLISHED", "Telegram WAN", "Whitelisted"),
        ("TCPv4", "127.0.0.1:51240", "127.0.0.1:51241", "ESTABLISHED", "IPC Pipe Loop", "Loopback"),
        ("UDPv4", "0.0.0.0:5353", "*:*", "LISTENING", "mDNS Service", "Local Net"),
        ("TCPv4", "0.0.0.0:3389", "0.0.0.0:0", "LISTENING", "Port Defender", "Protected")
    ]
    for si, (proto, loc, rem, st, who, pol) in enumerate(sockets):
        sy = 250 + si * 65
        draw.rounded_rectangle([880, sy, 1870, sy + 52], radius=8, fill="#111827")
        draw_badge(draw, 895, sy + 14, proto, "#1E3A8A", "#93C5FD", font_size=11)
        draw.text((980, sy + 16), f"{loc}  →  {rem}", font=get_font(13, bold=True), fill="#FFFFFF")
        draw_badge(draw, 1420, sy + 14, st, "#064E3B", "#34D399", font_size=11)
        draw.text((1560, sy + 16), who, font=get_font(12), fill="#9CA3AF")
        draw_badge(draw, 1720, sy + 14, pol, "#065F46", "#6EE7B7", font_size=11)

    path = os.path.join(OUTPUT_DIR, "variant_5_split_dual_pane_pro.png")
    img.save(path, "PNG")
    print(f"[+] Сгенерирован Вариант 5: {path}")

# ==============================================================================
# 6. TIMELINE ACTIVITY FLOW
# ==============================================================================
def render_variant_6():
    img = Image.new("RGB", (WIDTH, HEIGHT), "#0A0D14")
    draw = ImageDraw.Draw(img)
    draw_header(draw, "КОНЦЕПТ 6: TIMELINE ACTIVITY FLOW (ХРОНИКА СОБЫТИЙ)", "ВРЕМЕННАЯ ОСЬ ЖИЗНИ СИСТЕМЫ", "#101623", "#FFFFFF", "#7DD3FC")

    # Временная шкала по центру
    draw.rounded_rectangle([30, 90, WIDTH - 30, 160], radius=10, fill="#101623")
    draw.text((50, 105), "ШКАЛА ВРЕМЕНИ СЕАНСА WINDOWS (С МОМЕНТА СТАРТА ПК)", font=get_font(13, bold=True), fill="#00F0FF")
    
    # Линия времени
    draw.line([60, 135, WIDTH - 60, 135], fill="#334155", width=4)
    marks = ["00:00 (Boot)", "00:15 (Delayed 1)", "00:30 (Delayed 2)", "00:45 (Delayed 3)", "01:00 (Idle)", "01:15 (Swarm Start)", "Сейчас"]
    for mi, mark in enumerate(marks):
        mx = 80 + mi * 280
        draw.ellipse([mx - 8, 135 - 8, mx + 8, 135 + 8], fill="#00F0FF")
        draw.text((mx - 30, 145), mark, font=get_font(11), fill="#94A3B8")

    # Лента событий (Cards Feed)
    events = [
        ("11:50:45", "СВЕРНУТО В ТРЕЙ", "Режим глубокого сна 0.0% CPU активирован", "Eco-Sensing", "#10B981"),
        ("11:50:30", "Delayed Startup", "Steam.exe успешно запущен с задержкой 45 сек", "Delayed Engine", "#F59E0B"),
        ("11:50:15", "Delayed Startup", "Discord.exe успешно запущен с задержкой 30 сек", "Delayed Engine", "#F59E0B"),
        ("11:50:05", "Сетевой экран", "Telegram.exe подключился к 149.154.167.92:443 (Разрешено)", "Whitelist", "#3B82F6"),
        ("11:50:01", "Аудит реестра", "Обнаружена новая задача обновления GoogleUpdate (Проверено)", "Safe", "#10B981"),
        ("11:49:50", "Старт Windows", "Main Path Boot Time: 14.2 сек (Система стабильна)", "Boot Tracer", "#8B5CF6")
    ]
    for ei, (e_time, e_title, e_desc, e_cat, e_col) in enumerate(events):
        ey = 190 + ei * 135
        draw.rounded_rectangle([50, ey, WIDTH - 50, ey + 115], radius=12, fill="#101623", outline=e_col, width=1)
        draw.text((80, ey + 20), e_time, font=get_font(16, bold=True), fill=e_col)
        draw.text((200, ey + 20), e_title, font=get_font(16, bold=True), fill="#FFFFFF")
        draw.text((200, ey + 55), e_desc, font=get_font(13), fill="#94A3B8")
        draw_badge(draw, WIDTH - 260, ey + 20, e_cat, "#1E293B", e_col, font_size=12)

    path = os.path.join(OUTPUT_DIR, "variant_6_timeline_activity_flow.png")
    img.save(path, "PNG")
    print(f"[+] Сгенерирован Вариант 6: {path}")

# ==============================================================================
# 7. PROCESS-CENTRIC GRAPH HUB
# ==============================================================================
def render_variant_7():
    img = Image.new("RGB", (WIDTH, HEIGHT), "#05070D")
    draw = ImageDraw.Draw(img)
    draw_header(draw, "КОНЦЕПТ 7: PROCESS-CENTRIC GRAPH HUB (ТОПОЛОГИЯ СВЯЗЕЙ)", "ИНТЕРАКТИВНЫЙ ГРАФ СИСТЕМНЫХ ПРОЦЕССОВ", "#0C101D", "#FFFFFF", "#38BDF8")

    # Центральный графовый холст
    draw.rounded_rectangle([30, 90, 1300, 1040], radius=12, fill="#0C101D", outline="#1E293B")
    
    # Отрисовка корневого узла ядра Windows
    cx, cy = 650, 560
    draw.ellipse([cx - 50, cy - 50, cx + 50, cy + 50], fill="#1E3A8A", outline="#38BDF8", width=3)
    draw.text((cx - 32, cy - 10), "KERNEL\n(PID 4)", font=get_font(12, bold=True), fill="#FFFFFF")

    # Дочерние узлы по кругу
    child_nodes = [
        ("services.exe", 180, "#10B981", "SCM Core"),
        ("explorer.exe", 300, "#3B82F6", "Desktop UI"),
        ("StartupMonitor", 420, "#8B5CF6", "Service EDR"),
        ("Telegram.exe", 540, "#10B981", "Net Active"),
        ("Discord.exe", 660, "#F59E0B", "Delayed"),
        ("svchost.exe", 780, "#10B981", "Net Services"),
        ("spoolsv.exe", 900, "#64748B", "Print Spool"),
        ("Steam.exe", 1020, "#F59E0B", "Delayed")
    ]
    for ni, (n_name, radius, n_color, n_tag) in enumerate(child_nodes):
        angle = ni * (2 * math.pi / len(child_nodes))
        nx = int(cx + 380 * math.cos(angle))
        ny = int(cy + 300 * math.sin(angle))
        
        # Линия связи с ядром
        draw.line([cx, cy, nx, ny], fill="#334155", width=2)
        # Узел
        draw.ellipse([nx - 42, ny - 42, nx + 42, ny + 42], fill="#0F172A", outline=n_color, width=2)
        draw.text((nx - 36, ny - 12), n_name[:10], font=get_font(10, bold=True), fill="#FFFFFF")
        draw.text((nx - 28, ny + 6), n_tag, font=get_font(9), fill=n_color)

    # Правая панель сведений о графе
    draw.rounded_rectangle([1330, 90, WIDTH - 30, 1040], radius=12, fill="#0C101D", outline="#38BDF8")
    draw.text((1360, 120), "СВОЙСТВА ВЫБРАННОГО УЗЛА ГРАФА", font=get_font(16, bold=True), fill="#38BDF8")
    draw.text((1360, 150), "Узел: StartupMonitor.Service (v1.9.7)", font=get_font(13), fill="#FFFFFF")

    graph_stats = [
        ("Связанных процессов:", "8 дочерних воркеров и потоков"),
        ("Сетевых соединений:", "1 Named Pipe сервер, 0 внешних WAN"),
        ("Статус в графе:", "Безусловное доверие (System Core EDR)"),
        ("Изоляция безопасности:", "Session 0 (NT AUTHORITY\\SYSTEM)"),
        ("Нагрузка на ядро:", "0.1% CPU | 0.0% в спящем режиме")
    ]
    for gi, (glbl, gval) in enumerate(graph_stats):
        gy = 200 + gi * 65
        draw.text((1360, gy), glbl, font=get_font(12, bold=True), fill="#94A3B8")
        draw.text((1360, gy + 22), gval, font=get_font(13), fill="#FFFFFF")

    path = os.path.join(OUTPUT_DIR, "variant_7_process_graph_hub.png")
    img.save(path, "PNG")
    print(f"[+] Сгенерирован Вариант 7: {path}")

# ==============================================================================
# 8. ULTRA-COMPACT MINI-DOCK & OVERLAY
# ==============================================================================
def render_variant_8():
    img = Image.new("RGB", (WIDTH, HEIGHT), "#030712")
    draw = ImageDraw.Draw(img)
    draw_header(draw, "КОНЦЕПТ 8: ULTRA-COMPACT MINI-DOCK & OVERLAY", "МИНИМАЛИСТИЧНЫЙ ТРЕЙ-ВИДЖЕТ ДЛЯ ГЕЙМЕРОВ И PRO", "#111827", "#FFFFFF", "#FCD34D")

    # Плавающий док по центру вверху
    dx, dy, dw, dh = 460, 140, 1000, 75
    draw.rounded_rectangle([dx, dy, dx + dw, dy + dh], radius=20, fill="#111827", outline="#F59E0B", width=2)
    
    # Элементы индикации дока
    draw_badge(draw, dx + 25, dy + 22, "ЗАЩИТА: 100% OK", "#064E3B", "#34D399", font_size=13)
    draw_badge(draw, dx + 220, dy + 22, "CPU: 0.2%", "#1E293B", "#38BDF8", font_size=13)
    draw_badge(draw, dx + 380, dy + 22, "RAM: 64 МБ", "#1E293B", "#818CF8", font_size=13)
    draw_badge(draw, dx + 540, dy + 22, "СОКЕТЫ: 18", "#1E293B", "#FBBF24", font_size=13)
    draw_badge(draw, dx + 700, dy + 22, "УГРОЗ: 0", "#1E293B", "#34D399", font_size=13)
    
    # Кнопка разворачивания
    draw.rounded_rectangle([dx + 860, dy + 18, dx + 975, dy + 57], radius=10, fill="#2563EB")
    draw.text((dx + 880, dy + 27), "ОБЗОР ↗", font=get_font(12, bold=True), fill="#FFFFFF")

    # Всплывающее окно оверлея при наведении (демонстрация)
    ox, oy, ow, oh = 460, 250, 1000, 760
    draw.rounded_rectangle([ox, oy, ox + ow, oy + oh], radius=16, fill="#0F172A", outline="#3B82F6", width=2)
    draw.text((ox + 35, oy + 25), "БЫСТРЫЙ ЭКСПРЕСС-ИНСПЕКТОР СИСТЕМЫ", font=get_font(16, bold=True), fill="#FFFFFF")
    draw.text((ox + 35, oy + 55), "Управление активными сетевыми подключениями и всплесками нагрузки", font=get_font(12), fill="#94A3B8")

    overlay_items = [
        ("Telegram Desktop", "149.154.167.92:443", "0.0% CPU", "Белый список"),
        ("Discord App", "162.159.130.233:443", "0.2% CPU", "Белый список"),
        ("Steam Client", "Порты: 27015, 443", "0.0% CPU", "Отложен (45с)"),
        ("Microsoft Edge", "13.107.42.16:443", "1.1% CPU", "Доверенный"),
        ("StartupMonitor.Service", "NamedPipe IPC Active", "0.1% CPU", "SYSTEM Core")
    ]
    for oi, (o_name, o_net, o_cpu, o_st) in enumerate(overlay_items):
        oy_i = oy + 100 + oi * 110
        draw.rounded_rectangle([ox + 25, oy_i, ox + ow - 25, oy_i + 90], radius=10, fill="#1E293B")
        draw.text((ox + 45, oy_i + 16), o_name, font=get_font(15, bold=True), fill="#FFFFFF")
        draw.text((ox + 45, oy_i + 44), f"Сеть: {o_net}  |  Нагрузка: {o_cpu}", font=get_font(12), fill="#94A3B8")
        draw_badge(draw, ox + ow - 220, oy_i + 30, o_st, "#064E3B", "#34D399", font_size=12)

    path = os.path.join(OUTPUT_DIR, "variant_8_compact_mini_dock.png")
    img.save(path, "PNG")
    print(f"[+] Сгенерирован Вариант 8: {path}")

def main():
    print("=" * 70)
    print("ГЕНЕРАЦИЯ 8 ВЫСОКОДЕТАЛИЗИРОВАННЫХ PNG МАКЕТОВ ДИЗАЙНА (1920x1080)")
    print(f"Каталог выгрузки: {OUTPUT_DIR}")
    print("=" * 70)

    render_variant_1()
    render_variant_2()
    render_variant_3()
    render_variant_4()
    render_variant_5()
    render_variant_6()
    render_variant_7()
    render_variant_8()

    print("\n[+] ВСЕ 8 ВАРИАНТОВ ДИЗАЙНА УСПЕШНО СГЕНЕРИРОВАНЫ В ФОРМАТЕ PNG!")

if __name__ == "__main__":
    main()

