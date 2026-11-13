-- ============================================================================
-- FixHub: тестовые данные
--
-- Для ПУСТОЙ базы после применения миграций (dotnet ef database update).
-- Если в таблице "Services" уже есть строки, скрипт остановится и ничего не изменит.
--
-- Учётные записи (пароль у всех клиентов и мастеров: Test12345):
--   admin@fixhub.local      Admin12345  администратор
--   client1@fixhub.test     клиент
--   client2@fixhub.test     клиент
--   blocked@fixhub.test     заблокированный клиент
--   master1@fixhub.test     исполнитель (ремонт техники, сантехника)
--   master2@fixhub.test     исполнитель (клининг, репетиторство)
--   master3@fixhub.test     исполнитель (фотография, компьютерная помощь)
--
-- Запуск через Docker:
--   docker exec -i fixhub-db psql -U fixhub -d fixhub -v ON_ERROR_STOP=1 < db\seed.sql
-- Запуск в DBeaver: открыть файл в SQL-редакторе подключения fixhub и выполнить
--   как скрипт (Alt+X).
-- ============================================================================

SET client_encoding TO 'UTF8';

BEGIN;

-- Защита от повторного запуска
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM "Services") THEN
        RAISE EXCEPTION 'В таблице Services уже есть данные: скрипт предназначен для пустой базы';
    END IF;
END $$;

-- ---------------------------------------------------------------------------
-- Пользователи (хеши паролей в формате ASP.NET Core Identity)
-- ---------------------------------------------------------------------------
INSERT INTO "Users" ("Email", "PasswordHash", "FullName", "Phone", "Bio", "Role", "IsBlocked", "CreatedAt")
VALUES
    ('admin@fixhub.local',
     'AQAAAAIAAYagAAAAELAxG1z01SV0SSsrU80WN6PNyHbekftiPvfK9OZOmJZJhTx2jh7RrONDhzV7gkf13g==',
     'Администратор', NULL, NULL, 'Admin', FALSE, NOW() - INTERVAL '30 days'),

    ('client1@fixhub.test',
     'AQAAAAIAAYagAAAAEAIJzohEw/5wlEopxPK+pkThd9d+YXhc4KRSQrQFZmAFGArbDFapYMnHThNpSuBVMQ==',
     'Анна Климова', '+375291110001', NULL, 'Client', FALSE, NOW() - INTERVAL '25 days'),

    ('client2@fixhub.test',
     'AQAAAAIAAYagAAAAEAIJzohEw/5wlEopxPK+pkThd9d+YXhc4KRSQrQFZmAFGArbDFapYMnHThNpSuBVMQ==',
     'Павел Орлов', '+375291110002', NULL, 'Client', FALSE, NOW() - INTERVAL '20 days'),

    ('blocked@fixhub.test',
     'AQAAAAIAAYagAAAAEAIJzohEw/5wlEopxPK+pkThd9d+YXhc4KRSQrQFZmAFGArbDFapYMnHThNpSuBVMQ==',
     'Заблокированный Клиент', '+375291110003', NULL, 'Client', TRUE, NOW() - INTERVAL '15 days'),

    ('master1@fixhub.test',
     'AQAAAAIAAYagAAAAEAIJzohEw/5wlEopxPK+pkThd9d+YXhc4KRSQrQFZmAFGArbDFapYMnHThNpSuBVMQ==',
     'Иван Мастеров', '+375292220001',
     'Ремонт бытовой техники и сантехника, опыт 10 лет', 'Performer', FALSE, NOW() - INTERVAL '40 days'),

    ('master2@fixhub.test',
     'AQAAAAIAAYagAAAAEAIJzohEw/5wlEopxPK+pkThd9d+YXhc4KRSQrQFZmAFGArbDFapYMnHThNpSuBVMQ==',
     'Елена Чистова', '+375292220002',
     'Клининг и занятия по математике', 'Performer', FALSE, NOW() - INTERVAL '38 days'),

    ('master3@fixhub.test',
     'AQAAAAIAAYagAAAAEAIJzohEw/5wlEopxPK+pkThd9d+YXhc4KRSQrQFZmAFGArbDFapYMnHThNpSuBVMQ==',
     'Олег Кадров', '+375292220003',
     'Фотограф и специалист по настройке компьютеров', 'Performer', FALSE, NOW() - INTERVAL '35 days')
