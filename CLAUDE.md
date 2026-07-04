# CLAUDE.md — контекст проекта

## Что мы строим

Telegram-бот для селлеров маркетплейса Uzum Market (Узбекистан). Работаем через
**официальный Seller API** по ключу, который селлер сам создаёт в личном кабинете.

**Фаза 0 (текущая, строим сейчас):** бесплатный вирусный инструмент —
отчёт «Сколько Uzum вам должен»: потерянные и бракованные товары по каждой позиции
с оценкой суммы. Плюс выгрузка SKU + ИКПУ-кодов в Excel (нужна селлерам для ЭТТН).
Внутри бота НЕТ упоминаний платного и рекламы — это принципиально.

**Фаза 1 (позже, НЕ строить сейчас, но учитывать в архитектуре):** платная подписка —
чистая прибыль по SKU с вводом закупочной себестоимости пользователем, календарь
выплат, ежедневный дайджест, алерты (возвраты, остатки, отмены).

**Языки интерфейса:** русский + узбекский. Структура i18n с первого дня
(ресурсы/словари), тексты UZ можно добавить позже.

**Брендинг:** слово «uzum» НЕ использовать в имени бота, бренде, доменах
(чужой товарный знак). Рабочее название решения: `SellerTools` (нейтральное,
переименуем при запуске).

## Конкурентный контекст (для понимания решений)

Конкурент Market Plus (@uzumplusbot) подключается к магазинам селлеров через
добавление своего номера «сотрудником» в кабинет (не API) и показывает «чистую
прибыль», рассчитанную из поля purchasePrice в ЛК, которое селлеры не заполняют
или заполняют мусором → показывает бред (реальный кейс: −35 млн сум).

Наши отличия, зашитые в дизайн:
1. Только официальный API, ключ «Только чтение» — бот физически не может ничего
   изменить. Это главный аргумент доверия, беречь его в каждом решении.
2. Ключ вводится только в бота (не пересылкой человеку), сообщение с ключом
   немедленно удаляется, ключ хранится только шифрованным (AES), никогда не
   попадает в логи.
3. Прибыль (Фаза 1) считаем по себестоимости, введённой у нас, а не по грязному
   полю из ЛК.

## Официальный Seller API — вся добытая информация

- База: `https://api-seller.uzum.uz`
- Swagger UI: `https://api-seller.uzum.uz/api/seller-openapi/swagger/swagger-ui/webjars/swagger-ui/index.html`
- Ключ создаётся в ЛК селлера **на конкретный магазин**, права: «Только чтение» /
  «Редактирование», срок: от 1 месяца до бессрочного.
- **Имя auth-заголовка НЕ ПОДТВЕРЖДЕНО.** Дефолт: `Authorization: Bearer <key>`.
  Сделать конфигурируемым (`UzumApi:AuthHeaderName`, `UzumApi:AuthScheme`;
  пустая схема = сырое значение ключа в заголовке).

### Эндпоинты (подтверждены в swagger)

**GET `/v1/product/shop/{shopId}`** — товары и остатки магазина.
Query: `searchQuery`, `sortBy` (DEFAULT|ORDERS|PRICE|ID|ROI|CONVERSION|LEFTOVERS|CREATED_AND_TITLE),
`order` (ASC|DESC), `size`* , `page`*, `productRank` (A|B|C|N|D),
`filter` (ALL|ACTIVE|INACTIVE|WARNING|WITH_SKU|ARCHIVE|DEFECTED|WITHOUT_REQUIRED_FILTERS).
Ответ (схема подтверждена полностью):

