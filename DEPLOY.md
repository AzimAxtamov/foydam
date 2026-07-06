# Деплой SellerTools на VPS

Пошагово от чистого Ubuntu до работающего бота. Выполняй по одному блоку,
проверяя результат. Всё запускается в Docker, на хосте нужен только Docker.

Предполагается Ubuntu 22.04/24.04 и пользователь с `sudo`.

---

## 1. Обновить систему

```bash
sudo apt update && sudo apt upgrade -y
```

## 2. Установить Docker + Compose

```bash
# Официальный скрипт установки Docker.
curl -fsSL https://get.docker.com | sudo sh

# Запускать docker без sudo (перелогиниться после этой команды).
sudo usermod -aG docker $USER
newgrp docker

# Проверка:
docker --version
docker compose version
```

Ожидаемо: обе команды печатают версии без ошибок.

## 3. Клонировать репозиторий

```bash
# git обычно уже есть; если нет: sudo apt install -y git
git clone https://github.com/AzimAxtamov/foydam.git
cd foydam
```

## 4. Заполнить секреты (.env)

```bash
cp .env.example .env
nano .env
```

Заполни:

| Переменная | Что это |
|------------|---------|
| `POSTGRES_PASSWORD` | придумай сильный пароль БД |
| `BOT_TOKEN` | токен бота от [@BotFather](https://t.me/BotFather) |
| `CRYPTO_KEY_BASE64` | ключ шифрования: `openssl rand -base64 32` |
| `ADMIN_CHAT_ID` | твой chat_id (узнать: [@userinfobot](https://t.me/userinfobot)) |
| `UZUM_API_MODE` | `Real` для боевого API, `Mock` для проверки на демо-данных |

`POSTGRES_USER` и `POSTGRES_DB` можно оставить как есть.

> Сгенерировать AES-ключ прямо здесь:
> ```bash
> openssl rand -base64 32
> ```
> Вставь результат в `CRYPTO_KEY_BASE64`. **Не теряй этот ключ** — им зашифрованы
> API-ключи селлеров; при потере их придётся вводить заново.

Проверь, что `.env` не пуст и без плейсхолдеров `CHANGE_ME`:

```bash
grep CHANGE_ME .env && echo "ЕСТЬ незаполненные!" || echo "OK, всё заполнено"
```

## 5. Собрать и запустить

```bash
docker compose -f docker-compose.prod.yml up -d --build
```

Первая сборка идёт несколько минут (restore + publish). Дальше — быстро.

## 6. Проверить, что всё поднялось

```bash
# Статусы: app/postgres/redis/backup должны быть Up (healthy).
docker compose -f docker-compose.prod.yml ps
```

Ожидаемо: `sellertools-app` и `sellertools-postgres` — `Up (healthy)`,
`redis` — `Up (healthy)`, `backup` — `Up`.

```bash
# Логи приложения — ищем "Бот запущен".
docker compose -f docker-compose.prod.yml logs app | grep -i "Бот запущен"
```

Ожидаемо: строка вида `Бот запущен: @<имя_бота> (id ...)`.

```bash
# Health-эндпоинт (изнутри контейнера, порт наружу не публикуется).
docker compose -f docker-compose.prod.yml exec app curl -fsS http://localhost:8080/health
```

Ожидаемо: `Healthy`.

## 7. Проверить бота в Telegram

Открой бота, отправь `/start`, выбери язык, пришли API-ключ Uzum.
Бот определит магазин, привяжет и покажет меню. Кнопка «📊 Отчёт» строит отчёт.

## 8. Проверить бэкапы

```bash
# Первый дамп создаётся сразу при старте контейнера backup.
ls -lh backups/
```

Ожидаемо: файл вида `sellertools-YYYY-MM-DD_HHMMSS.sql.gz`. Дальше — раз в сутки,
хранятся 14 дней (старые удаляются автоматически).

Ручной бэкап при необходимости:

```bash
docker compose -f docker-compose.prod.yml exec -T postgres \
  pg_dump -U sellertools sellertools | gzip > backups/manual-$(date +%F).sql.gz
```

## 9. Логи приложения

Structured-логи пишутся и в файл (том `./logs`), с ротацией по дням, хранение 14 файлов:

```bash
ls -lh logs/
tail -f logs/sellertools-*.log
```

---

## Обновление до новой версии

```bash
cd foydam
git pull
docker compose -f docker-compose.prod.yml up -d --build
```

Данные (`pgdata`, `backups`, `logs`) переживают пересборку.

## Полезные команды

```bash
# Перезапустить только приложение
docker compose -f docker-compose.prod.yml restart app

# Остановить всё (данные сохраняются)
docker compose -f docker-compose.prod.yml down

# Остановить и УДАЛИТЬ данные БД (осторожно!)
docker compose -f docker-compose.prod.yml down -v

# Хвост логов конкретного сервиса
docker compose -f docker-compose.prod.yml logs -f app
```

## Частая проблема: `password authentication failed`

Если после смены `POSTGRES_PASSWORD` в `.env` приложение падает с
`password authentication failed for user "sellertools"` — Postgres применяет
пароль **только при первой инициализации** пустого тома `pgdata`. Существующий
том хранит старый пароль. Варианты:

- **Свежий деплой** (данных ещё нет): пересоздать том —
  `docker compose -f docker-compose.prod.yml down -v` затем `up -d`.
  ⚠️ Это УДАЛИТ данные БД. На чистом сервере это безопасно.
- **С данными**: не меняй пароль в `.env`, либо смени его и в самой БД
  (`ALTER USER sellertools PASSWORD '...';`), чтобы совпадал.

## Восстановление из бэкапа

```bash
# Распаковать и залить дамп в БД (перезапишет данные!).
gunzip -c backups/sellertools-2026-07-07_030000.sql.gz | \
  docker compose -f docker-compose.prod.yml exec -T postgres \
  psql -U sellertools -d sellertools
```

## Дашборд Hangfire

В Production дашборд **отключён** (доступен только при `ASPNETCORE_ENVIRONMENT=Development`).
Джобы всё равно выполняются — ночной синк в 03:00 по Ташкенту. Проверить, что джоба
зарегистрирована:

```bash
docker compose -f docker-compose.prod.yml exec -T postgres \
  psql -U sellertools -d sellertools -c \
  "select field, value from hangfire.hash where key='recurring-job:nightly-shop-sync';"
```

## Что мониторить

- **Админ-алерты в Telegram** приходят автоматически при: сбое синка магазина,
  необработанном исключении, остатке дневного лимита Uzum <10%. Не чаще раза в
  10 минут на тип — спама не будет.
- `docker compose ps` — все сервисы `healthy`.
- `backups/` — свежий дамп есть за сегодня.