ON CONFLICT ("Email") DO NOTHING;

-- ---------------------------------------------------------------------------
-- Категории (корневые и вложенные)
-- ---------------------------------------------------------------------------
INSERT INTO "Categories" ("Name", "ParentId")
SELECT v.name, NULL
FROM (VALUES ('Ремонт техники'), ('Клининг'), ('Сантехника'), ('Репетиторство'), ('Фотография')) AS v(name)
WHERE NOT EXISTS (SELECT 1 FROM "Categories" c WHERE c."Name" = v.name);

INSERT INTO "Categories" ("Name", "ParentId")
SELECT 'Компьютерная помощь', (SELECT "Id" FROM "Categories" WHERE "Name" = 'Ремонт техники' AND "ParentId" IS NULL)
WHERE NOT EXISTS (SELECT 1 FROM "Categories" WHERE "Name" = 'Компьютерная помощь');

INSERT INTO "Categories" ("Name", "ParentId")
SELECT 'Уборка после ремонта', (SELECT "Id" FROM "Categories" WHERE "Name" = 'Клининг' AND "ParentId" IS NULL)
WHERE NOT EXISTS (SELECT 1 FROM "Categories" WHERE "Name" = 'Уборка после ремонта');

-- ---------------------------------------------------------------------------
-- Услуги (одна скрыта: "Химчистка мебели")
-- ---------------------------------------------------------------------------
INSERT INTO "Services" ("PerformerId", "CategoryId", "Title", "Description", "PhotoUrl", "Price", "Unit", "IsActive", "CreatedAt")
SELECT u."Id", c."Id", v.title, v.descr, NULL, v.price, v.unit, v.active, NOW() - INTERVAL '20 days'
FROM (VALUES
    ('master1@fixhub.test', 'Ремонт техники',       'Ремонт стиральных машин',  'Диагностика и ремонт на дому, гарантия 6 месяцев',          45.00, 'Hour',  TRUE),
    ('master1@fixhub.test', 'Сантехника',           'Замена смесителя',         'Установка и подключение смесителя, материалы клиента',       60.00, 'Piece', TRUE),
    ('master1@fixhub.test', 'Сантехника',           'Устранение засора',        'Прочистка раковины, ванны или унитаза',                     35.00, 'Piece', TRUE),
    ('master2@fixhub.test', 'Клининг',              'Генеральная уборка квартиры', 'Полная уборка с мытьём окон и кухни',                    25.00, 'Hour',  TRUE),
    ('master2@fixhub.test', 'Клининг',              'Мытьё окон',               'Мытьё окон с обеих сторон',                                  8.00, 'Piece', TRUE),
    ('master2@fixhub.test', 'Уборка после ремонта', 'Уборка после ремонта',     'Вывоз строительной пыли и мытьё всех поверхностей',         30.00, 'Hour',  TRUE),
    ('master2@fixhub.test', 'Репетиторство',        'Репетитор по математике',  'Подготовка к ЦТ, 5-11 класс',                               20.00, 'Hour',  TRUE),
    ('master2@fixhub.test', 'Клининг',              'Химчистка мебели',         'Сейчас не принимает заказы (услуга скрыта)',                70.00, 'Piece', FALSE),
    ('master3@fixhub.test', 'Фотография',           'Семейная фотосессия',      'Съёмка на улице или дома, 30 обработанных фотографий',      80.00, 'Hour',  TRUE),
    ('master3@fixhub.test', 'Компьютерная помощь',  'Настройка компьютера',     'Установка системы, программ и антивируса',                  30.00, 'Hour',  TRUE)
) AS v(performer, category, title, descr, price, unit, active)
JOIN "Users" u      ON u."Email" = v.performer
JOIN "Categories" c ON c."Name"  = v.category;

-- ---------------------------------------------------------------------------
-- Корзина клиента 1
-- ---------------------------------------------------------------------------
INSERT INTO "CartItems" ("ClientId", "ServiceId", "Quantity", "DesiredDate", "Comment")
SELECT cl."Id", s."Id", v.qty, CURRENT_DATE + v.days, v.comment
FROM (VALUES
    ('Семейная фотосессия', 1, 7,  'Лучше в выходной день'),
    ('Мытьё окон',          6, 10, NULL)
) AS v(title, qty, days, comment)
JOIN "Services" s ON s."Title" = v.title
JOIN "Users" cl   ON cl."Email" = 'client1@fixhub.test';

