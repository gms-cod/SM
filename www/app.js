// StartupMonitor www — i18n RU/EN + theme dark/light + nav + reveal. Zero-dependency, classic script (работает и как file://).
const STRINGS = {
  ru: {
    "meta.title": "Startup Monitor — контроль автозапуска Windows",
    "meta.description": "Startup Monitor: мониторинг автозапуска, AI-анализ и ускорение загрузки Windows.",
    "header.subtitle": "Контроль автозапуска Windows",
    "header.theme": "Тема",
    "header.download": "Скачать",
    "nav.overview": "Обзор",
    "nav.features": "Возможности",
    "nav.how": "Как работает",
    "nav.download": "Загрузка",
    "hero.badge.version": "v1.7.5",
    "hero.badge.windows": "Windows 10/11 x64",
    "hero.badge.dotnet": ".NET 8",
    "hero.badge.license": "MIT",
    "hero.title": "Узнайте, что тормозит запуск Windows",
    "hero.subtitle": "Startup Monitor показывает все точки автозапуска, оценивает риск каждой записи с помощью AI и помогает безопасно ускорить загрузку.",
    "hero.cta.download": "Скачать бесплатно",
    "hero.cta.source": "Исходники",
    "kpi.points.value": "20+",
    "kpi.points.label": "точек автозапуска под контролем",
    "kpi.cpu.value": "0% CPU",
    "kpi.cpu.label": "в фоне: замеры без нагрузки",
    "kpi.ai.value": "AI 1–10",
    "kpi.ai.label": "скоринг риска записей",
    "kpi.uptime.value": "24/7",
    "kpi.uptime.label": "сторожа и трейсинг загрузки",
    "shot.title": "MainWindow: дерево автозапуска, 60 FPS",
    "shot.col.name": "Программа",
    "shot.col.status": "Статус",
    "shot.col.impact": "Влияние",
    "shot.r1.name": "OneDrive",
    "shot.r1.state": "Включено",
    "shot.r1.impact": "Высокое",
    "shot.r2.name": "Steam Client",
    "shot.r2.state": "Отложен · 30 с",
    "shot.r2.impact": "Среднее",
    "shot.r3.name": "Unknown Updater",
    "shot.r3.state": "Заблокировано",
    "shot.r3.impact": "Риск 9/10",
    "shot.note": "Скриншоты главного окна MainWindow появятся здесь позже",
    "features.title": "Возможности",
    "features.subtitle": "Всё для анализа и чистки автозапуска — от реестра до сети, брандмауэра и утилит.",
    "features.monitor.title": "Мониторинг автозапуска",
    "features.monitor.desc": "20+ точек: реестр Run, планировщик заданий, службы, WMI, папки автозагрузки.",
    "features.ai.title": "AI-анализ Groq",
    "features.ai.desc": "Скоринг риска 1–10 через Groq openai/gpt-oss-120b с объяснением каждой записи.",
    "features.opentip.title": "Kaspersky OpenTIP",
    "features.opentip.desc": "Проверка репутации файлов и хешей по облачной базе Kaspersky OpenTIP.",
    "features.authenticode.title": "Проверка Authenticode",
    "features.authenticode.desc": "Валидация цифровой подписи и SHA256-хеша каждого исполняемого файла.",
    "features.guards.title": "Auto-Guards",
    "features.guards.desc": "Сторожа блокируют новые записи автозапуска и предупреждают о вторжениях.",
    "features.firewall.title": "Брандмауэр Windows",
    "features.firewall.desc": "Network Security Hub: управление правилами Inbound/Outbound, профилями и портами.",
    "features.portdefender.title": "Port Defender (Fail2ban)",
    "features.portdefender.desc": "Автоматическая защита RDP/SSH/SMB от подбора паролей и изоляция IP нарушителей.",
    "features.netalerts.title": "Сетевые алерты & GeoIP",
    "features.netalerts.desc": "Всплывающие алерты при подозрительных соединениях с флагами стран и городами.",
    "features.locksmith.title": "File Locksmith",
    "features.locksmith.desc": "Разблокировка занятых файлов через Restart Manager и освобождение дескрипторов.",
    "features.awake.title": "Keep-Awake",
    "features.awake.desc": "Предотвращение сна и отключения экрана при длительных загрузках и рендеринге.",
    "features.cleaner.title": "Junk Cleaner",
    "features.cleaner.desc": "Экспресс-очистка Temp, дампов, кэша проводника и корзины в один клик.",
    "features.lanscan.title": "LAN / Wi-Fi Сканер",
    "features.lanscan.desc": "ARP-поиск всех устройств подсети за 1.5 секунды с определением производителя.",
    "features.hashpass.title": "Хеши и Генератор паролей",
    "features.hashpass.desc": "Расчет SHA-256/SHA-1/MD5 файлов и генератор криптостойких паролей с энтропией.",
    "features.tree.title": "Дерево 60 FPS",
    "features.tree.desc": "Виртуализированное дерево тысяч записей без лагов: плавный скролл 60 FPS.",
    "features.sockets.title": "Сокеты TCP/UDP",
    "features.sockets.desc": "Живые сетевые соединения процессов: кто, куда и по какому протоколу.",
    "features.boot.title": "Boot Tracer",
    "features.boot.desc": "Трейсинг загрузки по EventID 100–110: точное время каждого этапа старта.",
    "features.snapshot.title": "Снимки «Было — Стало»",
    "features.snapshot.desc": "Снапшоты состояния и наглядный дифф: что изменилось после установки программ.",
    "features.delayed.title": "Отложенный старт",
    "features.delayed.desc": "Delayed Start 15–120 с: тяжёлые программы стартуют после загрузки системы.",
    "features.reports.title": "Отчёты HTML/CSV/JSON",
    "features.reports.desc": "Экспорт результатов анализа в HTML, CSV и JSON для аудита и архива.",
    "features.notify.title": "Telegram + Discord",
    "features.notify.desc": "Мгновенные уведомления о новых записях в Telegram и Discord.",
    "how.title": "Как работает",
    "how.subtitle": "Три безопасных действия вместо слепого удаления.",
    "how.step1.title": "1. Отключить",
    "how.step1.desc": "Временно выключите запись и проверьте, что система стала быстрее.",
    "how.step2.title": "2. Заблокировать",
    "how.step2.desc": "Поставьте сторожа Auto-Guard, чтобы программа не прописалась обратно.",
    "how.step3.title": "3. Удалить",
    "how.step3.desc": "Удалите запись навсегда, когда убедились, что она не нужна.",
    "dl.title": "Загрузка",
    "dl.subtitle": "Выберите удобный вариант установки.",
    "dl.installer.title": "Инсталлятор",
    "dl.installer.desc": "Установщик Windows с подписью Authenticode и проверкой SHA256-хеша.",
    "dl.installer.cta": "Скачать установщик",
    "dl.portable.title": "Portable SMon2",
    "dl.portable.desc": "Портативная версия без установки: распаковал и запустил.",
    "dl.portable.cta": "Скачать portable",
    "dl.silent": "ключ тихой установки без диалогов",
    "dl.sha": "SHA-256 контрольная сумма",
    "dl.reqs": "Требования: Windows 10 1809+ x64, self-contained — .NET ставить не нужно.",
    "footer.license": "Распространяется под лицензией MIT.",
    "footer.icons": "Иконки — Fluent icons.",
    "footer.rights": "© 2026 Startup Monitor."
  },
  en: {
    "meta.title": "Startup Monitor — Windows startup control",
    "meta.description": "Startup Monitor: startup monitoring, AI analysis and faster Windows boot.",
    "header.subtitle": "Windows startup control",
    "header.theme": "Theme",
    "header.download": "Download",
    "nav.overview": "Overview",
    "nav.features": "Features",
    "nav.how": "How it works",
    "nav.download": "Download",
    "hero.badge.version": "v1.7.5",
    "hero.badge.windows": "Windows 10/11 x64",
    "hero.badge.dotnet": ".NET 8",
    "hero.badge.license": "MIT",
    "hero.title": "Find out what slows down Windows boot",
    "hero.subtitle": "Startup Monitor shows every startup location, scores each entry with AI and helps you safely speed up boot.",
    "hero.cta.download": "Download free",
    "hero.cta.source": "Sources",
    "kpi.points.value": "20+",
    "kpi.points.label": "startup locations covered",
    "kpi.cpu.value": "0% CPU",
    "kpi.cpu.label": "idle background watchers",
    "kpi.ai.value": "AI 1–10",
    "kpi.ai.label": "entry risk scoring",
    "kpi.uptime.value": "24/7",
    "kpi.uptime.label": "guards and boot tracing",
    "shot.title": "MainWindow: startup tree, 60 FPS",
    "shot.col.name": "Program",
    "shot.col.status": "Status",
    "shot.col.impact": "Impact",
    "shot.r1.name": "OneDrive",
    "shot.r1.state": "Enabled",
    "shot.r1.impact": "High",
    "shot.r2.name": "Steam Client",
    "shot.r2.state": "Delayed · 30 s",
    "shot.r2.impact": "Medium",
    "shot.r3.name": "Unknown Updater",
    "shot.r3.state": "Blocked",
    "shot.r3.impact": "Risk 9/10",
    "shot.note": "MainWindow screenshots will appear here later",
    "features.title": "Features",
    "features.subtitle": "Everything to analyze and clean startup — from registry to network, firewall, and utilities.",
    "features.monitor.title": "Startup monitoring",
    "features.monitor.desc": "20+ locations: Run registry, task scheduler, services, WMI, startup folders.",
    "features.ai.title": "Groq AI analysis",
    "features.ai.desc": "Risk score 1–10 via Groq openai/gpt-oss-120b with per-entry explanation.",
    "features.opentip.title": "Kaspersky OpenTIP",
    "features.opentip.desc": "File and hash reputation check via Kaspersky OpenTIP cloud.",
    "features.authenticode.title": "Authenticode check",
    "features.authenticode.desc": "Digital signature and SHA256 validation for every executable.",
    "features.guards.title": "Auto-Guards",
    "features.guards.desc": "Guards block new startup entries and warn about intrusions.",
    "features.firewall.title": "Windows Firewall Hub",
    "features.firewall.desc": "Network Security Hub: manage Inbound/Outbound rules, profiles, and ports.",
    "features.portdefender.title": "Port Defender (Fail2ban)",
    "features.portdefender.desc": "Automated RDP/SSH/SMB brute-force protection and suspicious IP isolation.",
    "features.netalerts.title": "Network Alerts & GeoIP",
    "features.netalerts.desc": "Real-time toast alerts for suspicious connections with GeoIP flags and cities.",
    "features.locksmith.title": "File Locksmith",
    "features.locksmith.desc": "Unlock locked files via Windows Restart Manager API and free handles.",
    "features.awake.title": "Keep-Awake",
    "features.awake.desc": "Prevent system sleep and screen turn-off during long renders and downloads.",
    "features.cleaner.title": "Junk Cleaner",
    "features.cleaner.desc": "Express cleaning of Temp, crash dumps, thumbnail caches, and recycle bin.",
    "features.lanscan.title": "LAN / Wi-Fi Scanner",
    "features.lanscan.desc": "Lightning-fast ARP subnet scanning in 1.5s with MAC and OUI vendor lookup.",
    "features.hashpass.title": "Hashes & Passwords",
    "features.hashpass.desc": "Instant SHA-256/SHA-1/MD5 checksums and secure password generator with entropy.",
    "features.tree.title": "60 FPS tree",
    "features.tree.desc": "Virtualized tree of thousands of entries, smooth 60 FPS scroll.",
    "features.sockets.title": "TCP/UDP sockets",
    "features.sockets.desc": "Live process connections: who talks where and over what protocol.",
    "features.boot.title": "Boot Tracer",
    "features.boot.desc": "Boot tracing via EventID 100–110: exact timing of every stage.",
    "features.snapshot.title": "Before / After snapshots",
    "features.snapshot.desc": "State snapshots with a clear diff of what changed after installs.",
    "features.delayed.title": "Delayed start",
    "features.delayed.desc": "Delayed Start 15–120 s: heavy apps launch after the system is up.",
    "features.reports.title": "HTML/CSV/JSON reports",
    "features.reports.desc": "Export analysis to HTML, CSV and JSON for audit and archive.",
    "features.notify.title": "Telegram + Discord",
    "features.notify.desc": "Instant alerts about new entries in Telegram and Discord.",
    "how.title": "How it works",
    "how.subtitle": "Three safe actions instead of blind deletion.",
    "how.step1.title": "1. Disable",
    "how.step1.desc": "Temporarily turn an entry off and check the system got faster.",
    "how.step2.title": "2. Block",
    "how.step2.desc": "Set an Auto-Guard so the program cannot register itself back.",
    "how.step3.title": "3. Remove",
    "how.step3.desc": "Delete the entry for good once you are sure it is not needed.",
    "dl.title": "Download",
    "dl.subtitle": "Pick the install option that fits you.",
    "dl.installer.title": "Installer",
    "dl.installer.desc": "Windows installer signed with Authenticode and SHA256 verified.",
    "dl.installer.cta": "Download installer",
    "dl.portable.title": "Portable SMon2",
    "dl.portable.desc": "No-install portable build: unpack and run.",
    "dl.portable.cta": "Download portable",
    "dl.silent": "silent install key, no dialogs",
    "dl.sha": "SHA-256 checksum",
    "dl.reqs": "Requires: Windows 10 1809+ x64, self-contained — no .NET install needed.",
    "footer.license": "Distributed under the MIT license.",
    "footer.icons": "Icons — Fluent icons.",
    "footer.rights": "© 2026 Startup Monitor."
  }
};

