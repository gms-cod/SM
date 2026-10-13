# -*- coding: utf-8 -*-
"""Professional JPG mockup generator for 'Windows Firewall & Port Defender' (StartupMonitor v1.3.0).
Stack: Python + Pillow. Style: Windows 11 Fluent Dark / Mica, Segoe UI.
Outputs 4 variants + 1 showcase collage into docs/images (quality=95).
"""
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

OUT_DIR = r"d:\Nas_sync\Coding\StartupMonitor\docs\images"
W, H = 1600, 1000  # variants
Q = 95

# ---------- palette ----------
BG      = (30, 30, 30)
BG2     = (32, 32, 36)
CARD    = (43, 43, 46)
CARD2   = (50, 50, 55)
STROKE  = (70, 70, 78)
TXT     = (255, 255, 255)
SUB     = (156, 163, 175)
ACCENT  = (96, 165, 250)
GREEN   = (16, 185, 129)
GREENBG = (24, 70, 52)
RED     = (239, 68, 68)
REDBG   = (80, 28, 30)
AMBER   = (245, 158, 11)
AMBERBG = (78, 58, 18)
BLUE    = (59, 130, 246)
BLUEBG  = (28, 48, 92)
TITLEBAR= (24, 24, 27)

F_SEGOE  = r"C:\Windows\Fonts\segoeui.ttf"
F_BOLD   = r"C:\Windows\Fonts\segoeuib.ttf"
F_CONS   = r"C:\Windows\Fonts\consola.ttf"
F_CONSB  = r"C:\Windows\Fonts\consolab.ttf"

def font(sz, bold=False, mono=False):
    try:
        if mono:
            return ImageFont.truetype(F_CONSB if bold else F_CONS, sz)
        return ImageFont.truetype(F_BOLD if bold else F_SEGOE, sz)
    except Exception:
        return ImageFont.load_default()

def new_canvas(w=W, h=H, bg=BG):
    img = Image.new("RGB", (w, h), bg)
    return img, ImageDraw.Draw(img)

def card(d, xy, radius=12, fill=CARD, outline=STROKE, width=1):
    d.rounded_rectangle(xy, radius=radius, fill=fill, outline=outline, width=width)

def text(d, pos, s, sz=14, bold=False, fill=TXT, mono=False, anchor="la"):
    d.text(pos, s, font=font(sz, bold, mono), fill=fill, anchor=anchor)

def badge(d, x, y, label, fg, bg, sz=12, pad_x=10, pad_y=5, dot=None):
    f = font(sz, True)
    bb = d.textbbox((0, 0), label, font=f)
    tw, th = bb[2] - bb[0], bb[3] - bb[1]
    extra = 16 if dot else 0
    bw, bh = tw + pad_x * 2 + extra, th + pad_y * 2
    d.rounded_rectangle([x, y, x + bw, y + bh], radius=bh // 2, fill=bg,
                        outline=tuple(min(255, c + 40) for c in bg), width=1)
    tx = x + pad_x
    if dot:
        d.ellipse([tx, y + bh / 2 - 5, tx + 10, y + bh / 2 + 5], fill=dot)
        tx += 16
    d.text((tx, y + bh / 2), label, font=f, fill=fg, anchor="lm")
    return bw, bh

def toggle(d, x, y, on=True, w=46, h=24):
    bg = ACCENT if on else (100, 100, 110)
    d.rounded_rectangle([x, y, x + w, y + h], radius=h // 2, fill=bg)
    kx = x + w - h // 2 - 2 if on else x + h // 2 + 2
    d.ellipse([kx - 8, y + 4, kx + 8, y + h - 4], fill=(255, 255, 255))

def titlebar(d, w, title, subtitle="StartupMonitor  •  Fluent Dark  •  Mica"):
    d.rectangle([0, 0, w, 52], fill=TITLEBAR)
    for i, c in enumerate([(255, 90, 90), (255, 190, 60), (60, 200, 120)]):
        d.ellipse([16 + i * 22, 18, 30 + i * 22, 32], fill=c)
    text(d, (100, 26), title, sz=16, bold=True, anchor="lm")
    text(d, (w - 150, 26), "—   ▢   ✕", sz=15, fill=SUB, anchor="lm")
    return 52

def shield_icon(d, cx, cy, r, bg, fg, glyph="!"):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=bg)
    d.text((cx, cy), glyph, font=font(int(r * 1.1), True), fill=fg, anchor="mm")

def button(d, x, y, w, h, label, primary=False, sz=13):
    fill = (0, 120, 215) if primary else CARD2
    d.rounded_rectangle([x, y, x + w, y + h], radius=7, fill=fill,
                        outline=ACCENT if primary else STROKE, width=1)
    d.text((x + w / 2, y + h / 2), label, font=font(sz, primary), fill=(255, 255, 255), anchor="mm")