-- ---------------------------------------------------------------------------
-- Заказы в разных статусах, с позициями и историей статусов
-- ---------------------------------------------------------------------------
DO $$
DECLARE
    c1 int; c2 int;
    m1 int; m2 int; m3 int;
    v_order int;
BEGIN
    SELECT "Id" INTO c1 FROM "Users" WHERE "Email" = 'client1@fixhub.test';
    SELECT "Id" INTO c2 FROM "Users" WHERE "Email" = 'client2@fixhub.test';
    SELECT "Id" INTO m1 FROM "Users" WHERE "Email" = 'master1@fixhub.test';
    SELECT "Id" INTO m2 FROM "Users" WHERE "Email" = 'master2@fixhub.test';
    SELECT "Id" INTO m3 FROM "Users" WHERE "Email" = 'master3@fixhub.test';

    -- Заказ 1: выполнен (клиент 1, мастер 1)
    INSERT INTO "Orders" ("ClientId", "PerformerId", "Status", "TotalAmount", "Address", "ContactName", "ContactPhone", "CreatedAt")
    VALUES (c1, m1, 'Completed', 0, 'Минск, ул. Тестовая, 1, кв. 5', 'Анна Климова', '+375291110001', NOW() - INTERVAL '14 days')
    RETURNING "Id" INTO v_order;

    INSERT INTO "OrderItems" ("OrderId", "ServiceId", "ServiceTitle", "UnitPrice", "Quantity", "DesiredDate", "Comment")
    SELECT v_order, "Id", "Title", "Price", 2, CURRENT_DATE - 12, 'Позвоните за час'
    FROM "Services" WHERE "Title" = 'Ремонт стиральных машин';
    INSERT INTO "OrderItems" ("OrderId", "ServiceId", "ServiceTitle", "UnitPrice", "Quantity", "DesiredDate", "Comment")
    SELECT v_order, "Id", "Title", "Price", 1, CURRENT_DATE - 12, NULL
    FROM "Services" WHERE "Title" = 'Замена смесителя';

    UPDATE "Orders" SET "TotalAmount" = (SELECT SUM("UnitPrice" * "Quantity") FROM "OrderItems" WHERE "OrderId" = v_order)
    WHERE "Id" = v_order;

    INSERT INTO "OrderStatusHistory" ("OrderId", "FromStatus", "ToStatus", "ChangedByUserId", "ChangedAt") VALUES
        (v_order, NULL,         'New',        c1, NOW() - INTERVAL '14 days'),
        (v_order, 'New',        'Confirmed',  m1, NOW() - INTERVAL '13 days'),
        (v_order, 'Confirmed',  'InProgress', m1, NOW() - INTERVAL '12 days'),
        (v_order, 'InProgress', 'Completed',  m1, NOW() - INTERVAL '12 days' + INTERVAL '4 hours');

    -- Заказ 2: в работе (клиент 1, мастер 2)
    INSERT INTO "Orders" ("ClientId", "PerformerId", "Status", "TotalAmount", "Address", "ContactName", "ContactPhone", "CreatedAt")
    VALUES (c1, m2, 'InProgress', 0, 'Минск, ул. Тестовая, 1, кв. 5', 'Анна Климова', '+375291110001', NOW() - INTERVAL '5 days')
    RETURNING "Id" INTO v_order;

    INSERT INTO "OrderItems" ("OrderId", "ServiceId", "ServiceTitle", "UnitPrice", "Quantity", "DesiredDate", "Comment")
    SELECT v_order, "Id", "Title", "Price", 3, CURRENT_DATE - 1, NULL
    FROM "Services" WHERE "Title" = 'Генеральная уборка квартиры';

    UPDATE "Orders" SET "TotalAmount" = (SELECT SUM("UnitPrice" * "Quantity") FROM "OrderItems" WHERE "OrderId" = v_order)
    WHERE "Id" = v_order;

    INSERT INTO "OrderStatusHistory" ("OrderId", "FromStatus", "ToStatus", "ChangedByUserId", "ChangedAt") VALUES
        (v_order, NULL,        'New',        c1, NOW() - INTERVAL '5 days'),
        (v_order, 'New',       'Confirmed',  m2, NOW() - INTERVAL '4 days'),
        (v_order, 'Confirmed', 'InProgress', m2, NOW() - INTERVAL '1 day');

    -- Заказ 3: новый (клиент 2, мастер 1)
    INSERT INTO "Orders" ("ClientId", "PerformerId", "Status", "TotalAmount", "Address", "ContactName", "ContactPhone", "CreatedAt")
    VALUES (c2, m1, 'New', 0, 'Минск, пр. Независимости, 10, кв. 12', 'Павел Орлов', '+375291110002', NOW() - INTERVAL '1 day')
    RETURNING "Id" INTO v_order;

    INSERT INTO "OrderItems" ("OrderId", "ServiceId", "ServiceTitle", "UnitPrice", "Quantity", "DesiredDate", "Comment")
    SELECT v_order, "Id", "Title", "Price", 1, CURRENT_DATE + 2, 'Засор на кухне'
    FROM "Services" WHERE "Title" = 'Устранение засора';

    UPDATE "Orders" SET "TotalAmount" = (SELECT SUM("UnitPrice" * "Quantity") FROM "OrderItems" WHERE "OrderId" = v_order)
    WHERE "Id" = v_order;

    INSERT INTO "OrderStatusHistory" ("OrderId", "FromStatus", "ToStatus", "ChangedByUserId", "ChangedAt") VALUES
        (v_order, NULL, 'New', c2, NOW() - INTERVAL '1 day');

    -- Заказ 4: отменён клиентом (клиент 2, мастер 3)
    INSERT INTO "Orders" ("ClientId", "PerformerId", "Status", "TotalAmount", "Address", "ContactName", "ContactPhone", "CreatedAt")
    VALUES (c2, m3, 'Cancelled', 0, 'Минск, пр. Независимости, 10, кв. 12', 'Павел Орлов', '+375291110002', NOW() - INTERVAL '7 days')
    RETURNING "Id" INTO v_order;

    INSERT INTO "OrderItems" ("OrderId", "ServiceId", "ServiceTitle", "UnitPrice", "Quantity", "DesiredDate", "Comment")
    SELECT v_order, "Id", "Title", "Price", 2, CURRENT_DATE + 3, NULL
    FROM "Services" WHERE "Title" = 'Семейная фотосессия';

    UPDATE "Orders" SET "TotalAmount" = (SELECT SUM("UnitPrice" * "Quantity") FROM "OrderItems" WHERE "OrderId" = v_order)
    WHERE "Id" = v_order;

    INSERT INTO "OrderStatusHistory" ("OrderId", "FromStatus", "ToStatus", "ChangedByUserId", "ChangedAt") VALUES
        (v_order, NULL,  'New',       c2, NOW() - INTERVAL '7 days'),
        (v_order, 'New', 'Cancelled', c2, NOW() - INTERVAL '6 days');

    -- Заказ 5: подтверждён (клиент 2, мастер 2)
    INSERT INTO "Orders" ("ClientId", "PerformerId", "Status", "TotalAmount", "Address", "ContactName", "ContactPhone", "CreatedAt")
    VALUES (c2, m2, 'Confirmed', 0, 'Минск, пр. Независимости, 10, кв. 12', 'Павел Орлов', '+375291110002', NOW() - INTERVAL '2 days')
    RETURNING "Id" INTO v_order;

    INSERT INTO "OrderItems" ("OrderId", "ServiceId", "ServiceTitle", "UnitPrice", "Quantity", "DesiredDate", "Comment")
    SELECT v_order, "Id", "Title", "Price", 4, CURRENT_DATE + 4, 'Подготовка к ЦТ'
    FROM "Services" WHERE "Title" = 'Репетитор по математике';

    UPDATE "Orders" SET "TotalAmount" = (SELECT SUM("UnitPrice" * "Quantity") FROM "OrderItems" WHERE "OrderId" = v_order)
    WHERE "Id" = v_order;

    INSERT INTO "OrderStatusHistory" ("OrderId", "FromStatus", "ToStatus", "ChangedByUserId", "ChangedAt") VALUES
        (v_order, NULL,  'New',       c2, NOW() - INTERVAL '2 days'),
        (v_order, 'New', 'Confirmed', m2, NOW() - INTERVAL '1 day');