const LANG_KEY = "sm-lang", THEME_KEY = "sm-theme";
const dict = (l) => STRINGS[l] || STRINGS.en;

// shortcut: theme button shows icon + word; word comes from dict so both langs stay correct
function themeLabel(theme, lang) {
  const word = (STRINGS[lang] && STRINGS[lang]["header.theme"]) || STRINGS.en["header.theme"];
  return (theme === "dark" ? "🌙 " : "☀ ") + word;
}

function setLang(l) {
  const lang = STRINGS[l] ? l : "en";
  localStorage.setItem(LANG_KEY, lang);
  document.documentElement.lang = lang;
  const d = dict(lang);
  document.querySelectorAll("[data-i18n]").forEach((el) => {
    const k = el.dataset.i18n;
    const v = d[k] ?? STRINGS.en[k];
    if (v == null) return;
    if (el.tagName === "META") el.setAttribute("content", v);
    else if (el.tagName === "TITLE") el.textContent = v;
    else el.textContent = v;
  });
  document.title = d["meta.title"] ?? document.title;
  document.querySelectorAll("[data-lang]").forEach((b) => {
    b.classList.toggle("is-active", b.dataset.lang === lang);
  });
  // re-apply theme icon in the new language
  setTheme(localStorage.getItem(THEME_KEY) || "dark", true);
}

