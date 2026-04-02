# Backend API для проверки сотрудников

ASP.NET Core Web API, подключается к SQL Server (SSMS) для сверки данных сотрудников.

## Настройка

1. **Создайте базу данных** в SQL Server Management Studio и выполните скрипт `Scripts/CreateTable.sql`

2. **Настройте строку подключения** в `appsettings.json`:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=ВАШ_СЕРВЕР;Database=EmployeesDb;User Id=ВАШ_ЛОГИН;Password=ВАШ_ПАРОЛЬ;TrustServerCertificate=True;"
}
```

3. **Запустите API**:
```bash
cd backend
dotnet run
```

API будет доступен по адресу: `http://localhost:5000`

## Настройка Android-приложения

В `app/build.gradle` измените `API_BASE_URL`:
- Эмулятор: `http://10.0.2.2:5000/` (localhost компьютера)
- Реальное устройство в той же сети: `http://IP_ВАШЕГО_КОМПЬЮТЕРА:5000/`

## Формат номера телефона

Поддерживаются форматы:
- +7 999 123 45 67
- 8 999 123 45 67
- 999 123 45 67

Все приводятся к формату 79XXXXXXXXX для сравнения с БД.

