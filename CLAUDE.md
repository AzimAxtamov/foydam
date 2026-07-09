# CLAUDE.md — контекст проекта

> **Текущее состояние (обновлять после каждого вечера!).**
> Сделаны вечера **В1, В3, В4, В5, В7** (см. Роадмап). Работает: Telegram-бот
> (@foydam_bot) с приёмом ключа, авто-определением магазина, карточным отчётом,
> двумя Excel-выгрузками, кнопкой «Обновить»; ночной синк снапшотов на Hangfire;
> production-обвязка (Docker, бэкапы, админ-алерты, Serilog) с `DEPLOY.md`.
> **В2 почти закрыт [ЖИВОЙ КЛЮЧ]:** канон-путь `/api/seller-openapi/`, деньги = СУМ,
> даты = epoch millis (и в expenses тоже, спека врала), finance/* доступны read-only
> ключом, дневной лимит 100 000. DTO поправлены под живые данные (commission=double,
> nullable-поля). Осталось: глубина истории `finance/orders` (нужен магазин с историей).
> **[ПОТЕРЯШКИ ПЕРЕПИСАНЫ]:** `quantityMissing` МЁРТВ (deprecated, всегда 0) —
> заменён на балансовую реконсиляцию `lost = accepted − removed − sold − defected −
> stock` из `/v1/invoice` (ACCEPTED) + `/v1/return` (legacy-правило) + карточки.
> Валидировано на живом бета-селлере. Полная спека — `docs/loss-reconciliation.md`.
> Отчёт строится из свежих API-данных (не из снапшота — там нет accepted/removed).
> COGS = только введённый селлером (в Фазе 0 нет → «закуп не указан»), purchasePrice
> из API грязный, не подставляем. HTTP-клиент: параллелизм ≤3, ретрай на socket reset.
> **Открыто:** В6 (беты на живых селлерах), деплой на реальный VPS. По умолчанию
> `UzumApi:Mode=Mock`. Репозиторий приватный: `AzimAxtamov/foydam`, клон по SSH.
>
> ⚠️ **ВРЕМЕННЫЙ ТЕСТ-КОСТЫЛЬ (убрать до реальных клиентов!):** в `OnKeyAsync`
> (TelegramBotService) присланный API-ключ пересылается админу в Telegram —
> помечено `TODO(remove-before-prod)`. Нарушает принцип доверия (ключ не должен
> уходить человеку); включён осознанно для тестов на живых ключах. **Обязательно
> удалить перед бетой на реальных селлерах.**

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
(ресурсы/словари). **[В5] Покрытие обоих языков — 100%** (все тексты бота, отчётов
и Excel-шапок в `Loc`, ключ `MissingKeys` для самопроверки). ⚠️ **UZ — ЧЕРНОВОЙ
машинный перевод (латиница), обязательно вычитать носителем перед публичным
запуском.** RU — эталон, фолбэк при отсутствии ключа.

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

> Полная спецификация лежит в репозитории: `uzum-openapi.json` (OpenAPI 3.0.0,
> 35 путей, 107 схем). Разобрана вечером анализа спеки — находки ниже помечены **[SPEC]**.

- База хоста: `https://api-seller.uzum.uz`
- **[ЖИВОЙ КЛЮЧ] Канонический base-path ПОДТВЕРЖДЁН: `https://api-seller.uzum.uz/api/seller-openapi/`.**
  Матрица `diag` (2 пути × 2 схемы) на `GET /v1/shops` показала разницу в теле:
  - путь **без** префикса (`/v1/shops`) → `403 RBAC: access denied` — это **внешний
    шлюз**, до Seller API запрос не доходит;
  - путь **с** `/api/seller-openapi/` → структурный JSON
    `{"errors":[{"code":"forbidden-001","message":"Token not found"}]}` — это ответ
    **самого Seller API**.
  Значит канонический путь — **только с `/api/seller-openapi/`**.
  → **Внедрено:** `UzumApiOptions.BaseUrl` = `…/api/seller-openapi/` (со слешем в
  конце), запросы в клиенте делаются относительными (`RelativeUri`, убирает ведущий
  `/`, иначе абсолютный путь отбросил бы префикс). appsettings обновлён.
- ⚠️ **[БАГ, исправлен] Auth-заголовок:** `ApplyAuth` использовал
  `AuthenticationHeaderValue.Parse(key)` — он валидирует значение и на ключе с `:`
  (реальный формат `<digits>:<base64>`) **бросал исключение С ТЕКСТОМ КЛЮЧА**
  (утечка + падение на валидных ключах, классифицировалось как `Network`). Заменено
  на `TryAddWithoutValidation` — без валидации и без утечки. Схема пустая (сырой ключ).
- Swagger UI: `https://api-seller.uzum.uz/api/seller-openapi/swagger/swagger-ui/webjars/swagger-ui/index.html`
- Ключ создаётся в ЛК селлера **на конкретный магазин**, права: «Только чтение» /
  «Редактирование», срок: от 1 месяца до бессрочного.
- **[SPEC] Авторизация ПОДТВЕРЖДЕНА (закрыт вопрос №1).** `securitySchemes.TokenAuth`:
  `type: apiKey`, `in: header`, `name: Authorization`,
  описание — **«Токен авторизации без префикса Bearer»**. Значит правильно:
  заголовок `Authorization: <сырой ключ>`, **БЕЗ схемы `Bearer`**.
  → **Уже исправлено в коде:** дефолт `UzumApi:AuthScheme=""` (пустая схема =
  сырое значение ключа), `AuthHeaderName=Authorization`; в appsettings тоже `""`.
  Глобальный `security: [{ TokenAuth: [] }]` — применяется ко всем эндпоинтам.
  ⚠️ Оговорка: живой `diag` с **невалидным** ключом даёт `403 RBAC: access denied`
  и с `Bearer`, и с пустой схемой (RBAC режет любой мусорный ключ до разбора
  формата), поэтому живьём эти два варианта неразличимы — источник истины здесь
  спека. Окончательно подтвердить пустую схему **валидным** ключом (вечер №2).

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
Ответ: `GenericResponseSellerFbsSkuStockUpdateDtoV2` (envelope с `payload`).
Элемент остатка `RestSellerSkuFbsAmountDto`: `skuId(int64)`, `skuTitle`,
`productTitle`, `barcode(string!)`, `amount(int32)`, `fbsLinked`, `dbsLinked`.
**POST `/v2/fbs/sku/stocks`** — обновление остатков (write, НЕ использовать в Фазе 0).
**POST `/v1/product/{shopId}/sendPriceData`** — изменение цен (write, Фаза 2).

### [SPEC] Полный список эндпоинтов (35; из uzum-openapi.json)

Помечены: **NEW** — не было в CLAUDE.md; ⭐ — потенциально полезно для Фаз 0/1.
Все — под глобальным `TokenAuth`. Пути даны без server-префикса `/api/seller-openapi/`.

**Shop** ⭐
- `GET /v1/shops` — **NEW ⭐** список своих магазинов → `[{id,name}]` (закрывает вопрос №2).

**Product**
- `GET /v1/product/shop/{shopId}` — SKU магазина (уже используем) → `AllProducts`.
- `GET /v1/product/barcodes/types` — **NEW** справочник размеров этикеток → `BarcodeTypes`.
- `POST /v1/product/shop/{shopId}/barcodes/print` — **NEW** печать этикеток SKU (write).
- `POST /v1/product/{shopId}/sendPriceData` — изменение цен (write, Фаза 2).

**Finance** ⭐
- `GET /v1/finance/orders` — позиции заказов → `FinanceOrderItemsDto`.
- `GET /v1/finance/expenses` — расходы → `GenericResponseSellerPaymentInfoDtoList`.

**Stocks**
- `GET /v3/fbs/sku/stocks` — остатки FBS (постранично).
- `GET /v2/fbs/sku/stocks` — **DEPRECATED**.
- `POST /v2/fbs/sku/stocks` — обновление остатков (write).

**Return Invoice** ⭐ (пригодится для Фазы 1 — алерты по возвратам)
- `GET /v1/return` — **ИСПОЛЬЗУЕМ ⭐** возвраты продавца → `array<SellerReturnDto>`
  (голый массив, size=50). Даёт `removed` (физвывоз) для баланса потерь по
  legacy-правилу (`totalPackedAmount==0 && assembledDate==null` → `amount`, иначе
  `packedAmount`). Матч SKU — по `NormalizeSkuKey(item.skuTitle)` (полный код лежит
  здесь в `skuTitle`, в карточке — в `skuFullTitle`). DTO: `ReconciliationDtos.cs`.
- `GET /v1/shop/{shopId}/return` — **NEW** накладные возврата → `SellerReturnLite`.
- `GET /v1/shop/{shopId}/return/{returnId}` — **NEW** состав накладной возврата.

**FBO Invoice** (накладные поставки на склад Uzum)
- `GET /v1/invoice` — **ИСПОЛЬЗУЕМ ⭐** список накладных → `array<InvoiceWithSkuListDto>`
  (голый массив, size=50). Даёт `accepted` для баланса потерь: сумма
  `quantityAccepted` по SKU **только** накладных `invoiceStatus.value=="ACCEPTED"`.
  Матч SKU — по `NormalizeSkuKey(sku.skuTitle)` (полный код лежит здесь в `skuTitle`,
  в карточке — в `skuFullTitle`; см. Формулы). DTO: `ReconciliationDtos.cs`.
- `GET /v1/shop/{shopId}/invoice` — **NEW** накладные поставки по магазину.
- `GET /v1/shop/{shopId}/invoice/products` — **NEW** состав накладной.

**FBS Invoice** (накладные отгрузки FBS; много — все NEW, для Фазы 0 не нужны)
- `GET/POST /v1/fbs/invoice`, `GET /v1/fbs/invoice/{invoiceId}`,
  `POST …/cancel`, `GET …/orders`, `GET …/print`, `GET …/closing-documents`,
  `POST …/update-content`, плюс `…/dop/drop-off-points`, `…/dop/time-slot`.

**Работа с заказами FBS/DBS** (все NEW; для Фазы 0 не нужны — это операционка)
- `GET /v2/fbs/orders` — заказы продавца (пагинация, `dateFrom/dateTo` int64).
- `GET /v2/fbs/orders/count` — количество заказов.
- `GET /v1/fbs/order/{orderId}`, `GET /v1/fbs/order/return-reasons`,
  `POST …/confirm`, `…/cancel`, `…/identifier`, `GET …/labels/print` (write/операции).
- DBS: `POST /v1/dbs/order/{orderId}/completed | /delivering | /refund`.

### [SPEC] Сверка наших DTO с реальными схемами

Реальные имена схем (не совпадают с нашими типами, но по JSON — та же форма):
- Ответ товаров — **`AllProducts`** = `{ productList: SellerProductCard[],
  totalProductsAmount: int32 }`. Наш `ProductShopResponse` — форма совпадает.
- Карточка товара — **`SellerProductCard`** (наш `ProductDto`). Есть `commission(double)`
  и `commissionDto: Commission {minCommission, maxCommission — оба double}`.
- SKU — **`SkuForTable`** (наш `SkuDto`). Форма совпадает.

⚠️ **Несоответствия типов (исправить в наших DTO, В2):**
- `SkuDto.Commission` у нас `long` → в спеке **`double`**. Также `double`:
  `returnedPercentage`, `turnover`, `avgdsales`, `avgdquantity` (у нас double —
  ок), `commission` на уровне карточки. **Комиссию хранить как double, не long.**
- Деньги-поля SKU (`price`, `purchasePrice`, `paidStoragePriceItem`,
  `paidStorageAmount`) — **`int64`** → наш `long` верен.
- `barcode` — `int64` в SKU (наш `long` ✓), но `int64` НЕ везде: в
  `RestSellerSkuFbsAmountDto.barcode` — **`string`**. Не путать.
- `SellerOrderItemDto.cancelled` — **`int64` (количество отмен)**, не bool.
- Enum'ы статусов (для справки):
  `ProductStatus.value` = NO_SKU | IN_STOCK | READY_TO_SEND | SENT | RUN_OUT |
  BLOCKED | SKU_BLOCKED | ARCHIVED | DELETED | PERM_BANNED | NOT_READY_TO_SEND;
  `ProductModerationStatus.value` = NOT_MODERATED | MODERATED | ON_MODERATION |
  HAS_COMPLAINTS | ON_PREMODERATION | PERM_BANNED;
  `SellerPaymentDto.status` = CREATED | REFUNDED | CONFIRMED | CANCELED;
  `SellerPaymentDto.type` = OUTCOME | INCOME;
  `SellerReturnDto.type` = DEFECTED | RETURN | FBS.

### [SPEC] Финансовые схемы (детали для Фазы 1)

- `FinanceOrderItemsDto` = `{ orderItems: FinanceItemEntity[], totalElements: int32 }`.
  `group=false` → элементы **`SellerOrderItemDto`** (наследует `FinanceItemEntity`):
  `id, status(enum TO_WITHDRAW|PROCESSING|CANCELED|PARTIALLY_CANCELLED),
  date(int64 millis), dateIssued(int64 millis), orderId, skuTitle, productId,
  productImage, shopId, sellerPrice(int64), amount(int), amountReturns(int),
  commission(int64), sellerProfit(int64), purchasePrice(int64),
  logisticDeliveryFee(int64), cancelled(int64), withdrawnProfit(int64),
  returnCause, comment, skuCharValue, skuCharTitle, productTitle`.
  `group=true` → **`ProductGroupedSellerItem`** = `{ productId, shopId, productTitle,
  items: SkuGroupedSellerItemDto[] }`; в `SkuGroupedSellerItemDto` деньги int64:
  `sellPrice, purchasePrice, sellerDiscountAmount, sellerProfit, commission,
  withdrawnProfit, logisticDeliveryFee`, плюс `amount, amountReturns (int)`.
- `finance/expenses` → **`SellerPaymentDto`**: `id(int64), dateCreated/dateUpdated/
  dateService(ISO date-time!), name, source, shopId, sellerId,
  paymentPrice(int64), amount(int32), status(enum), externalId, code, type(enum)`.
  Обёрнуто в generic-envelope `{ payload:{payments:[…]}, errors[], timestamp, trace, error }`.

### Rate limits (подтверждены заголовками в swagger)

Каждый ответ несёт: `x-ratelimit-remaining`, `x-ratelimit-replenish-rate`,
`x-ratelimit-burst-capacity`, `x-ratelimit-requested-tokens`,
`x-ratelimit-limit-per-day`, `x-ratelimit-remaining-per-day`.
Модель: посекундный token bucket + **дневной лимит**. Значения неизвестны до
живого ключа. Клиент обязан: читать и логировать заголовки, ретраить 429/5xx с
экспоненциальным бэкоффом, проектировать синки инкрементально (dateFrom от
последнего курсора), не делать полных перекачек без необходимости.

⚠️ **[ЖИВОЙ КЛЮЧ] Правила HTTP-клиента (внедрены):**
- **Параллелизм ≤ 3** одновременных запросов (`UzumApiOptions.MaxConcurrency=3`,
  статический `SemaphoreSlim`, общий на процесс). При большем **Uzum рвёт коннекты**.
- **Ретрай на разрыв соединения** (`HttpRequestException`: socket reset — край Uzum
  закрывает keep-alive) в `SendWithRetryAsync`, не только на HTTP-статусы.
- **Даты в параметрах `finance/*` — epoch СЕКУНДЫ**, в ответах — millis. Прислать
  millis в параметрах → сервер молча вернёт пусто (200, ноль записей). Делить на 1000.
- **`finance/expenses`: `payload.totalElements` = 0** даже при непустых данных →
  пагинация до пустой страницы, не по счётчику. (Комментарии-грабли — в `BuildProbeUrl`.)

### Неизвестное (статус после разбора спеки)

1. ✅ **Закрыто [SPEC].** Auth = `Authorization: <ключ>` без `Bearer` (см. выше).
2. ✅ **Закрыто [SPEC] + ВНЕДРЕНО.** shopId по ключу → **`GET /v1/shops`** (без
   параметров) отдаёт `array<OrganizationDto>` = `[{ id: int64, name: string }]`.
   Реализовано: `IUzumApiClient.GetShopsAsync`, `KeyVerification.DiscoverShopsAsync`
   (валидирует ключ и заодно даёт магазины). Бот больше НЕ спрашивает shopId —
   после ключа сам зовёт `/v1/shops`: один магазин → привязка сразу, несколько →
   выбор кнопками. Имя магазина (`name`) сохраняется в `Shop.Title`.
3. ✅ **ЗАКРЫТО [ЖИВОЙ КЛЮЧ].** Read-only ключ **ОТДАЁТ** `finance/orders` и
   `finance/expenses` — оба вернули `200` (на активном магазине). Доступ есть.
   На неактивном магазине — `403 forbidden-001 "Shop is not available"`.
4. ✅ **ЗАКРЫТО [ЖИВОЙ КЛЮЧ] — спека ВРАЛА по expenses:**
   - `finance/orders`: `date`, `dateIssued` — **int64 Unix epoch millis** (13 цифр,
     напр. `1774693507364`). ✓ как в спеке.
   - `finance/expenses`: `dateCreated`, `dateUpdated`, `dateService` — **тоже int64
     epoch millis** (напр. `1775209349730`), а НЕ ISO-строки как обещала спека!
     `dateUpdated` может быть `null`.
   - Ошибочный `timestamp` в error-envelope — ISO с наносекундами без TZ
     (`2026-07-08T11:40:14.999493203`).
5. ✅ **ЗАКРЫТО [ЖИВОЙ КЛЮЧ]. Деньги = СУМ, не тийины.** Проверено на реальном
   товаре: `price` ~105 тыс. — как сум логично (≈$8), как тийин был бы $0.08 (абсурд
   для крема). Арифметика сходится в сумах: `sellPrice − commission − logistic =
   sellerProfit` (проверено на реальной позиции).
   ⚠️ **Типы полей отличаются от спеки — правлены в DTO:**
   - `commission` на SKU — **double** (в живых данных ПРОЦЕНТ, напр. `19.5`), не long.
     Наш `SkuDto.Commission long` → **падал JsonException**. Исправлено на `double?`.
   - `purchasePrice`, `paidStorageAmount`, `avgdsales`, `barcode`, `article` часто
     **`null`** → JSON-поля сделаны нуллабельными (`…Raw`), наружу accessor `?? 0`.
   - В `finance/orders` реальное имя — **`sellPrice`** (не `sellerPrice` как в спеке);
     `cancelled` приходит `null` (не int64-счётчик).
6. ✅ **ЗАКРЫТО [ЖИВОЙ КЛЮЧ].** Заголовки на 200: `x-ratelimit-limit-per-day: 100000`,
   `remaining-per-day` уменьшается на 1/запрос; `replenish-rate: 2`,
   `burst-capacity: 2` (посекундный бакет ОЧЕНЬ мал — 2 запроса, восстановление 2/с).
   На 403 `-per-day` заголовков нет. **Дневной лимит = 100 000.**
7. ⏳ Глубина истории `finance/orders` — тест-магазин мал (`totalElements: 5`),
   на нём не измерить. Нужен магазин с историей.

## Формулы

**Потеряшки (Фаза 0) — БАЛАНСОВАЯ РЕКОНСИЛЯЦИЯ** (полная спека: `docs/loss-reconciliation.md`).
⚠️ **`quantityMissing` из карточки МЁРТВ:** поле deprecated в спеке и всегда
возвращает `0` — прежний расчёт был нерабочим (всегда ноль). Заменён на баланс,
валидированный на живом бета-селлере (3 SKU, сверка с физподсчётом, 4 источника сошлись).

По каждому SKU: `lost = accepted − removed − sold − defected − stock`, где:
- `accepted` — `GET /v1/invoice`, сумма `quantityAccepted` по `skuForInvoiceDtoList`,
  **только** накладные `invoiceStatus.value == "ACCEPTED"` (`CREATED` исключить);
- `removed` — `GET /v1/return`, физвывоз по **legacy-правилу**: если у родительского
  return `totalPackedAmount == 0` И `assembledDate == null` → берём `amount`; иначе `packedAmount`;
- `sold` — `quantitySold` карточки (**уже НЕТТО**, ничего не вычитать);
- `defected` — `quantityDefected` карточки; `stock` — `quantityActive` карточки.

**Матч SKU между источниками — по НОРМАЛИЗОВАННОМУ полному коду SKU (`NormalizeSkuKey`).**
⚠️ **[БАГ, исправлен — «немой ноль»]** Полный код SKU (`GALERIA-193194-11193`) лежит
в РАЗНЫХ полях:
- в **карточке товара** (`skuList`) — в **`skuFullTitle`**; `skuTitle` там **короткий
  вариант** (`11193`, `СЕРЫЙ`);
- в **накладных `/v1/invoice`** и **возвратах `/v1/return`** — в **их `skuTitle`**.

Прежний join матчил `card.skuTitle` ↔ `invoice.skuTitle` (`«11193»` vs
`«GALERIA-193194-11193»`) → **не совпадал никогда**, `accepted`/`removed` всегда `0`,
`lost` клампился в `0` — баг маскировался под честное «нет потерь». Теперь матч по
`NormalizeSkuKey(card.skuFullTitle)` ↔ `NormalizeSkuKey(invoice/return.skuTitle)`.
`NormalizeSkuKey`: trim + верхний регистр + схлопывание повторных пробелов (защита
от грязных строк Uzum). `id`/`skuId` в накладных/возвратах — внутренние, по ним НЕ матчим.

**Защита от «немого нуля»:** если по SKU было движение (`sold>0` ИЛИ `defected>0`),
но `accepted==0` — логируем warning с обоими ключами (рассинхрон join'а больше не
выглядит как честный ноль). `lost < 0` → клампим в `0` + warning с `skuId`.
⚠️ `quantityReturned` карточки = КЛИЕНТСКИЕ возвраты, в формуле НЕ участвует.

**Денежная оценка:** `lost × sellPrice` (`price`) — всегда, «к взысканию».
`lost × COGS` — COGS = **себестоимость, введённая селлером у нас**; в Фазе 0 ввода
нет → всегда 0, показываем «закуп не указан». `purchasePrice` из API **НЕ подставлять**
(грязный). **Компенсаций в API нет** (проверено на леджере `expenses`) — отчёт
добавляет строку «если уже была компенсация — вычтите вручную».

Пагинация `/v1/invoice` и `/v1/return`: голый JSON-массив (не envelope), `size=50`,
`page` с 0, до неполной страницы (у активных селлеров 70+; `totalElements` нет).

**Прибыль (Фаза 1, для архитектурного учёта):**
`sellerPrice×qtyNet − commission − logisticDeliveryFee − нашCOGS×qtyNet − доля(expenses)`,
где qtyNet = amount − amountReturns (нетто/брутто уточнить живым ключом).

## Архитектура

- **.NET 8**, PostgreSQL (Npgsql + EF Core), Redis (задел, пока не используется),
  Hangfire (Postgres-storage), Telegram.Bot (v22), ClosedXML (Excel), Serilog.
  Версии пакетов — плавающие `8.*` (кроме Telegram.Bot `22.*`, ClosedXML `0.105.*`,
  Hangfire.PostgreSql `1.21.*`).
- Проекты:
  - `SellerTools.Core` — сущности домена + модель отчёта, без зависимостей.
  - `SellerTools.UzumApi` — DTO (в т.ч. `ReconciliationDtos.cs`: invoice/return для
    баланса потерь), `IUzumApiClient` (+`GetAllInvoicesAsync`/`GetAllReturnsAsync`),
    реальный клиент (HttpClient, rate-limit заголовки, ретраи 429/5xx **и на socket
    reset**, гейт параллелизма ≤`MaxConcurrency`=3, `ProbeAsync` для diag),
    `MockUzumApiClient` (детерминированный, seed; ~25 SKU, у 6 — баланс с потерей;
    накладные+возвраты синтезируются из товаров, баланс сходится).
  - `SellerTools.App` — хост (WebApplication для дашборда), DI, EF, бот, джобы,
    Excel, шифрование, алерты, диагностика.
- Сущности: `Seller` (Id = Telegram user id, Language ru|uz),
  `Shop` (Id = Uzum shopId, ApiKeyEncrypted, KeyExpiresAt, Title),
  `SkuDailySnapshot` (**ров данных**: ежедневный срез quantityMissing/Defected/…
  по каждому SKU — API истории не отдаёт, копим сами; уникальный индекс
  ShopId+Date+SkuId), `SyncCursor` (ShopId+Kind составной ключ).
- Режим API: `UzumApi:Mode = Mock | Real` в appsettings.
- Деньги: `long`. Мок — детерминированный (фиксированный seed).

### Режимы запуска (первый аргумент)

- `bot` (по умолчанию) — WebApplication: Telegram long polling + Hangfire-сервер;
  `/health`; дашборд `/hangfire` только в Development.
- `diag <shopId> <apiKey>` — консольная диагностика: три GET-эндпоинта + проверка
  обоих base-path, печатает статусы/rate-limit/тело. Инструмент для В2.
- `smoke` — отчёт по мок-магазину + снапшоты (наследие В1).

### Production (В7)

- `Dockerfile` (multi-stage: SDK build → aspnet runtime, `InvariantGlobalization`,
  curl для healthcheck), `.dockerignore`.
- `docker-compose.prod.yml`: `app` + `postgres` + `redis` + `backup`. Секреты —
  через `.env` (см. `.env.example`): `BOT_TOKEN`, `CRYPTO_KEY_BASE64`,
  `ADMIN_CHAT_ID`, `POSTGRES_PASSWORD`, `UZUM_API_MODE`, `APP_IMAGE`. Порт Postgres
  наружу НЕ публикуется. У всех сервисов healthcheck, `restart: unless-stopped`.
  `app.image=${APP_IMAGE:-sellertools-app}` — на VPS тянется из GHCR, локально
  фолбэк на сборку из Dockerfile.
- CI/CD: `.github/workflows/deploy.yml` — на push в `main`: `dotnet build+test`
  (гейт, падает до образа) → build+push образа в GHCR → SSH на VPS
  `docker compose pull && up -d`. Секреты GitHub: `HOST`, `USER`, `SSH_KEY`
  (GHCR-логин на VPS — встроенным `GITHUB_TOKEN`). Первый деплой — руками по DEPLOY.md.
- Бэкап: `deploy/backup.sh` (контейнер `backup` на образе postgres) — `pg_dump`
  раз в сутки в `./backups`, ротация 14 дней. Снапшоты — главный актив.
- Алерты: `IAdminAlertService` шлёт админу (`Admin:ChatId`) в Telegram при сбое
  синка магазина, необработанном исключении, остатке дневного лимита <10%.
  Троттлинг раз в 10 мин на тип. Свой `TelegramBotClient`, ошибки отправки глушит.
- Логи: Serilog (секция `Serilog` в appsettings) — консоль + файл `logs/` с
  ротацией по дням (14 файлов). `appsettings.Production.json` — prod-уровни.
- ⚠️ **Грабли Postgres:** `POSTGRES_PASSWORD` применяется только при ПЕРВОЙ
  инициализации пустого тома. Смена пароля в `.env` на существующем томе →
  `password authentication failed`. Лечится `down -v` (стирает данные!) либо
  `ALTER USER … PASSWORD`. Описано в DEPLOY.md.
- Деплой на VPS — по `DEPLOY.md` (клон по SSH: репо приватный, GitHub не
  принимает пароль по HTTPS).
- ⚠️ **Грабли Contabo/сеть (ночь отладки):** Contabo теряет IPv6-маршрут через
  часы аптайма; .NET шёл по AAAA/IPv6 → `network unreachable`, а `curl` молча
  фолбэчился на IPv4 (диагностика curl'ом обманывала). Фиксы: в compose
  `DOTNET_SYSTEM_NET_DISABLEIPV6=1` (только IPv4); в клиенте
  `SocketsHttpHandler.PooledConnectionLifetime=60с` (край Uzum рвёт keep-alive,
  .NET иначе переиспользует мёртвый сокет → `HttpRequestException`). Плюс защитный
  `/etc/docker/daemon.json` с DNS 1.1.1.1/8.8.8.8 (ставится руками, см. DEPLOY.md).
  **Правило диагностики:** curl из контейнера работает, а приложение падает —
  проверять IPv6 и пул соединений, а не «сеть вообще».

## Роадмап по вечерам

- **В1 (СДЕЛАНО):** solution из 3 проектов, docker-compose (postgres:16,
  redis:7), EF-модели + EnsureCreated, DTO, IUzumApiClient + реальный клиент +
  мок, сервис расчёта потеряшек, smoke-тест как IHostedService.
- **В2 (ЧАСТИЧНО / ОТКРЫТ):** авторизация и shopId закрыты спекой; форматы дат
  разобраны. **Осталось живым read-only ключом:** единица денег (сум/тийин),
  доступ к `finance/*`, реальные значения лимитов, глубина истории, канонический
  base-path (см. чек-лист «Неизвестное»). Инструмент проверки — режим `diag`.
- **В3 (СДЕЛАНО):** Telegram-бот (long polling): /start, выбор языка RU/UZ, приём
  ключа (сообщение сразу удаляется), AES-шифрование ключа, привязка Shop.
  Авто-определение магазина по ключу через `GET /v1/shops` (shopId не спрашиваем).
- **В4 (СДЕЛАНО):** Hangfire (хранилище в Postgres) + ночной синк снапшотов 03:00
  Asia/Tashkent, изоляция ошибок по магазину, rate-limit пауза; кнопка «Отчёт».
  Дашборд `/hangfire` — только в Development.
- **В5 (СДЕЛАНО):** карточный отчёт в боте + Excel-экспорт (ClosedXML): «Потеряшки
  в Excel» (числа числовыми ячейками, итоги формулой SUM) и «Товары + ИКПУ» (для
  ЭТТН). Кнопка «Обновить данные» с троттлингом 30 мин. UZ доведён до 100% (черновой).
- **[ПОТЕРЯШКИ ПЕРЕПИСАНЫ, после В5]:** расчёт по мёртвому `quantityMissing`
  заменён на балансовую реконсиляцию (`docs/loss-reconciliation.md`). `LossReport`
  теперь несёт `Lost` + компоненты баланса (accepted/removed/sold/defected/stock);
  `LossReportService.BuildAsync` тянет товары+`/v1/invoice`+`/v1/return` и считает
  на лету; `BuildFromSnapshotAsync` удалён (в снапшоте нет accepted/removed). Отчёт
  и Excel-«Потеряшки» строятся из свежих API-данных. Excel-лист потеряшек: колонки
  баланса + «Потеряно» + сумма по продаже (колонки закупа больше нет). Loc: ключи
  `report.*`/`xlsx.*` обновлены (RU+UZ 100%), сноска про компенсацию.
- **В6 (НЕ НАЧАТ):** беты на живых селлерах, фиксы.
- **В7 (СДЕЛАНО):** production-деплой. Multi-stage `Dockerfile`,
  `docker-compose.prod.yml` (app+postgres+redis+backup, секреты через `.env`,
  restart:unless-stopped, healthchecks). Бэкап Postgres `pg_dump` раз в сутки в
  `./backups`, ротация 14 дней (`deploy/backup.sh`). Админ-алерты в Telegram
  (сбой синка / необработанное исключение / лимит <10%, троттл 10 мин/тип).
  Serilog в файл с ротацией. Health `/health`. `DEPLOY.md` — пошагово от чистого
  Ubuntu VPS (клон по SSH — приватный репо, HTTPS-пароль GitHub не принимает).

## Правила работы

- Не выдумывать эндпоинты и поля сверх задокументированных здесь — если чего-то
  не хватает, пометить TODO и спросить.
- Секреты (ключи селлеров, токен бота) — только через конфиг/env, не в коде,
  не в логах, не в git.
- Комментарии и коммиты — по-русски, кратко.
- После каждого вечера — git commit **И обновление CLAUDE.md**: блок «Текущее
  состояние» вверху + пометка вечера в Роадмапе + новые артефакты/решения в
  Архитектуре. Контекст проекта живёт в CLAUDE.md и git-истории, а не в сессии:
  `git clone` + Claude Code в папке проекта на любой машине должны дать полное
  понимание состояния. Если чего-то нет в CLAUDE.md — это дефект, чинить сразу.