def mica_bg(w=W, h=H):
    """Subtle Mica-like gradient + noise-free vignette."""
    base = Image.new("RGB", (w, h), BG)
    top = Image.new("RGB", (w, h // 2), (38, 38, 44))
    base.paste(top, (0, 0))
    px = base.load()
    return base

# =====================================================================
# VARIANT 1 — ProcessConnectionsWindow + context menu
# =====================================================================
def draw_variant1():
    img = mica_bg()
    d = ImageDraw.Draw(img)
    titlebar(d, W, "Сетевые соединения:  nginx.exe  (PID: 4120)")
    y = 66
    # Shield banner
    card(d, [24, y, W - 24, y + 108], radius=14, fill=(36, 44, 58), outline=(70, 110, 180))
    shield_icon(d, 78, y + 54, 26, BLUEBG, ACCENT, "◈")
    text(d, (116, y + 20), "Port Defender  АКТИВЕН", sz=17, bold=True)
    badge(d, 380, y + 16, "● LIVE", GREEN, GREENBG, sz=11, dot=GREEN)
    badge(d, 480, y + 16, "Забанено: 2 IP", AMBER, AMBERBG, sz=11)
    text(d, (116, y + 48), "Мониторинг портов: 80, 443   •   Банов: 2   •   Белый список: 5",
         sz=13, fill=SUB)
    button(d, W - 420, y + 26, 200, 34, "◈  Настроить Defender", sz=13)
    toggle(d, W - 190, y + 30, True)
    text(d, (W - 130, y + 42), "Defender", sz=13, anchor="lm")
    text(d, (W - 190, y + 66), "Вкл  •  10/мин  •  бан 1 ч", sz=11, fill=SUB)
    y += 122
    # Search row
    card(d, [24, y, W - 24, y + 52], radius=10)
    d.rounded_rectangle([40, y + 11, 420, y + 41], radius=7, fill=(28, 28, 32), outline=STROKE)
    text(d, (56, y + 26), "⌕  Поиск IP / порта…", sz=13, fill=SUB, anchor="lm")
    text(d, (700, y + 26), "☑  Автообновление 2 с", sz=13, fill=SUB, anchor="lm")
    button(d, W - 200, y + 9, 150, 34, "⟳  Обновить")
    y += 64
    # Table header
    cols = [("Протокол", 40), ("Локальный сокет", 180), ("Напр.", 400), ("Удалённый сокет", 500),
            ("Состояние", 760), ("Сегмент", 940), ("Firewall", 1130)]
    card(d, [24, y, W - 24, y + 560], radius=12)
    d.rectangle([24, y, W - 24, y + 44], fill=(36, 36, 42))
    for name, cx in cols:
        text(d, (cx, y + 22), name, sz=12, bold=True, fill=SUB, anchor="lm")
    rows = [
        ("TCP", "0.0.0.0:80", "→", "185.220.101.5:44123", "ESTABLISHED", "Internet", ("Врем. бан 14м", AMBER, AMBERBG)),
        ("TCP", "0.0.0.0:443", "→", "45.142.214.9:59210", "ESTABLISHED", "Internet", ("Заблокирован", RED, REDBG)),
        ("TCP", "0.0.0.0:80", "→", "93.184.216.34:51200", "ESTABLISHED", "Internet", ("Под защитой", ACCENT, BLUEBG)),
        ("TCP", "127.0.0.1:443", "→", "127.0.0.1:61001", "ESTABLISHED", "Loopback", ("Разрешено", GREEN, GREENBG)),
        ("TCP", "0.0.0.0:80", "→", "91.240.118.4:33891", "SYN_RCVD", "Internet", ("Под защитой", ACCENT, BLUEBG)),
        ("UDP", "0.0.0.0:443", "→", "8.8.8.8:53", "—", "Internet", ("Разрешено", GREEN, GREENBG)),
    ]
    ry = y + 52
    for i, (p, ls, dr, rs, st, seg, (fw, fg, fb)) in enumerate(rows):
        if i % 2 == 1:
            d.rectangle([26, ry - 4, W - 26, ry + 40], fill=(47, 47, 52))
        if i == 0:  # selected row highlight
            d.rectangle([26, ry - 4, W - 26, ry + 40], fill=(52, 62, 88),
                        outline=ACCENT, width=1)
        text(d, (40, ry + 18), p, sz=13, mono=True, anchor="lm")
        text(d, (180, ry + 18), ls, sz=13, mono=True, fill=ACCENT, anchor="lm")
        text(d, (400, ry + 18), dr, sz=14, fill=SUB, anchor="lm")
        tcol = RED if i <= 1 else TXT
        text(d, (500, ry + 18), rs, sz=13, mono=True, fill=tcol, anchor="lm")
        text(d, (760, ry + 18), st, sz=12, mono=True, fill=SUB, anchor="lm")
        text(d, (940, ry + 18), seg, sz=12, fill=SUB, anchor="lm")
        badge(d, 1130, ry + 4, ("◉ " if fg == GREEN else "⦸ " if fg == RED else "◈ ") + fw, fg, fb, sz=11)
        ry += 46
    text(d, (40, y + 528), "Всего сокетов: 24   •   Входящих: 18   •   Подозрительных: 2",
         sz=12, fill=SUB)
    # Context menu (cascading)
    mx, my, mw = 560, 300, 520
    d.rounded_rectangle([mx, my, mx + mw, my + 330], radius=12, fill=(44, 44, 50),
                        outline=(90, 90, 100), width=1)
    items = [
        ("⦸  Заблокировать IP  185.220.101.5 …", True),
        ("     ├  На 15 минут", False),
        ("     ├  На 1 час   ✓", False),
        ("     ├  На 24 часа", False),
        ("     └  Навсегда (Permanent)", False),
        ("◉  Заблокировать порт 80 (TCP) — nginx.exe", True),
        ("◈  Настроить Port Defender для порта 80…", True),
        ("—", False),
        ("⧉  Копировать IP   •   Проверить VirusTotal / Whois", False),
        ("✔  Разблокировать IP", False),
    ]
    iy = my + 12
    for label, bold in items:
        if label == "—":
            d.line([mx + 14, iy + 6, mx + mw - 14, iy + 6], fill=STROKE, width=1)
            iy += 14
            continue
        if "На 1 час" in label:
            d.rounded_rectangle([mx + 8, iy - 2, mx + mw - 8, iy + 24], radius=6, fill=(60, 70, 100))
        text(d, (mx + 20, iy + 10), label, sz=13, bold=bold, anchor="lm")
        iy += 28
    # footer note
    text(d, (24, H - 30), "Вариант 1  •  ProcessConnectionsWindow  •  Shield Card + каскадное меню бана  •  v1.3.0",
         sz=12, fill=SUB)
    text(d, (W - 24, H - 30), "1920 → 1600  •  Fluent Dark", sz=12, fill=SUB, anchor="rm")
    img.save(os.path.join(OUT_DIR, "variant_1_context_inspector.jpg"), quality=Q)
    print("saved variant_1")

# =====================================================================
# VARIANT 2 — Network Security Hub
# =====================================================================
def draw_variant2():
    img = mica_bg()
    d = ImageDraw.Draw(img)
    titlebar(d, W, "StartupMonitor  —  Центр сетевой безопасности")
    # Sidebar
    d.rectangle([0, 52, 250, H], fill=(26, 26, 30))
    nav = [("◫  Автозапуск", False), ("⚙  Процессы", False), ("◈  Сетевой экран", True),
           ("⊘  Чёрный список", False), ("⛁  Настройки", False)]
    ny = 80
    text(d, (24, ny), "НАВИГАЦИЯ", sz=11, bold=True, fill=SUB)
    ny += 28
    for label, active in nav:
        if active:
            d.rounded_rectangle([12, ny - 6, 238, ny + 30], radius=8, fill=(48, 58, 84))
            d.rectangle([12, ny - 6, 16, ny + 30], fill=ACCENT)
        text(d, (28, ny + 12), label, sz=14, bold=active, fill=TXT if active else SUB, anchor="lm")
        if active:
            d.ellipse([216, ny + 7, 226, ny + 17], fill=RED)  # attack dot
        ny += 44
    text(d, (24, H - 120), "Firewall: 🟢 Active", sz=12, fill=GREEN)
    text(d, (24, H - 96), "Domain • Private • Public", sz=11, fill=SUB)
    button(d, 24, H - 66, 200, 32, "Сбросить правила", sz=12)
    # Main area
    ax = 274
    text(d, (ax, 92), "Сетевой экран", sz=26, bold=True)
    text(d, (ax, 122), "Брандмауэр Windows + Port Defender  •  живой мониторинг", sz=13, fill=SUB)
    badge(d, ax + 560, 84, "🟢 Активен (Domain, Private, Public)", GREEN, GREENBG, sz=12)
    button(d, W - 220, 84, 190, 34, "Сбросить правила")
    # 4 KPI cards
    kpis = [
        ("◈ Port Defender", "ВКЛ", ACCENT, "Порог: 10/мин • бан 1 ч", True),
        ("Входящих / мин", "184", TXT, "▂▃▅▇▅▃▁  нагрузка 62%", False),
        ("Активные баны", "3", RED, "[ Снять все ]", False),
        ("Управляемых правил", "12", TXT, "MpsSvc: Running", False),
    ]
    kx = ax
    for title, val, col, sub, is_toggle in kpis:
        card(d, [kx, 150, kx + 305, 150 + 128], radius=12)
        text(d, (kx + 16, 172), title, sz=12, fill=SUB)
        text(d, (kx + 16, 214), val, sz=32, bold=True, fill=col)
        if is_toggle:
            toggle(d, kx + 200, 168, True)
            text(d, (kx + 16, 252), sub, sz=11, fill=SUB)
        else:
            text(d, (kx + 16, 252), sub, sz=11, fill=SUB)
        kx += 317
    # Row 2: temp bans + whitelist
    card(d, [ax, 292, ax + 740, 292 + 420], radius=12)
    text(d, (ax + 16, 314), "Временные баны", sz=15, bold=True)
    badge(d, ax + 190, 306, "3", RED, REDBG, sz=12)
    bans = [
        ("194.26.29.112", "RDP :3389 • 18 подкл/мин (порог 5/мин)", "⏱ 42:10 до разбана", "×2 рецидив"),
        ("91.240.118.4", "SSH :22 • 32 подкл/сек (порог 10/сек)", "⏱ 13:57 до разбана", "×3 рецидив"),
        ("185.220.101.5", "HTTP :80 • flood 120/мин", "⏱ 08:21 до разбана", "×1 впервые"),
    ]
    by = 344
    for ip, reason, timer, esc in bans:
        d.rounded_rectangle([ax + 16, by, ax + 724, by + 100], radius=10, fill=CARD2, outline=STROKE)
        shield_icon(d, ax + 52, by + 50, 20, REDBG, RED, "!")
        text(d, (ax + 82, by + 20), ip, sz=15, bold=True, mono=True)
        badge(d, ax + 300, by + 14, esc, AMBER, AMBERBG, sz=10)
        text(d, (ax + 82, by + 46), reason, sz=12, fill=SUB)
        badge(d, ax + 82, by + 66, timer, AMBER, AMBERBG, sz=11)
        button(d, ax + 600, by + 30, 110, 32, "Разбанить", sz=12)
        by += 110
    # Whitelist card
    card(d, [ax + 752, 292, W - 24, 292 + 420], radius=12)
    text(d, (ax + 768, 314), "Белый список", sz=15, bold=True)
    text(d, (ax + 768, 340), "Доверенные IP никогда не банятся", sz=11, fill=SUB)
    d.rounded_rectangle([ax + 768, 362, W - 40, 362 + 36], radius=7, fill=(28, 28, 32), outline=STROKE)
    text(d, (ax + 780, 380), "Добавить IP…   напр. 192.168.1.50", sz=12, fill=SUB, anchor="lm")
    button(d, ax + 768, 406, W - 24 - ax - 768 - 0, 34, "+  Добавить", primary=True)
    wl = ["127.0.0.1  (loopback)", "192.168.0.0/16  (RFC 1918)", "10.8.0.1  (VPN gw)", "93.184.216.34  (офис)"]
    wy = 452
    for entry in wl:
        d.rounded_rectangle([ax + 768, wy, W - 40, wy + 34], radius=7, fill=(34, 50, 40), outline=(40, 90, 60))
        text(d, (ax + 780, wy + 17), "✔  " + entry, sz=12, fill=GREEN, mono=True, anchor="lm")
        wy += 40
    # Row 3 toggles
    card(d, [ax, 724, W - 24, 724 + 190], radius=12)
    text(d, (ax + 16, 744), "Правила защиты  •  глобальные тумблеры", sz=15, bold=True)
    toggles = ["Блокировать сканеры портов", "Бан за N попыток", "Эскалация ×2 при рецидиве", "Уведомления в трей"]
    tx = ax + 16
    for i, t in enumerate(toggles):
        toggle(d, tx, 780, on=(i != 3))
        text(d, (tx, 812), t, sz=11, fill=SUB)
        tx += 260
    button(d, ax + 16, 850, 240, 34, "✦  Мастер настройки…")
    button(d, ax + 266, 850, 240, 34, "Экспорт правил (.json)")
    text(d, (ax, H - 30), "Вариант 2  •  Network Security Hub  •  KPI + баны + whitelist  •  v1.3.1",
         sz=12, fill=SUB)
    img.save(os.path.join(OUT_DIR, "variant_2_security_hub.jpg"), quality=Q)
    print("saved variant_2")

# =====================================================================
# VARIANT 3 — Port Defender Config Wizard
# =====================================================================
def draw_variant3():
    img = mica_bg()
    d = ImageDraw.Draw(img)
    titlebar(d, W, "StartupMonitor  —  мастер настройки защиты")
    # dim background fake window
    card(d, [60, 100, W - 60, 220], radius=12, fill=(36, 36, 40))
    text(d, (100, 150), "Сетевые соединения: nginx.exe — затемнено (модальный визард поверх)", sz=13, fill=SUB)
    # overlay dim
    overlay = Image.new("RGBA", (W, H), (0, 0, 0, 110))
    img_rgba = img.convert("RGBA")
    img_rgba = Image.alpha_composite(img_rgba, overlay)
    img = img_rgba.convert("RGB")
    d = ImageDraw.Draw(img)
    # Modal dialog
    dx, dy, dw, dh = 380, 120, 840, 800
    d.rounded_rectangle([dx, dy, dx + dw, dy + dh], radius=18, fill=(45, 45, 52),
                        outline=(110, 110, 125), width=2)
    # header
    shield_icon(d, dx + 60, dy + 60, 26, BLUEBG, ACCENT, "◈")
    text(d, (dx + 96, dy + 38), "Настройка Port Defender", sz=19, bold=True)
    text(d, (dx + 96, dy + 64), "nginx.exe  •  Порт 80 TCP", sz=13, fill=SUB)
    text(d, (dx + dw - 60, dy + 40), "✕", sz=16, fill=SUB, anchor="mm")
    # steps indicator
    steps = ["1 Порог", "2 Действия", "3 Исключения"]
    sx = dx + 120
    for i, s in enumerate(steps):
        active = (i == 0)
        col = ACCENT if active else (90, 90, 100)
        bg = BLUEBG if active else (60, 60, 68)
        d.ellipse([sx - 16, dy + 104, sx + 16, dy + 136], fill=bg, outline=col, width=2)
        d.text((sx, dy + 120), str(i + 1), font=font(14, True), fill=col, anchor="mm")
        text(d, (sx + 26, dy + 120), s, sz=13, bold=active, fill=TXT if active else SUB, anchor="lm")
        if i < 2:
            d.line([sx + 130, dy + 120, sx + 190, dy + 120], fill=STROKE, width=2)
        sx += 230
    # Step 1 body
    cy = dy + 165
    text(d, (dx + 48, cy), "Шаг 1 — Порог срабатывания", sz=15, bold=True)
    text(d, (dx + 48, cy + 28), "Если подключений с одного IP больше порога — временный бан.", sz=12, fill=SUB)
    # number + unit
    d.rounded_rectangle([dx + 48, cy + 58, dx + 168, cy + 100], radius=8, fill=(28, 28, 33), outline=ACCENT, width=2)
    text(d, (dx + 108, cy + 79), "10", sz=24, bold=True, anchor="mm")
    text(d, (dx + 180, cy + 79), "подключений  за", sz=13, fill=SUB, anchor="lm")
    d.rounded_rectangle([dx + 330, cy + 58, dx + 500, cy + 100], radius=8, fill=CARD2, outline=STROKE)
    text(d, (dx + 415, cy + 79), "1 минуту  ▾", sz=14, bold=True, anchor="mm")
    # slider
    sy = cy + 140
    d.rounded_rectangle([dx + 48, sy - 4, dx + dw - 48, sy + 4], radius=4, fill=(70, 70, 78))
    d.rounded_rectangle([dx + 48, sy - 4, dx + 300, sy + 4], radius=4, fill=ACCENT)
    d.ellipse([dx + 284, sy - 14, dx + 316, sy + 14], fill=(255, 255, 255), outline=ACCENT, width=3)
    text(d, (dx + 48, sy + 24), "2 (мягко)", sz=11, fill=SUB)
    text(d, (dx + dw - 48, sy + 24), "500 (строго)", sz=11, fill=SUB, anchor="rm")
    # preview
    d.rounded_rectangle([dx + 48, sy + 52, dx + dw - 48, sy + 108], radius=10, fill=BLUEBG, outline=(70, 110, 180))
    text(d, (dx + 68, sy + 80), "◈  «10 попыток/мин на порт 80 → временный бан»  •  пресет: Сбалансировано",
         sz=12, fill=ACCENT)
    # presets
    button(d, dx + 48, sy + 120, 220, 34, "Мягко  100/мин")
    button(d, dx + 280, sy + 120, 250, 34, "Сбалансировано  50/мин", primary=True)
    button(d, dx + 542, sy + 120, 200, 34, "Строго  10/мин")
    # Step 2 collapsed preview
    by = sy + 180
    text(d, (dx + 48, by), "Шаг 2 — Длительность бана", sz=15, bold=True)
    durs = [("15 минут", False), ("1 час ✓", True), ("24 часа", False), ("Навсегда", False)]
    bx = dx + 48
    for label, sel in durs:
        d.rounded_rectangle([bx, by + 30, bx + 170, by + 72], radius=9,
                            fill=BLUEBG if sel else CARD2,
                            outline=ACCENT if sel else STROKE, width=2 if sel else 1)
        d.ellipse([bx + 14, by + 44, bx + 28, by + 58], fill=(28, 28, 33), outline=ACCENT if sel else SUB, width=2)
        if sel:
            d.ellipse([bx + 18, by + 48, bx + 24, by + 54], fill=ACCENT)
        text(d, (bx + 36, by + 51), label, sz=12, bold=sel, anchor="lm")
        bx += 182
    text(d, (dx + 48, by + 84), "☑  Удваивать время бана при повторных атаках (эскалация ×2 → permanent)",
         sz=12, fill=GREEN)
    # Step 3 collapsed preview
    wy = by + 120
    text(d, (dx + 48, wy), "Шаг 3 — Исключения (белый список)", sz=15, bold=True)
    text(d, (dx + 48, wy + 26), "☑  Локальные сети 192.168.0.0/16, 10.0.0.0/8   ☑  Этот компьютер 127.0.0.1",
         sz=12, fill=SUB)
    d.rounded_rectangle([dx + 48, wy + 52, dx + dw - 160, wy + 88], radius=8, fill=(28, 28, 33), outline=STROKE)
    text(d, (dx + 62, wy + 70), "192.168.1.50, 10.8.0.1_", sz=13, mono=True, fill=TXT, anchor="lm")
    button(d, dx + dw - 148, wy + 52, 100, 36, "+", primary=True)
    # footer buttons + progress
    button(d, dx + dw - 460, dy + dh - 64, 200, 40, "←  Назад")
    button(d, dx + dw - 248, dy + dh - 64, 200, 40, "Сохранить и включить", primary=True)
    d.rounded_rectangle([dx + 48, dy + dh - 56, dx + 300, dy + dh - 48], radius=4, fill=(70, 70, 78))
    d.rounded_rectangle([dx + 48, dy + dh - 56, dx + 150, dy + dh - 48], radius=4, fill=ACCENT)
    text(d, (dx + 48, dy + dh - 30), "Вариант 3  •  Port Defender Config Wizard  •  шаги 1–3", sz=11, fill=SUB)
    img.save(os.path.join(OUT_DIR, "variant_3_defender_wizard.jpg"), quality=Q)
    print("saved variant_3")

# =====================================================================
# VARIANT 4 — TaskManagerView + flyout
# =====================================================================
def draw_variant4():
    img = mica_bg()
    d = ImageDraw.Draw(img)
    titlebar(d, W, "StartupMonitor  —  Диспетчер задач  •  процессы и сеть")
    # toolbar
    text(d, (24, 92), "Процессы", sz=24, bold=True)
    text(d, (24, 120), "184 процесса  •  колонка ◈ — состояние сетевой защиты (hover → flyout)", sz=12, fill=SUB)
    d.rounded_rectangle([W - 340, 84, W - 24, 120], radius=7, fill=(28, 28, 33), outline=STROKE)
    text(d, (W - 326, 102), "⌕  Поиск процесса…", sz=12, fill=SUB, anchor="lm")
    # table
    ty = 150
    card(d, [24, ty, 1010, ty + 720], radius=12)
    headers = [("◈", 44), ("Процесс", 90), ("PID", 330), ("CPU", 430), ("Память", 530),
               ("Сеть ↓↑", 660), ("Порты", 800), ("Статус", 900)]
    d.rectangle([24, ty, 1010, ty + 44], fill=(36, 36, 42))
    for name, cx in headers:
        text(d, (cx, ty + 22), name, sz=12, bold=True, fill=SUB, anchor="lm")
    procs = [
        ("!", REDBG, RED, "nginx.exe", "4120", "2.1%", "84 МБ", "184/мин", ":80, :443", "АТАКА", RED),
        ("◈", BLUEBG, ACCENT, "mysqld.exe", "2331", "4.8%", "412 МБ", "96/мин", ":3306", "Защита", ACCENT),
        ("◈", BLUEBG, ACCENT, "mstsc.exe (RDP)", "8812", "0.4%", "61 МБ", "12/мин", ":3389", "Защита", ACCENT),
        ("✓", GREENBG, GREEN, "chrome.exe ×14", "5110", "8.2%", "1.2 ГБ", "40/мин", "out", "Норма", GREEN),
        ("○", None, SUB, "svchost.exe", "1044", "0.2%", "22 МБ", "—", "—", "—", SUB),
        ("○", None, SUB, "explorer.exe", "2201", "0.1%", "96 МБ", "—", "—", "—", SUB),
        ("✓", GREENBG, GREEN, "node.exe", "7712", "1.1%", "140 МБ", "6/мин", ":3000", "Норма", GREEN),
        ("◈", BLUEBG, ACCENT, "sshd.exe", "9012", "0.3%", "18 МБ", "22/мин", ":22", "Защита", ACCENT),
    ]
    ry = ty + 52
    for i, (g, bg, fg, name, pid, cpu, mem, net, ports, st, scol) in enumerate(procs):
        if i == 0:
            d.rectangle([26, ry - 4, 1008, ry + 44], fill=(62, 34, 36), outline=RED, width=1)
        elif i % 2 == 1:
            d.rectangle([26, ry - 4, 1008, ry + 44], fill=(47, 47, 52))
        if bg:
            d.ellipse([44, ry + 6, 72, ry + 34], fill=bg)
        d.text((58, ry + 20), g, font=font(13, True), fill=fg, anchor="mm")
        ncol = RED if i == 0 else TXT
        text(d, (90, ry + 20), name, sz=13, bold=(i == 0), fill=ncol, anchor="lm")
        text(d, (330, ry + 20), pid, sz=12, mono=True, fill=SUB, anchor="lm")
        text(d, (430, ry + 20), cpu, sz=12, mono=True, anchor="lm")
        text(d, (530, ry + 20), mem, sz=12, mono=True, anchor="lm")
        text(d, (660, ry + 20), net, sz=12, mono=True, fill=RED if i == 0 else TXT, anchor="lm")
        text(d, (800, ry + 20), ports, sz=12, mono=True, fill=SUB, anchor="lm")
        badge(d, 900, ry + 6, st, scol, (40, 40, 45), sz=10)
        ry += 52
    # legend
    ly = ty + 736
    text(d, (24, ly), "Легенда:", sz=12, bold=True, fill=SUB)
    badge(d, 110, ly - 6, "◈ под защитой", ACCENT, BLUEBG, sz=11)
    badge(d, 290, ly - 6, "✓ норма", GREEN, GREENBG, sz=11)
    badge(d, 420, ly - 6, "! зафиксирована атака", RED, REDBG, sz=11)
    # Flyout for nginx.exe
    fx, fy, fw, fh = 1040, 150, 536, 560
    d.rounded_rectangle([fx, fy, fx + fw, fy + fh], radius=16, fill=(46, 46, 54),
                        outline=RED, width=2)
    shield_icon(d, fx + 56, fy + 56, 24, REDBG, RED, "!")
    text(d, (fx + 90, fy + 36), "nginx.exe  •  PID 4120", sz=17, bold=True)
    badge(d, fx + 90, fy + 62, "⚠ АТАКА: 120 попыток/мин на :80", RED, REDBG, sz=11)
    text(d, (fx + 32, fy + 112), "Статистика сокетов (live, 1 с)", sz=13, bold=True)
    stats = [("ESTABLISHED", "14"), ("SYN_RCVD (подозрит.)", "6"), ("Уникальных IP/мин", "38"),
             ("Забанено для процесса", "2 IP")]
    sy2 = fy + 140
    for k, v in stats:
        text(d, (fx + 32, sy2), k, sz=12, fill=SUB)
        text(d, (fx + fw - 32, sy2), v, sz=13, bold=True, mono=True, anchor="rm")
        sy2 += 28
    # mini bar chart
    text(d, (fx + 32, sy2 + 6), "Входящие/мин (last 60 с)", sz=12, fill=SUB)
    bars = [8, 10, 9, 14, 12, 20, 34, 60, 95, 120, 110, 88]
    bx = fx + 32
    for b in bars:
        h = int(b / 130 * 90)
        col = RED if b > 50 else AMBER if b > 20 else (90, 90, 100)
        d.rectangle([bx, sy2 + 110 - h, bx + 30, sy2 + 110], fill=col)
        bx += 36
    d.line([fx + 32, sy2 + 110 - int(10 / 130 * 90), fx + fw - 32, sy2 + 110 - int(10 / 130 * 90)],
           fill=GREEN, width=2)
    text(d, (fx + fw - 32, sy2 + 42), "порог 10/мин", sz=10, fill=GREEN, anchor="rm")
    # quick toggles
    qy = sy2 + 130
    text(d, (fx + 32, qy), "Быстрые переключатели", sz=13, bold=True)
    toggle(d, fx + fw - 78, qy - 2, True)
    text(d, (fx + 32, qy + 28), "Port Defender для :80, :443", sz=12, fill=SUB)
    text(d, (fx + 32, qy + 52), "Авто-бан топ-нарушителя 185.220.101.5", sz=12, fill=SUB)
    button(d, fx + 32, qy + 82, fw - 64, 38, "⦸  Заблокировать IP сейчас", primary=True)
    button(d, fx + 32, qy + 128, (fw - 76) // 2, 36, "Соединения…")
    button(d, fx + 44 + (fw - 76) // 2, qy + 128, (fw - 76) // 2, 36, "◈  Настроить…")
    # side note
    text(d, (1040, H - 60), "Клик по щиту: None → Wizard (В.3)  •  иначе → инспектор (В.1)", sz=11, fill=SUB)
    text(d, (24, H - 30), "Вариант 4  •  TaskManagerView  •  shield-badges + flyout nginx.exe", sz=12, fill=SUB)
    img.save(os.path.join(OUT_DIR, "variant_4_task_manager_flyout.jpg"), quality=Q)
    print("saved variant_4")

# =====================================================================
# SHOWCASE collage
# =====================================================================
def draw_showcase():
    from PIL import Image as PImage
    paths = ["variant_1_context_inspector.jpg", "variant_2_security_hub.jpg",
             "variant_3_defender_wizard.jpg", "variant_4_task_manager_flyout.jpg"]
    thumbs = []
    for p in paths:
        fp = os.path.join(OUT_DIR, p)
        im = PImage.open(fp)
        thumbs.append(im.copy())
    SW, SH = 1920, 2560
    img, d = new_canvas(SW, SH, (22, 22, 26))
    # header
    d.rectangle([0, 0, SW, 240], fill=(30, 34, 48))
    d.line([0, 240, SW, 240], fill=ACCENT, width=3)
    shield_icon(d, 110, 110, 52, BLUEBG, ACCENT, "◈")
    text(d, (190, 55), "Windows Firewall & Port Defender — визуальные варианты", sz=40, bold=True)
    text(d, (190, 108), "StartupMonitor v1.3.0  •  Windows 11 Fluent Dark (Mica, Wpf.Ui, Segoe UI)  •  4 варианта + план внедрения",
         sz=20, fill=SUB)
    badges = [("V1 Инспектор", ACCENT), ("V2 Security Hub", GREEN), ("V3 Wizard", AMBER), ("V4 Flyout", RED)]
    bx = 190
    for label, col in badges:
        bw, _ = badge(d, bx, 155, "● " + label, col, (40, 40, 48), sz=15)
        bx += bw + 14
    text(d, (SW - 60, 100), "docs/images  •  JPG q95", sz=15, fill=SUB, anchor="rm")
    # grid 2x2
    titles = [
        ("Вариант 1 — Контекстный инспектор", "ProcessConnectionsWindow • Shield Card + каскадное меню бана • релиз v1.3.0 (1→ в работу)"),
        ("Вариант 2 — Центр сетевой безопасности", "Network Security Hub • KPI + временные баны + whitelist • релиз v1.3.1"),
        ("Вариант 3 — Port Defender Config Wizard", "Модальный визард: порог → длительность → исключения • слайдер + пресеты"),
        ("Вариант 4 — Диспетчер задач + Flyout", "Shield-badges в процессах • flyout nginx.exe • 1–2 клика до бана"),
    ]
    tw, th = 880, 550
    gx, gy = 40, 290
    for i, (t, sub) in enumerate(titles):
        cx, cyy = gx + (i % 2) * 920, gy + (i // 2) * 760
        card(d, [cx, cyy, cx + tw + 20, cyy + th + 110], radius=16, fill=(34, 34, 40))
        th_im = thumbs[i].resize((tw, th))
        img.paste(th_im, (cx + 10, cyy + 60))
        num_col = [ACCENT, GREEN, AMBER, RED][i]
        d.ellipse([cx + 18, cyy + 14, cx + 54, cyy + 50], fill=num_col)
        d.text((cx + 36, cyy + 32), str(i + 1), font=font(20, True), fill=(255, 255, 255), anchor="mm")
        text(d, (cx + 64, cyy + 14), t, sz=21, bold=True)
        text(d, (cx + 64, cyy + 40), sub, sz=14, fill=SUB)
    # roadmap footer
    ry = gy + 2 * 760 + 20
    card(d, [40, ry, SW - 40, ry + 430], radius=16, fill=(32, 38, 52), outline=(70, 110, 180))
    text(d, (80, ry + 20), "План внедрения  •  1 → 3 → 4 → 2  (контекст → визард → бейджи → хаб)", sz=22, bold=True)
    steps = [
        ("Этап 1  v1.3.0", "Вариант 1: инспектор + колонка Firewall + меню бана", "2 клика до бана • 1 файл"),
        ("Этап 2  v1.3.0", "Вариант 3: визард порога, длительности, whitelist", "Guided • снижает ошибки"),
        ("Этап 3  v1.3.1", "Вариант 4: shield-badges + flyout в диспетчере", "Ambient • троттлинг опроса"),
        ("Этап 4  v1.3.1", "Вариант 2: Security Hub + KPI + AutoUnban", "Макс. обзор • VM + DTO"),
    ]
    sx = 80
    for title, l1, l2 in steps:
        d.rounded_rectangle([sx, ry + 70, sx + 410, ry + 330], radius=12, fill=(45, 48, 60), outline=STROKE)
        badge(d, sx + 20, ry + 92, title, ACCENT, BLUEBG, sz=13)
        text(d, (sx + 20, ry + 140), l1, sz=14, fill=TXT)
        text(d, (sx + 20, ry + 200), "", sz=1, fill=TXT)
        # wrap manually
        words, lines, cur = l1.split(), [], ""
        text(d, (sx + 20, ry + 140), l1[:34], sz=14)
        text(d, (sx + 20, ry + 166), l1[34:], sz=14)
        text(d, (sx + 20, ry + 210), l2, sz=13, fill=SUB)
        # phase bar
        d.rounded_rectangle([sx + 20, ry + 260, sx + 390, ry + 270], radius=5, fill=(70, 70, 78))
        d.rounded_rectangle([sx + 20, ry + 260, sx + 200 + steps.index((title, l1, l2)) * 60, ry + 270],
                            radius=5, fill=ACCENT)
        sx += 430
    text(d, (80, ry + 350), "Стек: Wpf.Ui 3.x (FluentWindow, Card, SymbolIcon) • INetFwPolicy2 COM • Sliding-Window Rate Limiter • Named Pipe IPC • Zero-Crash",
         sz=14, fill=SUB)
    text(d, (80, ry + 380), "Источники: release_firewall.md  •  docs/SWARM_UI_UX_VARIANTS.md  •  сгенерировано Pillow, Segoe UI, quality=95",
         sz=13, fill=SUB)
    img.save(os.path.join(OUT_DIR, "firewall_visual_variants_showcase.jpg"), quality=Q)
    print("saved showcase")

def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    draw_variant1()
    draw_variant2()
    draw_variant3()
    draw_variant4()
    draw_showcase()
    print("DONE ->", OUT_DIR)

if __name__ == "__main__":
    main()