```json
{
  "productList": [
    {
      "productId": 0,
      "category": "string",
      "rating": "string",
      "status": { "id": 0, "title": "string", "value": "NO_SKU", "description": "string", "color": "string" },
      "moderationStatus": { "title": "string", "color": "string", "value": "NOT_MODERATED", "hasAnySkuBlocked": true },
      "commissionDto": { "minCommission": 0, "maxCommission": 0 },
      "skuList": [
        {
          "skuTitle": "string",
          "skuFullTitle": "string",
          "productTitle": "string",
          "skuId": 0,
          "quantityCreated": 0,
          "quantityActive": 0,
          "quantityFbs": 0,
          "quantityAdditional": 0,
          "quantityOnPhotoStudio": 0,
          "quantityArchived": 0,
          "quantitySold": 0,
          "quantityReturned": 0,
          "quantityMissing": 0,
          "quantityDefected": 0,
          "quantityPending": 0,
          "returnedPercentage": 0,
          "barcode": 0,
          "archived": true,
          "commission": 0,
          "characteristics": "string",
          "purchasePrice": 0,
          "price": 0,
          "blockingReason": "string",
          "blocked": true,
          "rankInfo": { "rank": "A", "rankValue": "string", "dateUpdated": "..." },
          "article": "string",
          "turnover": 0,
          "paidStorageDimensionalGroup": { "group": "SMALL", "title": "string" },
          "paidStoragePriceItem": 0,
          "paidStorageAmount": 0,
          "hasStudioPhoto": true,
          "avgdsales": 0,
          "avgdquantity": 0,
          "pstorage": true,
          "previewImage": "string",
          "ikpu": "string",
          "sellerItemCode": "string"
        }
      ],
      "title": "string",
      "quantityActive": 0,
      "quantityFbs": 0
    }
  ],
  "totalProductsAmount": 0
}
```

Ключевые поля: `quantityMissing` (потеряно), `quantityDefected` (брак),
`purchasePrice` (закуп из ЛК — не доверять), `commission`, `ikpu`,
`paidStorageAmount`, `avgdsales` (средние дневные продажи), `rankInfo` (ABC от Uzum).

**GET `/v1/finance/orders`** — позиции заказов (финансы).
Query: `page` (def 0), `size` (def 20), `group` (bool: true → ProductGroupedSellerItem,
false → SellerOrderItemDto), `dateFrom`/`dateTo` (int64 — вероятно epoch millis,
ПРОВЕРИТЬ), `statuses` (TO_WITHDRAW | PROCESSING | CANCELED | PARTIALLY_CANCELLED),
`shopIds`* (array).
Ответ: `{ "orderItems": [...], "totalElements": 0 }`.
Поля `SellerOrderItemDto` (типы не подтверждены — деньги считать long):
`id, status, date, orderId, skuTitle, productId, productImage, shopId, dateIssued,
sellerPrice, amount, amountReturns, commission, sellerProfit, purchasePrice,
logisticDeliveryFee, cancelled, withdrawnProfit, returnCause, comment,
skuCharValue, skuCharTitle, productTitle`.
Поля `date`/`dateIssued` в DTO пока НЕ добавлять с жёстким типом — уточнить
живым ключом (могут быть epoch или ISO).

**GET `/v1/finance/expenses`** — расходы продавца.
Query: `page`, `size`, `shopId` (deprecated) / `shopIds` (array), `dateFrom`,
`dateTo` (int64), `sources` (array<string> — категории: логистика/маркетинг/склад…).
Ответ:

```json
{
  "payload": {
    "payments": [
      {
        "id": 0,
        "dateCreated": "2026-07-03T08:55:59.183Z",
        "dateUpdated": "2026-07-03T08:55:59.184Z",
        "name": "string",
        "source": "string",
        "shopId": 0,
        "sellerId": 0,
        "paymentPrice": 0,
        "amount": 0,
        "status": "CREATED",
        "externalId": "string",
        "code": "string",
        "dateService": "2026-07-03T08:55:59.184Z",
        "type": "OUTCOME"
      }
    ]
  },
  "errors": [ { "code": "string", "message": "string" } ],
  "timestamp": "...", "trace": "...", "error": null
}
```

**GET `/v3/fbs/sku/stocks`** — остатки FBS постранично (v2 deprecated).
**POST `/v2/fbs/sku/stocks`** — обновление остатков (write, НЕ использовать в Фазе 0).
**POST `/v1/product/{shopId}/sendPriceData`** — изменение цен (write, Фаза 2).

### Rate limits (подтверждены заголовками в swagger)

Каждый ответ несёт: `x-ratelimit-remaining`, `x-ratelimit-replenish-rate`,
`x-ratelimit-burst-capacity`, `x-ratelimit-requested-tokens`,
`x-ratelimit-limit-per-day`, `x-ratelimit-remaining-per-day`.
Модель: посекундный token bucket + **дневной лимит**. Значения неизвестны до
живого ключа. Клиент обязан: читать и логировать заголовки, ретраить 429/5xx с
экспоненциальным бэкоффом, проектировать синки инкрементально (dateFrom от
последнего курсора), не делать полных перекачек без необходимости.

