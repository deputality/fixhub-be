# ER-диаграмма FixHub

Диаграмма описывает таблицы базы данных PostgreSQL. Она отображается прямо на GitHub; для просмотра в другом редакторе нужна поддержка Mermaid.

```mermaid
erDiagram
    Users ||--o{ Services : "исполнитель"
    Categories ||--o{ Services : "категория"
    Categories ||--o{ Categories : "родитель"
    Users ||--o{ CartItems : "клиент"
    Services ||--o{ CartItems : "услуга"
    Users ||--o{ Orders : "клиент"
    Users ||--o{ Orders : "исполнитель"
    Orders ||--|{ OrderItems : "позиции"
    Services ||--o{ OrderItems : "услуга"
    Orders ||--|{ OrderStatusHistory : "история"
    Users ||--o{ OrderStatusHistory : "изменил"

    Users {
        int Id PK
        string Email UK
        string PasswordHash
        string FullName
        string Phone
        string Bio
        string Role "Client, Performer, Admin"
        boolean IsBlocked
        datetime CreatedAt
    }

    Categories {
        int Id PK
        string Name
        int ParentId FK "вложенность, может быть пустым"
    }

    Services {
        int Id PK
        int PerformerId FK
        int CategoryId FK
        string Title
        string Description
        string PhotoUrl
        decimal Price "numeric(10,2), не меньше 0"
        string Unit "Hour или Piece"
        boolean IsActive "скрытая услуга не видна в каталоге"
        datetime CreatedAt
    }

    CartItems {
        int Id PK
        int ClientId FK
        int ServiceId FK
        int Quantity "больше 0"
        date DesiredDate
        string Comment
    }

    Orders {
        int Id PK
        int ClientId FK
        int PerformerId FK
        string Status "New, Confirmed, InProgress, Completed, Cancelled"
        decimal TotalAmount "numeric(12,2)"
        string Address
        string ContactName
        string ContactPhone
        datetime CreatedAt
    }

    OrderItems {
        int Id PK
        int OrderId FK
        int ServiceId FK
        string ServiceTitle "название на момент заказа"
        decimal UnitPrice "цена на момент заказа"
        int Quantity "больше 0"
        date DesiredDate
        string Comment
    }

    OrderStatusHistory {
        int Id PK
        int OrderId FK
        string FromStatus "пусто у первой записи"
        string ToStatus
        int ChangedByUserId FK
        datetime ChangedAt
    }
```

## Ограничения и индексы

| Таблица | Ограничение или индекс |
|---------|------------------------|
| Users | уникальный индекс по `Email` |
| Categories | индекс по `ParentId` |
| Services | индексы по `CategoryId`, `Price`, `PerformerId`; проверка `Price >= 0` |
| CartItems | уникальная пара (`ClientId`, `ServiceId`); проверка `Quantity > 0` |
| Orders | индексы (`ClientId`, `Status`) и (`PerformerId`, `Status`) |
| OrderItems | индекс по `OrderId`; проверка `Quantity > 0` |
| OrderStatusHistory | индекс по `OrderId` |

## Правила удаления связанных записей

| Связь | Правило |
|-------|---------|
| Категория → подкатегории и услуги | запрет удаления (`Restrict`) |
| Пользователь → услуги, заказы, история статусов | запрет удаления (`Restrict`) |
| Услуга → позиции заказов | запрет удаления (`Restrict`) |
| Клиент → позиции корзины | каскадное удаление |
| Услуга → позиции корзины | каскадное удаление |
| Заказ → позиции и история статусов | каскадное удаление |

Вместо удаления услуга скрывается (`IsActive = false`), потому что на неё могут ссылаться заказы. Цена и название услуги копируются в `OrderItems` при оформлении заказа, поэтому последующее изменение услуги не влияет на старые заказы.
