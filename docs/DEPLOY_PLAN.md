# План развёртывания сайта www/ на VPS

- Версия сайта: 1.7.4 (`www/`, ~210 КБ статики, zero-dependency, секретов нет)
- Статус: **обновлено 2026-10-16 под релиз v1.7.4** (кнопки привязаны к GitHub Releases v1.7.4)
- Сервер: 2.26.55.66 (Ubuntu 26.04 LTS, доступы: `D:\Nas\Coding\VPS\README.md`)
- Домен: sm.monidom.ru (A → 2.26.55.66, проверено), TLS: Let's Encrypt до 2027-01-07, renew по таймеру certbot
- Email для LE: gms@bk.ru

## Почему не стандартный 443

Порт 443 на сервере занят Xray REALITY-inbound (VPN-маскировка, трогать нельзя).
Поэтому Nginx слушает **80** (ACME + редирект) и **8443** (HTTPS сайта).
Вариант «чистый URL без порта» возможен позже через Cloudflare Tunnel (нужен API-токен) —
см. раздел 6. SNI-мультиплексирование на 443 отвергнуто: риск для работающего VPN.

## 0. Вводные — закрыты

- [x] Домен sm.monidom.ru
- [x] Доступ root@2.26.55.66 (SSH-пароль в `D:\Nas\Coding\VPS\README.md`)
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

- Релизы: `/var/www/sm/releases/1.2.2/` (файлы из `www/` без `serve.ps1`), активный: `/var/www/sm/current` (симлинк)
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

## 5. Проверки после деплоя — пройдены 2026-10-09

- [x] `curl https://127.0.0.1:8443/` на сервере → 200, серт CN=sm.monidom.ru
- [x] Снаружи: https://sm.monidom.ru:8443 → 200 (валидный серт), http://sm.monidom.ru → 301 на HTTPS
- [x] `nginx -t` чисто; `ss -tlnp`: 80/8443 слушает nginx, 443/8445/4443 по-прежнему xray (VPN не задет)
- [ ] Открыть RU/EN и тёмную/светлую тему в браузере — вручную
- [ ] `systemctl list-timers | grep certbot` — при следующем SSH-визите

## 6. Позже (не входит в текущий деплой)

- Кнопки «Скачать»: привязать к GitHub Releases вместо `href="#"`, залить новой версией по разделу 3.
- Чистый URL без `:8443`: Cloudflare Tunnel (`cloudflared`) — нужен API-токен Cloudflare и перенос NS; порты открывать не придётся, Xray не затрагивается.
- `shortcut: favicon.ico 160 КБ из 204 КБ всего сайта — ужми до ~32px, если трафик пойдёт с мобильных.`