END $$;
-- ---------------------------------------------------------------------------
-- Ещё выполненные заказы (для отзывов)
-- ---------------------------------------------------------------------------
DO $$
DECLARE
    c1 int; c2 int;
    m1 int; m2 int; m3 int;
    v_order int;
BEGIN
    SELECT "Id" INTO c1 FROM "Users" WHERE "Email" = 'client1@fixhub.test';
    SELECT "Id" INTO c2 FROM "Users" WHERE "Email" = 'client2@fixhub.test';
    SELECT "Id" INTO m1 FROM "Users" WHERE "Email" = 'master1@fixhub.test';
    SELECT "Id" INTO m2 FROM "Users" WHERE "Email" = 'master2@fixhub.test';
    SELECT "Id" INTO m3 FROM "Users" WHERE "Email" = 'master3@fixhub.test';

    -- Заказ 6: выполнен (клиент 2, мастер 3)
    INSERT INTO "Orders" ("ClientId", "PerformerId", "Status", "TotalAmount", "Address", "ContactName", "ContactPhone", "CreatedAt")
    VALUES (c2, m3, 'Completed', 0, 'Минск, пр. Независимости, 10, кв. 12', 'Павел Орлов', '+375291110002', NOW() - INTERVAL '12 days')
    RETURNING "Id" INTO v_order;

    INSERT INTO "OrderItems" ("OrderId", "ServiceId", "ServiceTitle", "UnitPrice", "Quantity", "DesiredDate", "Comment")
    SELECT v_order, "Id", "Title", "Price", 1, CURRENT_DATE - 10, NULL
    FROM "Services" WHERE "Title" = 'Семейная фотосессия';
    INSERT INTO "OrderItems" ("OrderId", "ServiceId", "ServiceTitle", "UnitPrice", "Quantity", "DesiredDate", "Comment")
    SELECT v_order, "Id", "Title", "Price", 1, CURRENT_DATE - 10, NULL
    FROM "Services" WHERE "Title" = 'Настройка компьютера';

    UPDATE "Orders" SET "TotalAmount" = (SELECT SUM("UnitPrice" * "Quantity") FROM "OrderItems" WHERE "OrderId" = v_order)
    WHERE "Id" = v_order;

    INSERT INTO "OrderStatusHistory" ("OrderId", "FromStatus", "ToStatus", "ChangedByUserId", "ChangedAt") VALUES
        (v_order, NULL,         'New',        c2, NOW() - INTERVAL '12 days'),
        (v_order, 'New',        'Confirmed',  m3, NOW() - INTERVAL '11 days'),
        (v_order, 'Confirmed',  'InProgress', m3, NOW() - INTERVAL '10 days'),
        (v_order, 'InProgress', 'Completed',  m3, NOW() - INTERVAL '10 days' + INTERVAL '3 hours');

    -- Заказ 7: выполнен (клиент 1, мастер 2)
    INSERT INTO "Orders" ("ClientId", "PerformerId", "Status", "TotalAmount", "Address", "ContactName", "ContactPhone", "CreatedAt")
    VALUES (c1, m2, 'Completed', 0, 'Минск, ул. Тестовая, 1, кв. 5', 'Анна Климова', '+375291110001', NOW() - INTERVAL '10 days')
    RETURNING "Id" INTO v_order;

    INSERT INTO "OrderItems" ("OrderId", "ServiceId", "ServiceTitle", "UnitPrice", "Quantity", "DesiredDate", "Comment")
    SELECT v_order, "Id", "Title", "Price", 3, CURRENT_DATE - 8, NULL
    FROM "Services" WHERE "Title" = 'Мытьё окон';

    UPDATE "Orders" SET "TotalAmount" = (SELECT SUM("UnitPrice" * "Quantity") FROM "OrderItems" WHERE "OrderId" = v_order)
    WHERE "Id" = v_order;

    INSERT INTO "OrderStatusHistory" ("OrderId", "FromStatus", "ToStatus", "ChangedByUserId", "ChangedAt") VALUES
        (v_order, NULL,         'New',        c1, NOW() - INTERVAL '10 days'),
        (v_order, 'New',        'Confirmed',  m2, NOW() - INTERVAL '9 days'),
        (v_order, 'Confirmed',  'InProgress', m2, NOW() - INTERVAL '8 days'),
        (v_order, 'InProgress', 'Completed',  m2, NOW() - INTERVAL '8 days' + INTERVAL '2 hours');

    -- Заказ 8: выполнен (клиент 2, мастер 1)
    INSERT INTO "Orders" ("ClientId", "PerformerId", "Status", "TotalAmount", "Address", "ContactName", "ContactPhone", "CreatedAt")
    VALUES (c2, m1, 'Completed', 0, 'Минск, пр. Независимости, 10, кв. 12', 'Павел Орлов', '+375291110002', NOW() - INTERVAL '9 days')
    RETURNING "Id" INTO v_order;

    INSERT INTO "OrderItems" ("OrderId", "ServiceId", "ServiceTitle", "UnitPrice", "Quantity", "DesiredDate", "Comment")
    SELECT v_order, "Id", "Title", "Price", 1, CURRENT_DATE - 7, NULL
    FROM "Services" WHERE "Title" = 'Ремонт стиральных машин';

    UPDATE "Orders" SET "TotalAmount" = (SELECT SUM("UnitPrice" * "Quantity") FROM "OrderItems" WHERE "OrderId" = v_order)
    WHERE "Id" = v_order;

    INSERT INTO "OrderStatusHistory" ("OrderId", "FromStatus", "ToStatus", "ChangedByUserId", "ChangedAt") VALUES
        (v_order, NULL,         'New',        c2, NOW() - INTERVAL '9 days'),
        (v_order, 'New',        'Confirmed',  m1, NOW() - INTERVAL '8 days'),
        (v_order, 'Confirmed',  'InProgress', m1, NOW() - INTERVAL '7 days'),
        (v_order, 'InProgress', 'Completed',  m1, NOW() - INTERVAL '7 days' + INTERVAL '2 hours');
