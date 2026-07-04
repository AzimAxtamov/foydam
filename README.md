# SellerTools

Инструменты для селлеров Uzum Market через официальный Seller API.
Рабочее название (переименуем при запуске). Слово «uzum» в бренде не используем.

**Фаза 0:** бесплатный отчёт «Сколько Uzum вам должен» — потерянные и бракованные
товары по каждому SKU с оценкой суммы. Плюс задел под выгрузку SKU + ИКПУ в Excel.

Подробный контекст — в [CLAUDE.md](CLAUDE.md).

## Стек

- .NET 8 (собирается SDK 8/9/10, таргет `net8.0`)
- PostgreSQL 16 (Npgsql + EF Core) — хранилище ежедневных снапшотов
- Redis 7 — задел, пока не используется
- Клиент Uzum API: режимы `Mock` (по умолчанию) и `Real`

## Структура

- `src/SellerTools.Core` — сущности домена и модель отчёта, без зависимостей.
- `src/SellerTools.UzumApi` — DTO, `IUzumApiClient`, реальный клиент (rate-limit
  заголовки + ретраи, диагностический `ProbeAsync`) и детерминированный
  `MockUzumApiClient` (~25 SKU).
- `src/SellerTools.App` — хост: EF, расчёт потеряшек, Telegram-бот, шифрование,
  консольная диагностика.

## Режимы запуска

Первый аргумент выбирает режим (по умолчанию `bot`):

```bash
docker compose up -d          # postgres:16 + redis:7

# Telegram-бот (long polling) — основной режим
dotnet run --project src/SellerTools.App -- bot

# Консольная диагностика ключа: три GET-эндпоинта, статусы, rate-limit, тело
dotnet run --project src/SellerTools.App -- diag <shopId> <apiKey>

# Smoke-тест В1: отчёт по мок-магазину + снапшоты в Postgres
dotnet run --project src/SellerTools.App -- smoke
```

`diag` работает и в `Mock` (синтетические ответы), и в `Real` (живой API):

```bash
UzumApi__Mode=Real dotnet run --project src/SellerTools.App -- diag 123456 my-key
```

### Бот (Вечер №3)

`/start` → выбор языка RU/UZ → ввод `shopId` → присылка API-ключа. Сообщение с
ключом удаляется сразу; ключ проверяется запросом к API и сохраняется в БД
только в зашифрованном (AES-256) виде. Ошибки связи с API — дружелюбными
сообщениями (неверный ключ, магазин не найден, лимит, сеть, сервис). `/report` —
задел (полный отчёт в боте будет в В5).

## Секреты

Токен бота и AES-ключ **не хранятся** в `appsettings.json` (он в git). Задайте их
одним из способов:

- файл `src/SellerTools.App/appsettings.Secrets.json` (в `.gitignore`):
  ```json
  { "Bot": { "Token": "<токен>" }, "Crypto": { "KeyBase64": "<base64 32 байта>" } }
  ```
- либо через env: `Bot__Token=...`, `Crypto__KeyBase64=...`

AES-ключ (32 байта, base64) можно сгенерировать: `openssl rand -base64 32`.

## Конфигурация

`src/SellerTools.App/appsettings.json`, секция `UzumApi`:

| Ключ | Назначение |
|------|------------|
| `Mode` | `Mock` \| `Real` |
| `BaseUrl` | `https://api-seller.uzum.uz` |
| `AuthHeaderName` / `AuthScheme` | имя заголовка/схема авторизации (не подтверждены, В2) |
| `MaxRetries` / `RetryBaseDelayMs` | ретраи 429/5xx с бэкоффом |
| `PageSize` | размер страницы при обходе товаров |

Строка подключения к БД — `ConnectionStrings:Postgres` (по умолчанию порт 5432).

## Безопасность

Ключи селлеров, токен бота и AES-ключ — только через `appsettings.Secrets.json`
или env, никогда в коде, логах или git (см. `.gitignore`). Ключ селлера в БД
хранится только зашифрованным (AES-256), plaintext в логи не попадает.
В боте право ключа — «Только чтение», изменить в магазине ничего нельзя.

## Замечания по живому API (В2, частично)

Прогон `diag` по реальному API с невалидным ключом даёт `403 Forbidden` с телом
`RBAC: access denied` (не 401) — заголовок `Authorization: Bearer <key>` доходит
до слоя авторизации. Rate-limit заголовки на 403 не приходят. Единицы дат/денег
и доступ read-only ключа к `finance/*` — проверяются валидным ключом через `diag`.
