# -*- coding: utf-8 -*-
"""
Генератор детального графического макета редизайна страницы Firewall с высокой контрастностью и идеальной читаемостью
"""
from PIL import Image, ImageDraw, ImageFont
import os

width = 1200
height = 800

# Цветовая палитра проекта (высококонтрастная)
BG_DARK = (15, 23, 42)          # #0F172A Глубокий темный фон
CARD_BG = (30, 41, 59)          # #1E293B Фон карточек
CARD_BORDER = (51, 65, 85)      # #334155 Обводка карточек
TEXT_MAIN = (255, 255, 255)     # #FFFFFF Белоснежный основной текст (максимальный контраст)
TEXT_MUTED = (203, 213, 225)    # #CBD5E1 Светло-серый читаемый текст
CYAN = (0, 210, 211)            # #00D2D3 Неоновый циан
GREEN = (52, 211, 153)          # #34D399 Яркий читаемый изумруд
RED = (248, 113, 113)           # #F87171 Яркий читаемый коралл
HEADER_BG = (24, 34, 53)
ROW_HOVER = (39, 52, 75)

img = Image.new("RGB", (width, height), BG_DARK)
draw = ImageDraw.Draw(img)

# Загружаем шрифты
try:
    font_title = ImageFont.truetype("segoeui.ttf", 22)
    font_subtitle = ImageFont.truetype("segoeui.ttf", 13)
    font_bold = ImageFont.truetype("segoeuib.ttf", 14)
    font_regular = ImageFont.truetype("segoeui.ttf", 13)
    font_small = ImageFont.truetype("segoeui.ttf", 11)
    font_small_bold = ImageFont.truetype("segoeuib.ttf", 11)
    font_code = ImageFont.truetype("consola.ttf", 12)
except Exception:
    font_title = font_subtitle = font_bold = font_regular = font_small = font_small_bold = font_code = ImageFont.load_default()

# ── 1. Левая боковая панель навигации ──
nav_width = 220
draw.rectangle([0, 0, nav_width, height], fill=(20, 29, 47), outline=(35, 48, 71), width=1)

# Логотип
draw.ellipse([24, 24, 56, 56], fill=(15, 76, 129), outline=CYAN, width=2)
draw.text((33, 28), "🛡", fill=(255, 255, 255), font=font_title)
draw.text((68, 26), "Startup Monitor", fill=TEXT_MAIN, font=font_bold)
draw.text((68, 44), "Real-time Guard", fill=TEXT_MUTED, font=font_small)

# Пункты навигации
nav_items = [
    ("🏠  Обзор (Overview)", False),
    ("📷  Снимки системы", False, "3"),
    ("⚡  Диспетчер задач", False),
    ("⏱  Время загрузки", False),
    ("🛡  Сетевая безопасность", True),
    ("🚫  Черный список автозапуска", False),
    ("📝  Журнал событий", False),
    ("⚙  Настройки", False)
]

y_nav = 90
for item in nav_items:
    title = item[0]
    active = item[1]
    badge = item[2] if len(item) > 2 else None
    
    if active:
        draw.rectangle([10, y_nav, nav_width - 10, y_nav + 38], fill=(30, 58, 88), outline=CYAN, width=1)
        draw.rectangle([10, y_nav, 14, y_nav + 38], fill=CYAN)
        draw.text((26, y_nav + 9), title, fill=TEXT_MAIN, font=font_bold)
    else:
        draw.text((26, y_nav + 9), title, fill=TEXT_MUTED, font=font_regular)
        if badge:
            bx = nav_width - 38
            draw.rounded_rectangle([bx, y_nav + 8, bx + 22, y_nav + 28], radius=8, fill=(37, 99, 235))
            draw.text((bx + 8, y_nav + 10), badge, fill=(255, 255, 255), font=font_small_bold)
    y_nav += 46

# ── 2. Основная рабочая область ──
main_x = nav_width + 24
main_w = width - main_x - 24

# Заголовок страницы
draw.text((main_x, 24), "Сетевая безопасность и Фаервол", fill=TEXT_MAIN, font=font_title)
draw.text((main_x, 52), "Контроль сетевых подключений, системные правила Windows Firewall и защита от атак", fill=TEXT_MUTED, font=font_subtitle)

# Верхняя статусная плашка
draw.rounded_rectangle([main_x, 78, main_x + main_w, 140], radius=8, fill=CARD_BG, outline=CARD_BORDER, width=1)
draw.text((main_x + 16, 92), "Статус брандмауэра Windows:", fill=TEXT_MUTED, font=font_small)
draw.text((main_x + 16, 110), "● Активен (Все профили защищены)", fill=GREEN, font=font_bold)

