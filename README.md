# FixHub: backend

Серверная часть платформы FixHub для поиска и заказа услуг местных мастеров (ремонт техники, клининг, сантехника, репетиторство, фотография). Реализована как REST API на ASP.NET Core.

Фронтенд находится в отдельном репозитории: fixhub-fe.

Технологии
C#, ASP.NET Core Web API (.NET 9)
Entity Framework Core 9 + Npgsql
PostgreSQL 16 (в Docker)
Аутентификация: JWT, пароли хранятся в виде хеша (PasswordHasher)
Документация API: Swagger (Swashbuckle)
Контроль версий: Git
Требования
.NET SDK 9
Docker Desktop
Инструмент миграций EF Core: dotnet tool install --global dotnet-ef
Запуск
# 1. База данных

Создай файл docker-compose.yml (например, в папке выше репозитория) и запусти PostgreSQL:

yaml
services:
  db:
    image: postgres:16
    container_name: fixhub-db
    environment:
      POSTGRES_USER: fixhub
      POSTGRES_PASSWORD: fixhub123
      POSTGRES_DB: fixhub
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data

volumes:
  pgdata:
docker compose up -d
docker ps

В списке должен быть контейнер fixhub-db со статусом Up. Данные хранятся в volume и сохраняются после перезапуска.

# 2. Настройки

Настройки лежат в FixHub.Api/appsettings.json:

Раздел	Назначение
ConnectionStrings:Default	строка подключения к PostgreSQL
Jwt	ключ подписи, издатель, аудитория, срок жизни токена (минуты)
Admin	email и пароль администратора, создаваемого при первом запуске

Значения в репозитории предназначены только для разработки. Для реального развёртывания ключ JWT и пароль администратора нужно хранить вне репозитория (переменные окружения или user-secrets).

# 3. Миграции и запуск
cd FixHub.Api
dotnet ef database update
dotnet run

Миграции автоматически при старте не применяются, их нужно выполнить командой выше. Приложение слушает адреса https://localhost:7173 и http://localhost:5239.

# Swagger: https://localhost:7173/swagger

При первом запуске создаётся администратор с данными из раздела Admin.

Роли
Роль	Возможности
Гость	просмотр категорий, регистрация, вход
Client	профиль, просмотр данных о себе
Performer	создание, изменение и скрытие собственных услуг
Admin	управление категориями

Регистрация доступна для ролей Client и Performer. Администратор создаётся только автоматически.

# API

Для защищённых методов нужен заголовок Authorization: Bearer <token>. В Swagger токен вставляется (без слова Bearer и без кавычек) через кнопку Authorize.

Аутентификация
Метод	Адрес	Доступ
POST	/api/auth/register	все
POST	/api/auth/login	все
GET	/api/auth/me	авторизованные
Категории
Метод	Адрес	Доступ
GET	/api/categories	все
POST	/api/categories	Admin
PUT	/api/categories/{id}	Admin
DELETE	/api/categories/{id}	Admin

Категорию нельзя удалить, если в ней есть подкатегории или услуги.

Услуги исполнителя
Метод	Адрес	Доступ
GET	/api/performer/services	Performer
POST	/api/performer/services	Performer
PUT	/api/performer/services/{id}	Performer
PATCH	/api/performer/services/{id}/hide	Performer
PATCH	/api/performer/services/{id}/show	Performer

Исполнитель видит и меняет только свои услуги: при обращении к чужой услуге API возвращает 404.

Коды ответов
Код	Значение
400	неверные данные запроса
401	нет токена, токен неверный или неверный логин/пароль
403	недостаточно прав или пользователь заблокирован
404	объект не найден
409	конфликт (например, email уже занят)
Структура проекта
FixHub.Api/
  Controllers/   контроллеры API
  Data/          AppDbContext
  DTOs/          объекты запросов и ответов
  Migrations/    миграции EF Core
  Models/        сущности и перечисления
  Services/      сервисы (выдача JWT)
  Program.cs     настройка приложения
База данных

Сущности: User, Category (с вложенностью), Service, CartItem, Order, OrderItem, OrderStatusHistory.

Денежные значения хранятся в типе numeric (decimal), время в UTC.
Уникальный email пользователя; в корзине уникальная пара (клиент, услуга).
Индексы по категории и цене услуг, по клиенту и статусу заказов.
Ограничения: цена не меньше нуля, количество больше нуля.
Роли и статусы хранятся в виде текста.
Статус разработки

# Готово:

регистрация, вход, JWT, разграничение прав по ролям;
создание администратора при старте;
категории (управление администратором);
услуги исполнителя (создание, изменение, скрытие и показ).
публичный каталог: поиск, фильтры по категории и цене, сортировка, постраничный вывод;
корзина;
оформление заказа в одной транзакции, статусы заказа и их история;
кабинеты клиента, исполнителя и администратора;
unit - тесты
# В планах:

отзывы, уведомления и статистика (дополнительно);
интеграционные тесты, ER-диаграмма, SQL-скрипт с тестовыми данными.