function setTheme(t, keepLang) {
  const theme = t === "light" ? "light" : "dark"; // default dark, как ThemeService
  document.documentElement.dataset.theme = theme;
  localStorage.setItem(THEME_KEY, theme);
  const b = document.getElementById("themeToggle");
  if (b) {
    const lang = document.documentElement.lang || "ru";
    b.textContent = themeLabel(theme, lang);
    b.setAttribute("aria-label", theme === "dark" ? "Light theme" : "Dark theme");
  }
  if (!keepLang) { /* theme switch only */ }
}

function init() {
  setLang(localStorage.getItem(LANG_KEY) || ((navigator.language || "en").toLowerCase().startsWith("ru") ? "ru" : "en"));
  setTheme(localStorage.getItem(THEME_KEY) || (matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark"), true);
  document.querySelectorAll("[data-lang]").forEach((b) => b.addEventListener("click", () => setLang(b.dataset.lang)));
  document.getElementById("langToggle")?.addEventListener("click", () => setLang(document.documentElement.lang === "ru" ? "en" : "ru"));
  document.getElementById("themeToggle")?.addEventListener("click", () => setTheme(document.documentElement.dataset.theme === "dark" ? "light" : "dark"));
  document.getElementById("navToggle")?.addEventListener("click", (e) => {
    const nav = document.getElementById("nav") || document.querySelector("nav");
    nav?.classList.toggle("open");
    e.currentTarget.setAttribute("aria-expanded", nav?.classList.contains("open") ? "true" : "false");
  });
  document.querySelectorAll("#nav a").forEach((a) => a.addEventListener("click", () => {
    document.getElementById("nav")?.classList.remove("open");
  }));
  // active nav marker
  const links = [...document.querySelectorAll("#nav a")];
  const mark = () => {
    const h = location.hash || "#overview";
    links.forEach((a) => a.classList.toggle("active", a.getAttribute("href") === h));
  };
  addEventListener("hashchange", mark); mark();
  const els = document.querySelectorAll(".reveal");
  if ("IntersectionObserver" in window && els.length) {
    const io = new IntersectionObserver((es) => es.forEach((e) => e.isIntersecting && (e.target.classList.add("visible"), io.unobserve(e.target))), { threshold: 0.12 });
    els.forEach((el) => io.observe(el));
  }
}

document.addEventListener("DOMContentLoaded", init);