draw.text((main_x + 280, 92), "Фильтрация трафика:", fill=TEXT_MUTED, font=font_small)
draw.text((main_x + 280, 110), "Входящий: Блокировать  •  Исходящий: Разрешать", fill=TEXT_MAIN, font=font_regular)

draw.text((main_x + 640, 92), "Режим защиты:", fill=TEXT_MUTED, font=font_small)
# Селектор режима
draw.rounded_rectangle([main_x + 640, 108, main_x + 770, 130], radius=4, fill=(15, 23, 42), outline=CARD_BORDER)
draw.text((main_x + 648, 112), "🟢 Нормальный ▼", fill=TEXT_MAIN, font=font_small_bold)

# Кнопка обновить
draw.rounded_rectangle([main_x + main_w - 140, 96, main_x + main_w - 16, 126], radius=6, fill=(51, 65, 85), outline=(71, 85, 105), width=1)
draw.text((main_x + main_w - 128, 104), "🔄 Обновить статус", fill=TEXT_MAIN, font=font_small_bold)

# ── 3. Вкладки (Tabs) ──
tab_y = 156
tabs = [
    ("🌐 Соединения (24)", False),
    ("🟢 Белый список (14)", True),
    ("🔴 Чёрный список (3)", False),
    ("🛡 Системные правила", False),
    ("⚡ Port Defender", False)
]

cur_x = main_x
for t_title, t_active in tabs:
    t_width = len(t_title) * 9 + 30
    if t_active:
        # Темный фон с четкой изумрудной рамкой
        draw.rounded_rectangle([cur_x, tab_y, cur_x + t_width, tab_y + 36], radius=6, fill=CARD_BG, outline=GREEN, width=2)
        draw.text((cur_x + 14, tab_y + 10), t_title, fill=GREEN, font=font_bold)
    else:
        draw.rounded_rectangle([cur_x, tab_y, cur_x + t_width, tab_y + 36], radius=6, fill=(24, 34, 53), outline=CARD_BORDER, width=1)
        draw.text((cur_x + 14, tab_y + 10), t_title, fill=TEXT_MUTED, font=font_regular)
    cur_x += t_width + 10

# ── 4. Содержимое активной вкладки: «Белый список» ──
content_y = tab_y + 48

# Информационный баннер: ТЕМНЫЙ ФОН + ЧЕТКИЙ ТЕКСТ (без зеленой заливки)
draw.rounded_rectangle([main_x, content_y, main_x + main_w, content_y + 54], radius=8, fill=(18, 38, 32), outline=GREEN, width=1)
draw.text((main_x + 16, content_y + 10), "🟢 Белый список: приложениям разрешена любая сетевая активность без всплывающих окон", fill=TEXT_MAIN, font=font_bold)
draw.text((main_x + 16, content_y + 30), "Для программ из белого списка сетевой экран полностью прозрачен — 0 запросов и 0 задержек.", fill=TEXT_MUTED, font=font_small)

# Панель действий таблицы
panel_y = content_y + 66
draw.rounded_rectangle([main_x, panel_y, main_x + main_w, panel_y + 48], radius=8, fill=CARD_BG, outline=CARD_BORDER, width=1)

# Кнопка Добавить в белый список: Темно-зеленая подложка с ярко-белым четким текстом
draw.rounded_rectangle([main_x + 12, panel_y + 8, main_x + 230, panel_y + 40], radius=6, fill=(6, 95, 70), outline=GREEN, width=1)
draw.text((main_x + 24, panel_y + 15), "➕ Добавить программу...", fill=(255, 255, 255), font=font_bold)

# Кнопка Удалить из списка
draw.rounded_rectangle([main_x + 240, panel_y + 8, main_x + 400, panel_y + 40], radius=6, fill=(51, 65, 85), outline=(71, 85, 105), width=1)
draw.text((main_x + 254, panel_y + 15), "🗑️ Удалить из списка", fill=TEXT_MAIN, font=font_regular)

# Поле поиска
search_x = main_x + main_w - 280
draw.rounded_rectangle([search_x, panel_y + 8, main_x + main_w - 12, panel_y + 40], radius=6, fill=(15, 23, 42), outline=CARD_BORDER, width=1)
draw.text((search_x + 12, panel_y + 15), "🔍 Поиск по белому списку...", fill=TEXT_MUTED, font=font_small)

