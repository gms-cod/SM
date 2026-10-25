// StartupMonitor www вЂ” i18n RU/EN + theme dark/light + nav + reveal. Zero-dependency, classic script (СЂР°Р±РѕС‚Р°РµС‚ Рё РєР°Рє file://).
const STRINGS = {
  ru: {
    "meta.title": "Startup Monitor вЂ” РєРѕРЅС‚СЂРѕР»СЊ Р°РІС‚РѕР·Р°РїСѓСЃРєР° Windows",
    "meta.description": "Startup Monitor: РјРѕРЅРёС‚РѕСЂРёРЅРі Р°РІС‚РѕР·Р°РїСѓСЃРєР°, AI-Р°РЅР°Р»РёР· Рё СѓСЃРєРѕСЂРµРЅРёРµ Р·Р°РіСЂСѓР·РєРё Windows.",
    "header.subtitle": "РљРѕРЅС‚СЂРѕР»СЊ Р°РІС‚РѕР·Р°РїСѓСЃРєР° Windows",
    "header.theme": "РўРµРјР°",
    "header.download": "РЎРєР°С‡Р°С‚СЊ",
    "nav.overview": "РћР±Р·РѕСЂ",
    "nav.features": "Р’РѕР·РјРѕР¶РЅРѕСЃС‚Рё",
    "nav.how": "РљР°Рє СЂР°Р±РѕС‚Р°РµС‚",
    "nav.download": "Р—Р°РіСЂСѓР·РєР°",
    "hero.badge.version": "v1.9.6",
    "hero.badge.windows": "Windows 10/11 x64",
    "hero.badge.dotnet": ".NET 8",
    "hero.badge.license": "MIT",
    "hero.title": "РЈР·РЅР°Р№С‚Рµ, С‡С‚Рѕ С‚РѕСЂРјРѕР·РёС‚ Р·Р°РїСѓСЃРє Windows",
    "hero.subtitle": "Startup Monitor РїРѕРєР°Р·С‹РІР°РµС‚ РІСЃРµ С‚РѕС‡РєРё Р°РІС‚РѕР·Р°РїСѓСЃРєР°, РѕС†РµРЅРёРІР°РµС‚ СЂРёСЃРє РєР°Р¶РґРѕР№ Р·Р°РїРёСЃРё СЃ РїРѕРјРѕС‰СЊСЋ AI Рё РїРѕРјРѕРіР°РµС‚ Р±РµР·РѕРїР°СЃРЅРѕ СѓСЃРєРѕСЂРёС‚СЊ Р·Р°РіСЂСѓР·РєСѓ.",
    "hero.cta.download": "РЎРєР°С‡Р°С‚СЊ Р±РµСЃРїР»Р°С‚РЅРѕ",
    "hero.cta.source": "РСЃС…РѕРґРЅРёРєРё",
    "kpi.points.value": "20+",
    "kpi.points.label": "С‚РѕС‡РµРє Р°РІС‚РѕР·Р°РїСѓСЃРєР° РїРѕРґ РєРѕРЅС‚СЂРѕР»РµРј",
    "kpi.cpu.value": "0% CPU",
    "kpi.cpu.label": "РІ С„РѕРЅРµ: Р·Р°РјРµСЂС‹ Р±РµР· РЅР°РіСЂСѓР·РєРё",
    "kpi.ai.value": "AI 1вЂ“10",
    "kpi.ai.label": "СЃРєРѕСЂРёРЅРі СЂРёСЃРєР° Р·Р°РїРёСЃРµР№",
    "kpi.uptime.value": "24/7",
    "kpi.uptime.label": "СЃС‚РѕСЂРѕР¶Р° Рё С‚СЂРµР№СЃРёРЅРі Р·Р°РіСЂСѓР·РєРё",
    "shot.title": "MainWindow: РґРµСЂРµРІРѕ Р°РІС‚РѕР·Р°РїСѓСЃРєР°, 60 FPS",
    "shot.col.name": "РџСЂРѕРіСЂР°РјРјР°",
    "shot.col.status": "РЎС‚Р°С‚СѓСЃ",
    "shot.col.impact": "Р’Р»РёСЏРЅРёРµ",
    "shot.r1.name": "OneDrive",
    "shot.r1.state": "Р’РєР»СЋС‡РµРЅРѕ",
    "shot.r1.impact": "Р’С‹СЃРѕРєРѕРµ",
    "shot.r2.name": "Steam Client",
    "shot.r2.state": "РћС‚Р»РѕР¶РµРЅ В· 30 СЃ",
    "shot.r2.impact": "РЎСЂРµРґРЅРµРµ",
    "shot.r3.name": "Unknown Updater",
    "shot.r3.state": "Р—Р°Р±Р»РѕРєРёСЂРѕРІР°РЅРѕ",
    "shot.r3.impact": "Р РёСЃРє 9/10",
    "shot.note": "РЎРєСЂРёРЅС€РѕС‚С‹ РіР»Р°РІРЅРѕРіРѕ РѕРєРЅР° MainWindow РїРѕСЏРІСЏС‚СЃСЏ Р·РґРµСЃСЊ РїРѕР·Р¶Рµ",
    "features.title": "Р’РѕР·РјРѕР¶РЅРѕСЃС‚Рё",
    "features.subtitle": "Р’СЃС‘ РґР»СЏ Р°РЅР°Р»РёР·Р° Рё С‡РёСЃС‚РєРё Р°РІС‚РѕР·Р°РїСѓСЃРєР° вЂ” РѕС‚ СЂРµРµСЃС‚СЂР° РґРѕ СЃРµС‚Рё, Р±СЂР°РЅРґРјР°СѓСЌСЂР° Рё СѓС‚РёР»РёС‚.",
    "features.monitor.title": "РњРѕРЅРёС‚РѕСЂРёРЅРі Р°РІС‚РѕР·Р°РїСѓСЃРєР°",
    "features.monitor.desc": "20+ С‚РѕС‡РµРє: СЂРµРµСЃС‚СЂ Run, РїР»Р°РЅРёСЂРѕРІС‰РёРє Р·Р°РґР°РЅРёР№, СЃР»СѓР¶Р±С‹, WMI, РїР°РїРєРё Р°РІС‚РѕР·Р°РіСЂСѓР·РєРё.",
    "features.ai.title": "AI-Р°РЅР°Р»РёР· Groq",
    "features.ai.desc": "РЎРєРѕСЂРёРЅРі СЂРёСЃРєР° 1вЂ“10 С‡РµСЂРµР· Groq openai/gpt-oss-120b СЃ РѕР±СЉСЏСЃРЅРµРЅРёРµРј РєР°Р¶РґРѕР№ Р·Р°РїРёСЃРё.",
    "features.opentip.title": "Kaspersky OpenTIP",
    "features.opentip.desc": "РџСЂРѕРІРµСЂРєР° СЂРµРїСѓС‚Р°С†РёРё С„Р°Р№Р»РѕРІ Рё С…РµС€РµР№ РїРѕ РѕР±Р»Р°С‡РЅРѕР№ Р±Р°Р·Рµ Kaspersky OpenTIP.",
    "features.authenticode.title": "РџСЂРѕРІРµСЂРєР° Authenticode",
    "features.authenticode.desc": "Р’Р°Р»РёРґР°С†РёСЏ С†РёС„СЂРѕРІРѕР№ РїРѕРґРїРёСЃРё Рё SHA256-С…РµС€Р° РєР°Р¶РґРѕРіРѕ РёСЃРїРѕР»РЅСЏРµРјРѕРіРѕ С„Р°Р№Р»Р°.",
    "features.guards.title": "Auto-Guards",
    "features.guards.desc": "РЎС‚РѕСЂРѕР¶Р° Р±Р»РѕРєРёСЂСѓСЋС‚ РЅРѕРІС‹Рµ Р·Р°РїРёСЃРё Р°РІС‚РѕР·Р°РїСѓСЃРєР° Рё РїСЂРµРґСѓРїСЂРµР¶РґР°СЋС‚ Рѕ РІС‚РѕСЂР¶РµРЅРёСЏС….",
    "features.firewall.title": "Р‘СЂР°РЅРґРјР°СѓСЌСЂ Windows",
    "features.firewall.desc": "Network Security Hub: СѓРїСЂР°РІР»РµРЅРёРµ РїСЂР°РІРёР»Р°РјРё Inbound/Outbound, РїСЂРѕС„РёР»СЏРјРё Рё РїРѕСЂС‚Р°РјРё.",
    "features.portdefender.title": "Port Defender (Fail2ban)",
    "features.portdefender.desc": "РђРІС‚РѕРјР°С‚РёС‡РµСЃРєР°СЏ Р·Р°С‰РёС‚Р° RDP/SSH/SMB РѕС‚ РїРѕРґР±РѕСЂР° РїР°СЂРѕР»РµР№ Рё РёР·РѕР»СЏС†РёСЏ IP РЅР°СЂСѓС€РёС‚РµР»РµР№.",
    "features.netalerts.title": "РЎРµС‚РµРІС‹Рµ Р°Р»РµСЂС‚С‹ & GeoIP",
    "features.netalerts.desc": "Р’СЃРїР»С‹РІР°СЋС‰РёРµ Р°Р»РµСЂС‚С‹ РїСЂРё РїРѕРґРѕР·СЂРёС‚РµР»СЊРЅС‹С… СЃРѕРµРґРёРЅРµРЅРёСЏС… СЃ С„Р»Р°РіР°РјРё СЃС‚СЂР°РЅ Рё РіРѕСЂРѕРґР°РјРё.",
    "features.locksmith.title": "File Locksmith",
    "features.locksmith.desc": "Р Р°Р·Р±Р»РѕРєРёСЂРѕРІРєР° Р·Р°РЅСЏС‚С‹С… С„Р°Р№Р»РѕРІ С‡РµСЂРµР· Restart Manager Рё РѕСЃРІРѕР±РѕР¶РґРµРЅРёРµ РґРµСЃРєСЂРёРїС‚РѕСЂРѕРІ.",
    "features.awake.title": "Keep-Awake",
    "features.awake.desc": "РџСЂРµРґРѕС‚РІСЂР°С‰РµРЅРёРµ СЃРЅР° Рё РѕС‚РєР»СЋС‡РµРЅРёСЏ СЌРєСЂР°РЅР° РїСЂРё РґР»РёС‚РµР»СЊРЅС‹С… Р·Р°РіСЂСѓР·РєР°С… Рё СЂРµРЅРґРµСЂРёРЅРіРµ.",
    "features.cleaner.title": "Junk Cleaner",
    "features.cleaner.desc": "Р­РєСЃРїСЂРµСЃСЃ-РѕС‡РёСЃС‚РєР° Temp, РґР°РјРїРѕРІ, РєСЌС€Р° РїСЂРѕРІРѕРґРЅРёРєР° Рё РєРѕСЂР·РёРЅС‹ РІ РѕРґРёРЅ РєР»РёРє.",
    "features.lanscan.title": "LAN / Wi-Fi РЎРєР°РЅРµСЂ",
    "features.lanscan.desc": "ARP-РїРѕРёСЃРє РІСЃРµС… СѓСЃС‚СЂРѕР№СЃС‚РІ РїРѕРґСЃРµС‚Рё Р·Р° 1.5 СЃРµРєСѓРЅРґС‹ СЃ РѕРїСЂРµРґРµР»РµРЅРёРµРј РїСЂРѕРёР·РІРѕРґРёС‚РµР»СЏ.",
    "features.hashpass.title": "РҐРµС€Рё Рё Р“РµРЅРµСЂР°С‚РѕСЂ РїР°СЂРѕР»РµР№",
    "features.hashpass.desc": "Р Р°СЃС‡РµС‚ SHA-256/SHA-1/MD5 С„Р°Р№Р»РѕРІ Рё РіРµРЅРµСЂР°С‚РѕСЂ РєСЂРёРїС‚РѕСЃС‚РѕР№РєРёС… РїР°СЂРѕР»РµР№ СЃ СЌРЅС‚СЂРѕРїРёРµР№.",
    "features.tree.title": "Р”РµСЂРµРІРѕ 60 FPS",
    "features.tree.desc": "Р’РёСЂС‚СѓР°Р»РёР·РёСЂРѕРІР°РЅРЅРѕРµ РґРµСЂРµРІРѕ С‚С‹СЃСЏС‡ Р·Р°РїРёСЃРµР№ Р±РµР· Р»Р°РіРѕРІ: РїР»Р°РІРЅС‹Р№ СЃРєСЂРѕР»Р» 60 FPS.",
    "features.sockets.title": "РЎРѕРєРµС‚С‹ TCP/UDP",
    "features.sockets.desc": "Р–РёРІС‹Рµ СЃРµС‚РµРІС‹Рµ СЃРѕРµРґРёРЅРµРЅРёСЏ РїСЂРѕС†РµСЃСЃРѕРІ: РєС‚Рѕ, РєСѓРґР° Рё РїРѕ РєР°РєРѕРјСѓ РїСЂРѕС‚РѕРєРѕР»Сѓ.",
    "features.boot.title": "Boot Tracer",
    "features.boot.desc": "РўСЂРµР№СЃРёРЅРі Р·Р°РіСЂСѓР·РєРё РїРѕ EventID 100вЂ“110: С‚РѕС‡РЅРѕРµ РІСЂРµРјСЏ РєР°Р¶РґРѕРіРѕ СЌС‚Р°РїР° СЃС‚Р°СЂС‚Р°.",
    "features.snapshot.title": "РЎРЅРёРјРєРё В«Р‘С‹Р»Рѕ вЂ” РЎС‚Р°Р»РѕВ»",
    "features.snapshot.desc": "РЎРЅР°РїС€РѕС‚С‹ СЃРѕСЃС‚РѕСЏРЅРёСЏ Рё РЅР°РіР»СЏРґРЅС‹Р№ РґРёС„С„: С‡С‚Рѕ РёР·РјРµРЅРёР»РѕСЃСЊ РїРѕСЃР»Рµ СѓСЃС‚Р°РЅРѕРІРєРё РїСЂРѕРіСЂР°РјРј.",
    "features.delayed.title": "РћС‚Р»РѕР¶РµРЅРЅС‹Р№ СЃС‚Р°СЂС‚",
    "features.delayed.desc": "Delayed Start 15вЂ“120 СЃ: С‚СЏР¶С‘Р»С‹Рµ РїСЂРѕРіСЂР°РјРјС‹ СЃС‚Р°СЂС‚СѓСЋС‚ РїРѕСЃР»Рµ Р·Р°РіСЂСѓР·РєРё СЃРёСЃС‚РµРјС‹.",
    "features.reports.title": "РћС‚С‡С‘С‚С‹ HTML/CSV/JSON",
    "features.reports.desc": "Р­РєСЃРїРѕСЂС‚ СЂРµР·СѓР»СЊС‚Р°С‚РѕРІ Р°РЅР°Р»РёР·Р° РІ HTML, CSV Рё JSON РґР»СЏ Р°СѓРґРёС‚Р° Рё Р°СЂС…РёРІР°.",
    "features.notify.title": "Telegram + Discord",
    "features.notify.desc": "РњРіРЅРѕРІРµРЅРЅС‹Рµ СѓРІРµРґРѕРјР»РµРЅРёСЏ Рѕ РЅРѕРІС‹С… Р·Р°РїРёСЃСЏС… РІ Telegram Рё Discord.",
    "how.title": "РљР°Рє СЂР°Р±РѕС‚Р°РµС‚",
    "how.subtitle": "РўСЂРё Р±РµР·РѕРїР°СЃРЅС‹С… РґРµР№СЃС‚РІРёСЏ РІРјРµСЃС‚Рѕ СЃР»РµРїРѕРіРѕ СѓРґР°Р»РµРЅРёСЏ.",
    "how.step1.title": "1. РћС‚РєР»СЋС‡РёС‚СЊ",
    "how.step1.desc": "Р’СЂРµРјРµРЅРЅРѕ РІС‹РєР»СЋС‡РёС‚Рµ Р·Р°РїРёСЃСЊ Рё РїСЂРѕРІРµСЂСЊС‚Рµ, С‡С‚Рѕ СЃРёСЃС‚РµРјР° СЃС‚Р°Р»Р° Р±С‹СЃС‚СЂРµРµ.",
    "how.step2.title": "2. Р—Р°Р±Р»РѕРєРёСЂРѕРІР°С‚СЊ",
    "how.step2.desc": "РџРѕСЃС‚Р°РІСЊС‚Рµ СЃС‚РѕСЂРѕР¶Р° Auto-Guard, С‡С‚РѕР±С‹ РїСЂРѕРіСЂР°РјРјР° РЅРµ РїСЂРѕРїРёСЃР°Р»Р°СЃСЊ РѕР±СЂР°С‚РЅРѕ.",
    "how.step3.title": "3. РЈРґР°Р»РёС‚СЊ",
    "how.step3.desc": "РЈРґР°Р»РёС‚Рµ Р·Р°РїРёСЃСЊ РЅР°РІСЃРµРіРґР°, РєРѕРіРґР° СѓР±РµРґРёР»РёСЃСЊ, С‡С‚Рѕ РѕРЅР° РЅРµ РЅСѓР¶РЅР°.",
    "dl.title": "Р—Р°РіСЂСѓР·РєР°",
    "dl.subtitle": "Р’С‹Р±РµСЂРёС‚Рµ СѓРґРѕР±РЅС‹Р№ РІР°СЂРёР°РЅС‚ СѓСЃС‚Р°РЅРѕРІРєРё.",
    "dl.installer.title": "РРЅСЃС‚Р°Р»Р»СЏС‚РѕСЂ",
    "dl.installer.desc": "РЈСЃС‚Р°РЅРѕРІС‰РёРє Windows СЃ РїРѕРґРїРёСЃСЊСЋ Authenticode Рё РїСЂРѕРІРµСЂРєРѕР№ SHA256-С…РµС€Р°.",
    "dl.installer.cta": "РЎРєР°С‡Р°С‚СЊ СѓСЃС‚Р°РЅРѕРІС‰РёРє",
    "dl.portable.title": "Portable SMon2",
    "dl.portable.desc": "РџРѕСЂС‚Р°С‚РёРІРЅР°СЏ РІРµСЂСЃРёСЏ Р±РµР· СѓСЃС‚Р°РЅРѕРІРєРё: СЂР°СЃРїР°РєРѕРІР°Р» Рё Р·Р°РїСѓСЃС‚РёР».",
    "dl.portable.cta": "РЎРєР°С‡Р°С‚СЊ portable",
    "dl.silent": "РєР»СЋС‡ С‚РёС…РѕР№ СѓСЃС‚Р°РЅРѕРІРєРё Р±РµР· РґРёР°Р»РѕРіРѕРІ",
    "dl.sha": "SHA-256 РєРѕРЅС‚СЂРѕР»СЊРЅР°СЏ СЃСѓРјРјР°",
    "dl.reqs": "РўСЂРµР±РѕРІР°РЅРёСЏ: Windows 10 1809+ x64, self-contained вЂ” .NET СЃС‚Р°РІРёС‚СЊ РЅРµ РЅСѓР¶РЅРѕ.",
    "footer.license": "Р Р°СЃРїСЂРѕСЃС‚СЂР°РЅСЏРµС‚СЃСЏ РїРѕРґ Р»РёС†РµРЅР·РёРµР№ MIT.",
    "footer.icons": "РРєРѕРЅРєРё вЂ” Fluent icons.",
    "footer.rights": "В© 2026 Startup Monitor."
  },
  en: {
    "meta.title": "Startup Monitor вЂ” Windows startup control",
    "meta.description": "Startup Monitor: startup monitoring, AI analysis and faster Windows boot.",
    "header.subtitle": "Windows startup control",
    "header.theme": "Theme",
    "header.download": "Download",
    "nav.overview": "Overview",
    "nav.features": "Features",
    "nav.how": "How it works",
    "nav.download": "Download",
    "hero.badge.version": "v1.9.6",
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
    "kpi.ai.value": "AI 1вЂ“10",
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
    "shot.r2.state": "Delayed В· 30 s",
    "shot.r2.impact": "Medium",
    "shot.r3.name": "Unknown Updater",
    "shot.r3.state": "Blocked",
    "shot.r3.impact": "Risk 9/10",
    "shot.note": "MainWindow screenshots will appear here later",
    "features.title": "Features",
    "features.subtitle": "Everything to analyze and clean startup вЂ” from registry to network, firewall, and utilities.",
    "features.monitor.title": "Startup monitoring",
    "features.monitor.desc": "20+ locations: Run registry, task scheduler, services, WMI, startup folders.",
    "features.ai.title": "Groq AI analysis",
    "features.ai.desc": "Risk score 1вЂ“10 via Groq openai/gpt-oss-120b with per-entry explanation.",
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
    "features.boot.desc": "Boot tracing via EventID 100вЂ“110: exact timing of every stage.",
    "features.snapshot.title": "Before / After snapshots",
    "features.snapshot.desc": "State snapshots with a clear diff of what changed after installs.",
    "features.delayed.title": "Delayed start",
    "features.delayed.desc": "Delayed Start 15вЂ“120 s: heavy apps launch after the system is up.",
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
    "dl.reqs": "Requires: Windows 10 1809+ x64, self-contained вЂ” no .NET install needed.",
    "footer.license": "Distributed under the MIT license.",
    "footer.icons": "Icons вЂ” Fluent icons.",
    "footer.rights": "В© 2026 Startup Monitor."
  }
};

const LANG_KEY = "sm-lang", THEME_KEY = "sm-theme";
const dict = (l) => STRINGS[l] || STRINGS.en;

// shortcut: theme button shows icon + word; word comes from dict so both langs stay correct
function themeLabel(theme, lang) {
  const word = (STRINGS[lang] && STRINGS[lang]["header.theme"]) || STRINGS.en["header.theme"];
  return (theme === "dark" ? "рџЊ™ " : "вЂ ") + word;
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
  const theme = t === "light" ? "light" : "dark"; // default dark, РєР°Рє ThemeService
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