### Неизвестное (проверить живым ключом — вечер №2)

1. Имя auth-заголовка (дефолт Authorization: Bearer, конфигурируемо).
2. Как узнать shopId по ключу (ключ per-shop; возможно есть эндпоинт списка
   магазинов — поискать в swagger; пока shopId вводится вручную).
3. Отдаёт ли read-only ключ `finance/*` эндпоинты.
4. Единицы дат (`dateFrom` int64 — millis?) и формат `date` в ответах.
5. Единицы денег (сум или тийины) — в коде везде long + TODO.
6. Реальные значения лимитов.
7. Глубина истории `finance/orders`.

## Формулы

**Потеряшки (Фаза 0):** по каждому SKU, где `quantityMissing + quantityDefected > 0`:
- оценка по цене продажи: `(missing + defected) × price`
- оценка по закупу: `(missing + defected) × purchasePrice`, только если `purchasePrice > 0`
- итоги по магазину обеими оценками. В отчёте показывать обе, с пометкой, что
  закуп из ЛК может быть не заполнен.

**Прибыль (Фаза 1, для архитектурного учёта):**
`sellerPrice×qtyNet − commission − logisticDeliveryFee − нашCOGS×qtyNet − доля(expenses)`,
где qtyNet = amount − amountReturns (нетто/брутто уточнить живым ключом).

## Архитектура

- **.NET 8**, PostgreSQL (Npgsql + EF Core), Redis (задел, пока не используется),
  Hangfire (вечер №4), Telegram.Bot (вечер №3). Версии пакетов — плавающие `8.*`.
- Проекты:
  - `SellerTools.Core` — сущности домена, без зависимостей.
  - `SellerTools.UzumApi` — DTO, `IUzumApiClient`, реальный клиент (HttpClient,
    обработка rate-limit заголовков, ретраи), `MockUzumApiClient` (детерминированный,
    seed; ~25 SKU с узбекско-русскими названиями, у 5–7 есть missing/defected).
  - `SellerTools.App` — хост: DI, EF, джобы, бот.
- Сущности: `Seller` (Id = Telegram user id, Language ru|uz),
  `Shop` (Id = Uzum shopId, ApiKeyEncrypted, KeyExpiresAt),
  `SkuDailySnapshot` (**ров данных**: ежедневный срез quantityMissing/Defected/…
  по каждому SKU — API истории не отдаёт, копим сами; уникальный индекс
  ShopId+Date+SkuId), `SyncCursor` (ShopId+Kind составной ключ).
- Режим API: `UzumApi:Mode = Mock | Real` в appsettings.
- Деньги: `long`. Мок — детерминированный (фиксированный seed).

## Роадмап по вечерам

- **В1 (СДЕЛАТЬ СЕЙЧАС):** solution из 3 проектов, docker-compose (postgres:16,
  redis:7), EF-модели + EnsureCreated, DTO, IUzumApiClient + реальный клиент +
  мок, сервис расчёта потеряшек, smoke-тест как IHostedService.
  **Definition of done:** `docker compose up -d && dotnet run --project
  src/SellerTools.App` → в консоли отчёт «Сколько Uzum вам должен» по мок-магазину
  (таблица SKU: потеряно/брак/суммы + итоги двумя оценками), в Postgres — строки
  снапшотов за сегодня. Сборка без ошибок, README с этими командами.
- **В2:** живой ключ — прогон реального клиента, фикс типов по чек-листу
  «Неизвестное», добавление полей дат в DTO.
- **В3:** Telegram-бот (long polling): /start, выбор языка RU/UZ, приём ключа
  (немедленно удалять сообщение с ключом), AES-шифрование ключа, привязка Shop.
- **В4:** Hangfire + ночной синк снапшотов по всем магазинам; отчёт по команде.
- **В5:** отчёт красиво в боте + Excel-экспорт (ClosedXML): потеряшки и ИКПУ-выгрузка.
- **В6:** беты на живых селлерах, фиксы.
- **В7:** деплой на VPS (docker), логи/алерты об ошибках.

## Правила работы

- Не выдумывать эндпоинты и поля сверх задокументированных здесь — если чего-то
  не хватает, пометить TODO и спросить.
- Секреты (ключи селлеров, токен бота) — только через конфиг/env, не в коде,
  не в логах, не в git.
- Комментарии и коммиты — по-русски, кратко.
- После каждого вечера — git commit.