END $$;

-- ---------------------------------------------------------------------------
-- Отзывы к выполненным заказам (один отзыв скрыт администратором как спам)
-- ---------------------------------------------------------------------------
INSERT INTO "Reviews" ("OrderItemId", "ClientId", "ServiceId", "PerformerId", "Rating", "Text", "IsHidden", "CreatedAt")
SELECT oi."Id", o."ClientId", oi."ServiceId", o."PerformerId", v.rating, v.txt, v.hidden, v.created
FROM (VALUES
    ('client1@fixhub.test', 'Ремонт стиральных машин', 5, 'Приехал вовремя, починил за час, всё объяснил.',              FALSE, NOW() - INTERVAL '11 days'),
    ('client1@fixhub.test', 'Замена смесителя',        4, 'Сделал аккуратно, но пришлось немного подождать.',            FALSE, NOW() - INTERVAL '11 days'),
    ('client2@fixhub.test', 'Ремонт стиральных машин', 4, 'Быстро нашёл причину поломки, машина работает как новая.',     FALSE, NOW() - INTERVAL '6 days'),
    ('client2@fixhub.test', 'Семейная фотосессия',     5, 'Отличные кадры, дети были в восторге.',                       FALSE, NOW() - INTERVAL '9 days'),
    ('client2@fixhub.test', 'Настройка компьютера',    2, 'Долго возился, пришлось звонить повторно.',                    FALSE, NOW() - INTERVAL '9 days'),
    ('client1@fixhub.test', 'Мытьё окон',              1, 'Купите дёшево, переходите по ссылке!!!',                       TRUE,  NOW() - INTERVAL '7 days')
) AS v(client, title, rating, txt, hidden, created)
JOIN "Users" cl      ON cl."Email" = v.client
JOIN "Orders" o      ON o."ClientId" = cl."Id" AND o."Status" = 'Completed'
JOIN "OrderItems" oi ON oi."OrderId" = o."Id" AND oi."ServiceTitle" = v.title;

COMMIT;

-- ---------------------------------------------------------------------------
-- Проверка: сколько строк в каждой таблице
-- ---------------------------------------------------------------------------
SELECT 'Users' AS "Таблица", COUNT(*) AS "Строк" FROM "Users"
UNION ALL SELECT 'Categories',         COUNT(*) FROM "Categories"
UNION ALL SELECT 'Services',           COUNT(*) FROM "Services"
UNION ALL SELECT 'CartItems',          COUNT(*) FROM "CartItems"
UNION ALL SELECT 'Orders',             COUNT(*) FROM "Orders"
UNION ALL SELECT 'OrderItems',         COUNT(*) FROM "OrderItems"
UNION ALL SELECT 'OrderStatusHistory', COUNT(*) FROM "OrderStatusHistory"
UNION ALL SELECT 'Reviews',            COUNT(*) FROM "Reviews";
