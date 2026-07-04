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
  заголовки + ретраи) и детерминированный `MockUzumApiClient` (~25 SKU).
- `src/SellerTools.App` — хост: EF, сервис расчёта потеряшек, smoke-тест.

## Запуск (Вечер №1)

```bash
docker compose up -d          # postgres:16 + redis:7
dotnet run --project src/SellerTools.App
```

Ожидаемо: в консоли — отчёт «Сколько Uzum вам должен» по мок-магазину
(таблица SKU: потеряно/брак/суммы + итоги двумя оценками), в Postgres — строки
снапшотов за сегодня. Процесс отрабатывает smoke-тест и завершается.

По умолчанию `UzumApi:Mode = Mock` — живой ключ не нужен. Для реального API
переключить в `src/SellerTools.App/appsettings.json` (или через env
`UzumApi__Mode=Real`) и задать ключ/`shopId` (появится в В2–В3).

## Конфигурация

`src/SellerTools.App/appsettings.json`, секция `UzumApi`:

| Ключ | Назначение |
|------|------------|
| `Mode` | `Mock` \| `Real` |
| `BaseUrl` | `https://api-seller.uzum.uz` |
| `AuthHeaderName` / `AuthScheme` | имя заголовка/схема авторизации (не подтверждены, В2) |
| `MaxRetries` / `RetryBaseDelayMs` | ретраи 429/5xx с бэкоффом |
| `PageSize` | размер страницы при обходе товаров |

Строка подключения к БД — `ConnectionStrings:Postgres`. Контейнер Postgres
проброшен на хост-порт **5433** (чтобы не конфликтовать с локально установленным
Postgres на 5432).

## Безопасность

Ключи селлеров и токен бота — только через конфиг/env, никогда в коде, логах
или git. `.env` и `*.local.json` в `.gitignore`. Ключ будет храниться только
зашифрованным (AES, В3).
