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

## 3. Клонировать репозиторий (через SSH)

Репозиторий **приватный**, а GitHub с 2021 г. **не принимает пароль** при
клонировании по HTTPS (ошибка `Password authentication is not supported`).
Поэтому клонируем по SSH — настраивается один раз, дальше без паролей.

```bash
# git обычно уже есть; если нет: sudo apt install -y git

# 3.1. Сгенерировать SSH-ключ (Enter на все вопросы — без пароля на ключ):
ssh-keygen -t ed25519 -C "vps-foydam" -f ~/.ssh/id_ed25519 -N ""

# 3.2. Показать публичный ключ — скопировать ВЕСЬ вывод:
cat ~/.ssh/id_ed25519.pub
```

3.3. Добавить ключ в GitHub: открой <https://github.com/settings/ssh/new>,
Title — `foydam-vps`, Key — вставь скопированное, **Add SSH key**.

```bash
# 3.4. Проверить связь (на "Are you sure..." набери yes):
ssh -T git@github.com
# Ожидаемо: Hi AzimAxtamov! You've successfully authenticated...

# 3.5. Клонировать по SSH-адресу (git@, НЕ https):
git clone git@github.com:AzimAxtamov/foydam.git
cd foydam
```

> Альтернатива без SSH: создать Personal Access Token на
> <https://github.com/settings/tokens> (scope `repo`) и ввести его **вместо
> пароля** при `git clone https://github.com/AzimAxtamov/foydam.git`.

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

## Миграции БД и baseline существующей прод-БД

С перехода на модель «синк → Postgres → отчёт из БД» схема управляется **EF-миграциями**
(`InitialCreate` + `AddSyncStorage`), а не прежним `EnsureCreated`. При старте приложение
само вызывает `Migrate()` — новые таблицы (`Invoices`, `Returns`, `Skus`, `ShopSyncStates`
и др.) создаются автоматически.

⚠️ **Если прод-БД была создана СТАРОЙ версией** (на `EnsureCreated`), у неё есть таблицы
`Sellers`/`Shops`/…, но НЕТ таблицы `__EFMigrationsHistory`. Прямой `Migrate()` попытался
бы создать уже существующие таблицы и упал бы с `relation "Sellers" already exists`.

**Приложение обрабатывает это автоматически** ([`MigrationBaseline`](src/SellerTools.App/Data/MigrationBaseline.cs)):
при старте, если найдены legacy-таблицы без строки `InitialCreate` в истории — оно
помечает `InitialCreate` применённой (baseline), и `Migrate()` накатывает **только**
`AddSyncStorage`. Отдельных действий не требуется — просто выкати новый образ.

Проверить, что baseline прошёл и обе миграции применены:

```bash
docker compose -f docker-compose.prod.yml exec -T postgres \
  psql -U sellertools -d sellertools -c \
  'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
```

Ожидаемо: две строки — `..._InitialCreate` и `..._AddSyncStorage`.

**Ручной baseline** (если понадобится сделать вручную — обычно НЕ нужно):

```bash
docker compose -f docker-compose.prod.yml exec -T postgres \
  psql -U sellertools -d sellertools -c \
  "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\"(\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL);
   INSERT INTO \"__EFMigrationsHistory\" VALUES ('20260710071528_InitialCreate','8.0.0') ON CONFLICT DO NOTHING;"
```

### Приёмка: первичные синки существующих магазинов

После первого старта на новой архитектуре приложение находит магазины **без состояния
синка** ([`StartupProvisioner`](src/SellerTools.App/Services/StartupProvisioner.cs)),
помечает их `Never` и ставит первичные полные синки в очередь `uzum-sync` **с интервалом
2 минуты** между магазинами (чтобы не завалить Uzum). Первый отчёт по магазину придёт
после завершения его синка. Проверить состояние:

```bash
docker compose -f docker-compose.prod.yml exec -T postgres \
  psql -U sellertools -d sellertools -c \
  'SELECT "ShopId","Status","LastFullSyncAt","Inactive" FROM "ShopSyncStates";'
```

`Status`: 0=Never, 1=Syncing, 2=Ready, 3=Error.

---

