# План развёртывания сайта www/ на VPS

- Версия сайта: **2.0.14** (`www/`, статический HTML/CSS/JS без внешних зависимостей), установщик и sidecar SHA-256 находятся в `www/downloads/`.
- Проверка подготовки 2026-11-04: восемь сочетаний RU/EN, Dark/Light и ширины 390/1440 px прошли в Edge. Развёртывание проверяет SHA-256 файлов и атомарно переключает `current`; обнаруженная перед ним версия `/var/www/sm/releases/1.7.2` сохраняется для отката. Активную версию следует проверять по `readlink -f /var/www/sm/current` и содержимому HTTPS, а не по историческим записям документа.
- Сервер: 2.26.55.66 (Ubuntu 26.04 LTS, доступы: `D:\Nas_sync\Coding\VPS\README.md`)
- Домен: sm.monidom.ru (A → 2.26.55.66, проверено), TLS: Let's Encrypt до 2027-01-07, renew по таймеру certbot
- Email для LE: gms@bk.ru

## Почему не стандартный 443

Порт 443 на сервере занят Xray REALITY-inbound (VPN-маскировка, трогать нельзя).
Поэтому Nginx слушает **80** (ACME + редирект) и **8443** (HTTPS сайта).
Вариант «чистый URL без порта» возможен позже через Cloudflare Tunnel (нужен API-токен) —
см. раздел 6. SNI-мультиплексирование на 443 отвергнуто: риск для работающего VPN.

## 0. Вводные и история первичного развёртывания

- [x] Домен sm.monidom.ru
- [x] Доступ root@2.26.55.66 (SSH-пароль в `D:\Nas_sync\Coding\VPS\README.md`, не копируется в репозиторий)
- [x] Email gms@bk.ru
- [x] Ссылки кнопок «Скачать» — привязаны 2026-10-09 (релиз `1.2.2-r2`):
  инсталлятор → `releases/latest/download/StartupMonitorSetup-1.2.2.exe`,
  portable → страница `releases`, плюс ссылка на `.sha256`, «Исходники» → репозиторий.
  ⚠️ На 2026-10-09 все три URL отдают 404 — заработают после `git push` и сборки релиза Actions.
- [x] Инспекция сервера выполнена (см. «Как устроен сервер»)

## Как устроен сервер (факты живой инспекции 2026-10-09)

- Ubuntu 26.04 LTS, UFW active, fail2ban (SSH), IPv6 отключён, DNS 1.1.1.1+1.0.0.1 (`chattr +i`)
- Слушают: 22 (sshd), 443/8445/4443 (xray), 2053/2096 (x-ui, закрыты фаерволом наружу)
- Порт 80 был свободен и закрыт в UFW; Nginx и certbot отсутствовали — установлены: nginx 1.28.3, certbot 4.0.0
- Docker 3x-ui (`network_mode: host`) — не трогали

## 1. Подготовка VPS — выполнено

```bash
apt-get update && apt-get install -y nginx certbot
ufw allow 80/tcp comment 'Web HTTP+ACME'
ufw allow 8443/tcp comment 'Web HTTPS-alt'
```

## 2. Nginx — как есть на сервере

- Релизы: `/var/www/sm/releases/<версия>/` (файлы из `www/` без `serve.ps1`), активный: `/var/www/sm/current` (симлинк). Исторический первый каталог — `1.2.2`; перед публикацией 2.0.14 активен `1.7.2`.
- Конфиг: `/etc/nginx/sites-available/sm` (симлинк в `sites-enabled/`), stage-копии: `/root/sm-deploy/`

```nginx
server {  # 80: ACME + редирект
  listen 80;
  server_name sm.monidom.ru;
  root /var/www/sm/current;
  location /.well-known/acme-challenge/ { try_files $uri =404; }
  location / { return 301 https://$host:8443$request_uri; }
}
server {  # 8443: сам сайт по TLS
  listen 8443 ssl;
  server_name sm.monidom.ru;
  root /var/www/sm/current;
  index index.html;
  ssl_certificate /etc/letsencrypt/live/sm.monidom.ru/fullchain.pem;
  ssl_certificate_key /etc/letsencrypt/live/sm.monidom.ru/privkey.pem;
  gzip on;
  gzip_types text/css application/javascript image/svg+xml;
  location = /index.html { add_header Cache-Control "no-cache"; }
  location / { try_files $uri $uri/ =404; add_header Cache-Control "public, max-age=3600"; }
  location /assets/ { add_header Cache-Control "public, max-age=604800, immutable"; }
  add_header X-Content-Type-Options nosniff;
  add_header X-Frame-Options SAMEORIGIN;
}
```