# ── 5. Таблица программ белого списка ──
grid_y = panel_y + 58
grid_h = 340
draw.rounded_rectangle([main_x, grid_y, main_x + main_w, grid_y + grid_h], radius=8, fill=CARD_BG, outline=CARD_BORDER, width=1)

# Заголовки колонок
draw.rectangle([main_x, grid_y, main_x + main_w, grid_y + 34], fill=HEADER_BG)
draw.line([main_x, grid_y + 34, main_x + main_w, grid_y + 34], fill=CARD_BORDER, width=1)

draw.text((main_x + 16, grid_y + 9), "ПРОГРАММА", fill=TEXT_MUTED, font=font_small_bold)
draw.text((main_x + 200, grid_y + 9), "ПОЛНЫЙ ПУТЬ К ИСПОЛНЯЕМОМУ ФАЙЛУ", fill=TEXT_MUTED, font=font_small_bold)
draw.text((main_x + 620, grid_y + 9), "СТАТУС СЕТИ", fill=TEXT_MUTED, font=font_small_bold)
draw.text((main_x + 750, grid_y + 9), "ДОБАВЛЕНО", fill=TEXT_MUTED, font=font_small_bold)
draw.text((main_x + 850, grid_y + 9), "ДЕЙСТВИЕ", fill=TEXT_MUTED, font=font_small_bold)

# Строки таблицы
rows = [
    ("🌐 opera.exe", r"C:\Users\Gms\AppData\Local\Programs\Opera\opera.exe", "Разрешено всё", "25.10.2026 22:15", True),
    ("💬 telegram.exe", r"C:\Users\Gms\AppData\Roaming\Telegram Desktop\telegram.exe", "Разрешено всё", "24.10.2026 18:30", False),
    ("🎮 steam.exe", r"C:\Program Files (x86)\Steam\steam.exe", "Разрешено всё", "20.10.2026 11:00", False),
    ("🦊 firefox.exe", r"C:\Program Files\Mozilla Firefox\firefox.exe", "Разрешено всё", "18.10.2026 09:12", False),
    ("💻 code.exe", r"C:\Users\Gms\AppData\Local\Programs\Microsoft VS Code\Code.exe", "Разрешено всё", "15.10.2026 14:02", False),
    ("🎵 spotify.exe", r"C:\Users\Gms\AppData\Roaming\Spotify\Spotify.exe", "Разрешено всё", "12.10.2026 19:40", False),
]

row_y = grid_y + 36
for prog, path, status, added, selected in rows:
    if selected:
        draw.rectangle([main_x + 1, row_y, main_x + main_w - 1, row_y + 38], fill=ROW_HOVER)
    
    draw.text((main_x + 16, row_y + 10), prog, fill=TEXT_MAIN, font=font_bold)
    draw.text((main_x + 200, row_y + 11), path[:62], fill=TEXT_MUTED, font=font_code)
    
    # Бейдж «Разрешено всё»: темный фон + яркий зеленый текст
    draw.rounded_rectangle([main_x + 620, row_y + 7, main_x + 725, row_y + 29], radius=4, fill=(6, 78, 59), outline=GREEN, width=1)
    draw.text((main_x + 628, row_y + 9), "● " + status, fill=(255, 255, 255), font=font_small_bold)
    
    draw.text((main_x + 750, row_y + 11), added, fill=TEXT_MUTED, font=font_small)
    
    # Кнопка Убрать
    draw.rounded_rectangle([main_x + 850, row_y + 7, main_x + 920, row_y + 29], radius=4, fill=(51, 65, 85), outline=(71, 85, 105), width=1)
    draw.text((main_x + 862, row_y + 9), "Убрать", fill=TEXT_MAIN, font=font_small)
    
    row_y += 40
    draw.line([main_x, row_y - 2, main_x + main_w, row_y - 2], fill=(35, 48, 71), width=1)

# Нижняя строка статистики
draw.text((main_x + 16, grid_y + grid_h - 26), "Всего программ в белом списке: 14 • Активных сетевых соединений: 8", fill=TEXT_MUTED, font=font_small)

output_path = os.path.join(r"d:\Nas_sync\Coding\StartupMonitor\docs\images", "firewall_redesign_preview.png")
os.makedirs(os.path.dirname(output_path), exist_ok=True)
img.save(output_path, "PNG")
print(f"Обновленное высококонтрастное изображение сохранено: {output_path}")
