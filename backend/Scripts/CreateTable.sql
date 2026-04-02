-- Скрипт создания таблицы сотрудников для SQL Server (SSMS)
-- Выполните в вашей базе данных

USE lktest;
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Employees')
BEGIN
    CREATE TABLE Employees (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        LastName NVARCHAR(100) NOT NULL,
        FirstName NVARCHAR(100) NOT NULL,
        Patronymic NVARCHAR(100) NOT NULL,
        EmployeeId NVARCHAR(50) NOT NULL,
        Phone NVARCHAR(20) NOT NULL,
        CreatedAt DATETIME2 DEFAULT GETDATE(),
        CONSTRAINT UQ_EmployeeId UNIQUE (EmployeeId)
    );

    -- Индекс для быстрого поиска по телефону
    CREATE INDEX IX_Employees_Phone ON Employees(Phone);
    
    -- Пример данных для тестирования
    INSERT INTO Employees (LastName, FirstName, Patronymic, EmployeeId, Phone) VALUES
    (N'Иванов', N'Иван', N'Иванович', '001', '79991234567'),
    (N'Петров', N'Пётр', N'Петрович', '002', '+7 999 222 33 44'),
    (N'Сидоров', N'Сидор', N'Сидорович', '003', '89991234567');

    PRINT 'Таблица Employees создана успешно';
END
GO