Сертификат: `certbot certonly --webroot -w /var/www/sm/current -d sm.monidom.ru -m gms@bk.ru --agree-tos --non-interactive`

## 3. Деплой / откат

Деплой новой версии (с Windows-машины, SFTP + SSH):

Для 2.0.14 подготовлен локальный `out/deploy-website-2.0.14.py`: без флагов проверяет артефакты, пароль читает в память из указанного README; `--publish` загружает отдельный каталог, сверяет SHA-256 каждого файла, атомарно переключает симлинк и проверяет опубликованные файлы с откатом при ошибке. SSH использует существующий `known_hosts` и дополнительно проверяет ранее подтверждённый отпечаток узла. Сайт содержит локальные ссылки на `downloads/StartupMonitorSetup-2.0.14.exe` и `.exe.sha256`; файлы копируются только из проверенного релизного пакета. Подпись установщика — самоподписанный сертификат разработчика; публичное доверие сертификату не заявляется.

```bash
# залить www/ в /var/www/sm/releases/<версия>/ (без serve.ps1)
ln -sfn /var/www/sm/releases/<версия> /var/www/sm/current
nginx -t && systemctl reload nginx
```

Откат (одна команда, даунтайма нет):

```bash
ln -sfn /var/www/sm/releases/<предыдущая> /var/www/sm/current && systemctl reload nginx
```

## 4. Автодеплой (фаза 2, не сделано — один из двух)

- **Вариант A — GitHub Actions**: workflow на push в `main` с изменениями в `www/**` — rsync в `/var/www/sm/releases/$VERSION/` + переключение симлинка. Секреты: `SSH_KEY`, `HOST`.
- **Вариант B — без GitHub**: bare-репозиторий на VPS + `post-receive`-хук.
- Чистка: держать последние 3 релиза.

## 5. Проверки первичного деплоя — история 2026-10-09

- [x] `curl https://127.0.0.1:8443/` на сервере → 200, серт CN=sm.monidom.ru
- [x] Снаружи: https://sm.monidom.ru:8443 → 200 (валидный серт), http://sm.monidom.ru → 301 на HTTPS
- [x] `nginx -t` чисто; `ss -tlnp`: 80/8443 слушает nginx, 443/8445/4443 по-прежнему xray (VPN не задет)
- [ ] Открыть RU/EN и тёмную/светлую тему в браузере — вручную
- [ ] `systemctl list-timers | grep certbot` — при следующем SSH-визите

Актуальная read-only проверка 2026-11-04: HTTPS отвечает 200, `nginx -t` успешен, `certbot.timer` активен. Локальная проверка 2.0.14 в настоящем Edge/Playwright прошла в восьми сочетаниях RU/EN, Dark/Light и ширин 390/1440: 94 непустых локализованных элемента, переключение языков и тем, отсутствие ошибок JS/HTTP и горизонтального переполнения. Повторяемая проверка — `python out/verify-website.py`; результаты и screenshots — `out/verification-20261104/website`. Фактическая публикация и её внешняя проверка фиксируются отдельно после выполнения.

## 6. Позже (не входит в текущий деплой)

- Архивная задача привязки кнопок закрыта в локальной 2.0.14: установщик и SHA-256 загружаются с сайта, описание выпуска ведёт на `gms-cod/SM/releases/tag/v2.0.14`.
- Чистый URL без `:8443`: Cloudflare Tunnel (`cloudflared`) — нужен API-токен Cloudflare и перенос NS; порты открывать не придётся, Xray не затрагивается.
- `shortcut: favicon.ico 160 КБ из 204 КБ всего сайта — ужми до ~32px, если трафик пойдёт с мобильных.`