## Автодеплой через GitHub Actions (CI/CD)

Настроен workflow `.github/workflows/deploy.yml`: на каждый push в `main` →
`dotnet build` + `dotnet test` → сборка Docker-образа и публикация в GHCR →
деплой на VPS по SSH (`docker compose pull && up -d`). Сборка/тесты **падают до**
сборки образа и деплоя — битый код на прод не уедет.

**Что настроить один раз:**

1. **Секреты репозитория** (Settings → Secrets and variables → Actions → New secret):
   | Secret | Значение |
   |--------|----------|
   | `HOST` | IP или домен VPS |
   | `USER` | SSH-пользователь (напр. `root`) |
   | `SSH_KEY` | приватный SSH-ключ, чей публичный лежит в `~/.ssh/authorized_keys` на VPS |

   > `GITHUB_TOKEN` создаётся автоматически — им же VPS логинится в GHCR для pull.
   > Отдельный PAT не нужен, пока образ в том же приватном репозитории.

2. **На VPS** должен быть склонирован репозиторий в `~/foydam` и заполнен `.env`
   (см. шаги 3–4 выше). CI сам пропишет в `.env` строку `APP_IMAGE=ghcr.io/...:<sha>`
   перед `pull`, поэтому конкретный образ подтягивается автоматически.

3. **Первый деплой** всё равно делается руками по шагам 1–8. CI берёт на себя
   только последующие обновления при push в `main`.

Проверить, что образ публикуется: вкладка репозитория **Packages** (после первого
успешного прогона там появится `foydam`).

## Обновление вручную (если нужно без CI)

```bash
cd foydam
git pull
# Собрать образ локально на VPS и поднять:
docker compose -f docker-compose.prod.yml up -d --build
```

> При включённом CI обновление приходит само после push в `main` — ручной вариант
> нужен только для отладки или отката.

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

---

## Известные особенности Contabo / VPS

Разбор реального инцидента (ночь отладки прода). Держи под рукой при похожих симптомах.

### Симптом для будущей диагностики

> **Если `curl` из контейнера к внешнему API работает, а само приложение (.NET)
> к нему же падает — почти наверняка дело в IPv6 или в пуле соединений, а не в
> «сети вообще».**

`curl` молча фолбэчится с IPv6 на IPv4 при недоступном IPv6-маршруте, а .NET по
умолчанию идёт по первому адресу из DNS (часто AAAA/IPv6) и падает `network
unreachable`. Поэтому диагностика curl'ом обманывает.

### 1. IPv6 отключён в приложении (уже в compose)

В `docker-compose.prod.yml` у сервиса `app` задано
`DOTNET_SYSTEM_NET_DISABLEIPV6: "1"`. Причина: Contabo **теряет IPv6-маршрут через
несколько часов аптайма**, резолвер отдаёт AAAA первым, .NET шёл по IPv6 и ловил
`network unreachable`. Флаг заставляет .NET использовать только IPv4.
**Ничего делать не нужно — это уже в репозитории.**

### 2. Пул соединений (уже в клиенте)

`SocketsHttpHandler.PooledConnectionLifetime = 60с` (см. `UzumApiOptions`). Край
Uzum рвёт keep-alive после ответа; без ограничения жизни .NET переиспользует
«мёртвое» соединение из пула и падает `HttpRequestException`. Лимит жизни заставляет
пересоздавать соединение. **Тоже уже в коде.**

### 3. DNS для Docker-демона (защитная настройка, ставится руками)

В итоге виновником оказался не DNS, но настройку решено оставить как защитную —
чтобы резолвинг внутри контейнеров не зависел от системного resolver'а хоста.

```bash
# Один раз на хосте:
sudo tee /etc/docker/daemon.json >/dev/null <<'JSON'
{ "dns": ["1.1.1.1", "8.8.8.8"] }
JSON
sudo systemctl restart docker

# После рестарта Docker подними стек заново:
cd ~/foydam && docker compose -f docker-compose.prod.yml up -d
```

> Это настройка **хоста**, в git её внести нельзя — поэтому она отдельным шагом здесь.
> Если файл уже есть с другим содержимым — не затирай, а добавь ключ `dns` аккуратно.
