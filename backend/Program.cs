using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using EmployeeApi.Services;
using EmployeeApi.Hubs;
using EmployeeApi.Services.Coins;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseWebRoot("wwwroot");

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSingleton<ChatMessageCipher>();
builder.Services.AddSingleton<ICoinsService, SqlCoinsService>();
builder.Services.AddHostedService<PostEventCoinsGrantHostedService>();
builder.Services.AddHostedService<ServerDiagnosticsLifetimeHostedService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
if (args.Length >= 1 && (args[0].Equals("grant-posts", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("revoke-posts", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("grant-tech-admin", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("revoke-tech-admin", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("grant-dev-console", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("revoke-dev-console", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("notify-test", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("notify-test-all", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("notify-update-all", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("push-update-all", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("security-test", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("security-approve-login", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("clear-posts", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("clear-notifications", StringComparison.OrdinalIgnoreCase)
                         || args[0].Equals("clear-everything", StringComparison.OrdinalIgnoreCase)))
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.WriteLine("Missing connection string 'DefaultConnection' in appsettings.");
        return;
    }

    if (args[0].Equals("grant-posts", StringComparison.OrdinalIgnoreCase)
        || args[0].Equals("revoke-posts", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Login is required. Example: dotnet run -- grant-posts test");
            return;
        }
        var login = args[1].Trim();
        if (string.IsNullOrWhiteSpace(login))
        {
            Console.WriteLine("Login is required. Example: dotnet run -- grant-posts test");
            return;
        }

        var canCreate = args[0].Equals("grant-posts", StringComparison.OrdinalIgnoreCase);
        var affected = await UpsertCanCreatePostsAsync(connectionString, login, canCreate);

        var notifId = await InsertNotificationAsync(
            connectionString,
            recipientLogin: login,
            type: "permissions",
            title: canCreate ? "Доступ выдан" : "Доступ отозван",
            body: canCreate
                ? "Технический администратор выдал вам доступ к публикации новостей."
                : "Технический администратор отозвал у вас доступ к публикации новостей.",
            action: "open_profile",
            actionData: null);

        if (FcmPush.IsConfigured())
        {
            try
            {
                await FcmPush.SendToLoginAsync(
                    connectionString,
                    login,
                    canCreate ? "Доступ выдан" : "Доступ отозван",
                    canCreate
                        ? "Технический администратор выдал вам доступ к публикации новостей."
                        : "Технический администратор отозвал у вас доступ к публикации новостей.",
                    new Dictionary<string, string>
                    {
                        ["type"] = "permissions",
                        ["action"] = "open_profile",
                        ["notificationId"] = notifId.ToString()
                    });
            }
            catch
            {
            }
        }

        Console.WriteLine(affected > 0
            ? $"OK: {(canCreate ? "granted" : "revoked")} CanCreatePosts for '{login}'."
            : $"OK: {(canCreate ? "granted" : "revoked")} CanCreatePosts for '{login}' (no rows affected).");
        return;
    }

    if (args[0].Equals("grant-tech-admin", StringComparison.OrdinalIgnoreCase)
        || args[0].Equals("revoke-tech-admin", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Login is required. Example: dotnet run -- grant-tech-admin test");
            return;
        }
        var login = args[1].Trim();
        if (string.IsNullOrWhiteSpace(login))
        {
            Console.WriteLine("Login is required. Example: dotnet run -- grant-tech-admin test");
            return;
        }

        var allow = args[0].Equals("grant-tech-admin", StringComparison.OrdinalIgnoreCase);
        var affected = await UpsertCanTechAdminAsync(connectionString, login, allow);

        var title = allow ? "Назначен технический администратор" : "Снят технический администратор";
        var body = allow
            ? "Вам выданы права технического администратора (полный доступ)."
            : "У вас сняты права технического администратора.";
        var notifId = await InsertNotificationAsync(
            connectionString,
            recipientLogin: login,
            type: "permissions",
            title: title,
            body: body,
            action: "open_profile",
            actionData: null);

        if (FcmPush.IsConfigured())
        {
            try
            {
                await FcmPush.SendToLoginAsync(
                    connectionString,
                    login,
                    title,
                    body,
                    new Dictionary<string, string>
                    {
                        ["type"] = "permissions",
                        ["action"] = "open_profile",
                        ["notificationId"] = notifId.ToString()
                    });
            }
            catch
            {
            }
        }

        Console.WriteLine(affected > 0
            ? $"OK: {(allow ? "granted" : "revoked")} CanTechAdmin for '{login}'."
            : $"OK: {(allow ? "granted" : "revoked")} CanTechAdmin for '{login}' (no rows affected).");
        return;
    }

    if (args[0].Equals("grant-dev-console", StringComparison.OrdinalIgnoreCase)
        || args[0].Equals("revoke-dev-console", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Login is required. Example: dotnet run -- grant-dev-console test");
            return;
        }
        var login = args[1].Trim();
        if (string.IsNullOrWhiteSpace(login))
        {
            Console.WriteLine("Login is required. Example: dotnet run -- grant-dev-console test");
            return;
        }

        var allowed = args[0].Equals("grant-dev-console", StringComparison.OrdinalIgnoreCase);
        var affected = await UpsertCanUseDevConsoleAsync(connectionString, login, allowed);
        Console.WriteLine(affected > 0
            ? $"OK: {(allowed ? "granted" : "revoked")} CanUseDevConsole for '{login}'."
            : $"OK: {(allowed ? "granted" : "revoked")} CanUseDevConsole for '{login}' (no rows affected).");
        return;
    }

    if (args[0].Equals("clear-posts", StringComparison.OrdinalIgnoreCase))
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var deleted = await ClearAllPostsAsync(connection);
        Console.WriteLine($"OK: deleted posts from App_Posts. rows={deleted}.");
        return;
    }

    if (args[0].Equals("clear-notifications", StringComparison.OrdinalIgnoreCase))
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var result = await ClearAllNotificationsAsync(connection);
        Console.WriteLine($"OK: deleted notifications. reads={result.reads}, notifications={result.notifications}.");
        return;
    }

    if (args[0].Equals("clear-everything", StringComparison.OrdinalIgnoreCase))
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var deletedPosts = await ClearAllPostsAsync(connection);
        var result = await ClearAllNotificationsAsync(connection);

        Console.WriteLine($"OK: cleared everything. posts={deletedPosts}, reads={result.reads}, notifications={result.notifications}.");
        return;
    }

    if (args[0].Equals("notify-test-all", StringComparison.OrdinalIgnoreCase))
    {
        var id = await InsertNotificationAsync(
            connectionString,
            recipientLogin: null,
            type: "test",
            title: "Тестовое уведомление",
            body: "Это тестовое уведомление для всех пользователей.",
            action: "open_notifications",
            actionData: null);

        if (FcmPush.IsConfigured())
        {
            try
            {
                await FcmPush.SendBroadcastAsync(
                    connectionString,
                    "Тестовое уведомление",
                    "Это тестовое уведомление для всех пользователей.",
                    new Dictionary<string, string>
                    {
                        ["type"] = "test",
                        ["action"] = "open_notifications",
                        ["notificationId"] = id.ToString()
                    });
            }
            catch
            {
                // ignore
            }
        }

        Console.WriteLine($"OK: created broadcast notification id={id}.");
        return;
    }

    if (args[0].Equals("notify-update-all", StringComparison.OrdinalIgnoreCase))
    {
        var version = args.Length >= 2 ? args[1].Trim() : "";
        var suffix = string.IsNullOrWhiteSpace(version) ? "" : $" (v{version})";
        var id = await InsertNotificationAsync(
            connectionString,
            recipientLogin: null,
            type: "update",
            title: $"Доступно обновление приложения{suffix}",
            body: "Вышла новая версия. Перейдите в Профиль и нажмите «Обновить приложение».",
            action: "open_profile",
            actionData: version);

        if (FcmPush.IsConfigured())
        {
            try
            {
                await FcmPush.SendBroadcastAsync(
                    connectionString,
                    $"Доступно обновление приложения{suffix}",
                    "Вышла новая версия. Перейдите в Профиль и нажмите «Обновить приложение».",
                    new Dictionary<string, string>
                    {
                        ["type"] = "update",
                        ["action"] = "open_profile",
                        ["actionData"] = version,
                        ["notificationId"] = id.ToString()
                    });
            }
            catch
            {
                // ignore
            }
        }

        Console.WriteLine($"OK: created update broadcast notification id={id}.");
        return;
    }

    if (args[0].Equals("push-update-all", StringComparison.OrdinalIgnoreCase))
    {
        var version = args.Length >= 2 ? args[1].Trim() : "";
        var suffix = string.IsNullOrWhiteSpace(version) ? "" : $" v{version}";

        var serviceAccountPath = Environment.GetEnvironmentVariable("FCM_SERVICE_ACCOUNT_PATH")?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(serviceAccountPath))
        {
            Console.WriteLine("Missing env var FCM_SERVICE_ACCOUNT_PATH (path to Firebase service account json).");
            return;
        }

        var sent = await SendFcmUpdateBroadcastAsync(connectionString, serviceAccountPath,
            title: $"Доступно обновление приложения{suffix}",
            body: "Вышла новая версия. Откройте Профиль и нажмите «Обновить приложение».",
            versionCode: version);

        Console.WriteLine($"OK: push-update-all sent to {sent} devices.");
        return;
    }

    if (args[0].Equals("notify-test", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Login is required. Example: dotnet run -- notify-test test");
            return;
        }
        var login = args[1].Trim();
        if (string.IsNullOrWhiteSpace(login))
        {
            Console.WriteLine("Login is required. Example: dotnet run -- notify-test test");
            return;
        }

        var id = await InsertNotificationAsync(
            connectionString,
            recipientLogin: login,
            type: "test",
            title: "Тестовое уведомление",
            body: $"Это тестовое уведомление для пользователя {login}.",
            action: "open_notifications",
            actionData: null);

        if (FcmPush.IsConfigured())
        {
            try
            {
                await FcmPush.SendToLoginAsync(
                    connectionString,
                    login,
                    "Тестовое уведомление",
                    $"Это тестовое уведомление для пользователя {login}.",
                    new Dictionary<string, string>
                    {
                        ["type"] = "test",
                        ["action"] = "open_notifications",
                        ["notificationId"] = id.ToString()
                    });
            }
            catch
            {
                // ignore
            }
        }

        Console.WriteLine($"OK: created notification for '{login}' id={id}.");
        return;
    }

    if (args[0].Equals("security-test", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Login is required. Example: dotnet run -- security-test test");
            return;
        }
        var login = args[1].Trim();
        if (string.IsNullOrWhiteSpace(login))
        {
            Console.WriteLine("Login is required. Example: dotnet run -- security-test test");
            return;
        }

        var deviceId = args.Length >= 3 ? args[2].Trim() : Guid.NewGuid().ToString("N");
        var deviceName = args.Length >= 4 ? string.Join(' ', args.Skip(3)).Trim() : "CLI Test Device";

        // Create pending attempt (simulates "unknown device login")
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();

            const string ensureSql = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_LoginDevices')
                CREATE TABLE [App_LoginDevices] (
                    [Login] NVARCHAR(100) NOT NULL,
                    [DeviceId] NVARCHAR(100) NOT NULL,
                    [DeviceName] NVARCHAR(200) NULL,
                    [FirstSeenAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    [LastSeenAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    [LastIp] NVARCHAR(100) NULL,
                    [LastUserAgent] NVARCHAR(500) NULL,
                    CONSTRAINT [PK_App_LoginDevices] PRIMARY KEY ([Login], [DeviceId])
                );

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_BlockedIps')
                CREATE TABLE [App_BlockedIps] (
                    [Login] NVARCHAR(100) NOT NULL,
                    [Ip] NVARCHAR(100) NOT NULL,
                    [BlockedUntil] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    CONSTRAINT [PK_App_BlockedIps] PRIMARY KEY ([Login], [Ip])
                );

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_SecurityLoginAttempts')
                CREATE TABLE [App_SecurityLoginAttempts] (
                    [Id] INT IDENTITY(1,1) PRIMARY KEY,
                    [RecipientLogin] NVARCHAR(100) NOT NULL,
                    [DeviceId] NVARCHAR(100) NOT NULL,
                    [DeviceName] NVARCHAR(200) NULL,
                    [Ip] NVARCHAR(100) NULL,
                    [UserAgent] NVARCHAR(500) NULL,
                    [Status] NVARCHAR(20) NOT NULL DEFAULT 'pending',
                    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    [ApprovedAt] DATETIME2 NULL,
                    [DeniedAt] DATETIME2 NULL
                );";
            await using (var ensure = new SqlCommand(ensureSql, connection))
            {
                await ensure.ExecuteNonQueryAsync();
            }

            const string insertSql = @"
                INSERT INTO [App_SecurityLoginAttempts] ([RecipientLogin], [DeviceId], [DeviceName], [Ip], [UserAgent], [Status])
                VALUES (@Login, @DeviceId, @DeviceName, @Ip, @UA, 'pending');
                SELECT SCOPE_IDENTITY();";
            await using (var cmd = new SqlCommand(insertSql, connection))
            {
                cmd.Parameters.AddWithValue("@Login", login);
                cmd.Parameters.AddWithValue("@DeviceId", deviceId);
                cmd.Parameters.AddWithValue("@DeviceName", (object?)deviceName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Ip", "cli");
                cmd.Parameters.AddWithValue("@UA", "cli");
                var attemptIdObj = await cmd.ExecuteScalarAsync();
                var attemptId = Convert.ToInt32(attemptIdObj ?? 0);

                var id = await InsertNotificationAsync(
                    connectionString,
                    recipientLogin: login,
                    type: "security",
                    title: "Безопасность: требуется подтверждение входа",
                    body: $"(Тест) Вход с устройства: {deviceName}. IP: cli",
                    action: "security_login",
                    actionData: attemptId.ToString());

                if (FcmPush.IsConfigured())
                {
                    try
                    {
                        await FcmPush.SendToLoginAsync(
                            connectionString,
                            login,
                            "Безопасность: требуется подтверждение входа",
                            $"(Тест) Вход с устройства: {deviceName}. IP: cli",
                            new Dictionary<string, string>
                            {
                                ["type"] = "security",
                                ["action"] = "security_login",
                                ["actionData"] = attemptId.ToString(),
                                ["notificationId"] = id.ToString()
                            });
                    }
                    catch
                    {
                        // ignore
                    }
                }

                Console.WriteLine($"OK: created security attempt id={attemptId} and notification id={id} for '{login}'.");
            }
        }
        return;
    }

    if (args[0].Equals("security-approve-login", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Login is required. Example: dotnet run -- security-approve-login test");
            return;
        }
        var login = args[1].Trim();
        if (string.IsNullOrWhiteSpace(login))
        {
            Console.WriteLine("Login is required. Example: dotnet run -- security-approve-login test");
            return;
        }

        int? attemptIdFilter = null;
        if (args.Length >= 3 && int.TryParse(args[2].Trim(), out var parsedAttemptId) && parsedAttemptId > 0)
            attemptIdFilter = parsedAttemptId;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        const string ensureSql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_LoginDevices')
            CREATE TABLE [App_LoginDevices] (
                [Login] NVARCHAR(100) NOT NULL,
                [DeviceId] NVARCHAR(100) NOT NULL,
                [DeviceName] NVARCHAR(200) NULL,
                [FirstSeenAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                [LastSeenAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                [LastIp] NVARCHAR(100) NULL,
                [LastUserAgent] NVARCHAR(500) NULL,
                CONSTRAINT [PK_App_LoginDevices] PRIMARY KEY ([Login], [DeviceId])
            );

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_BlockedIps')
            CREATE TABLE [App_BlockedIps] (
                [Login] NVARCHAR(100) NOT NULL,
                [Ip] NVARCHAR(100) NOT NULL,
                [BlockedUntil] DATETIME2 NOT NULL,
                [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT [PK_App_BlockedIps] PRIMARY KEY ([Login], [Ip])
            );

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_SecurityLoginAttempts')
            CREATE TABLE [App_SecurityLoginAttempts] (
                [Id] INT IDENTITY(1,1) PRIMARY KEY,
                [RecipientLogin] NVARCHAR(100) NOT NULL,
                [DeviceId] NVARCHAR(100) NOT NULL,
                [DeviceName] NVARCHAR(200) NULL,
                [Ip] NVARCHAR(100) NULL,
                [UserAgent] NVARCHAR(500) NULL,
                [Status] NVARCHAR(20) NOT NULL DEFAULT 'pending',
                [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                [ApprovedAt] DATETIME2 NULL,
                [DeniedAt] DATETIME2 NULL
            );";
        await using (var ensure = new SqlCommand(ensureSql, connection))
        {
            await ensure.ExecuteNonQueryAsync();
        }

        const string selectSql = @"
            SELECT TOP 1 [Id], [RecipientLogin], [DeviceId], [DeviceName], [Ip], [UserAgent], [Status]
            FROM [App_SecurityLoginAttempts]
            WHERE [RecipientLogin] = @Login
              AND (@AttemptId IS NULL OR [Id] = @AttemptId)
            ORDER BY [CreatedAt] DESC;";
        await using var sel = new SqlCommand(selectSql, connection);
        sel.Parameters.AddWithValue("@Login", login);
        sel.Parameters.AddWithValue("@AttemptId", (object?)attemptIdFilter ?? DBNull.Value);

        int attemptId;
        string recipientLogin;
        string deviceId;
        string? deviceName;
        string? ip;
        string? ua;
        string? status;

        await using (var reader = await sel.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
            {
                Console.WriteLine(attemptIdFilter.HasValue
                    ? $"NOT FOUND: no attempt with id={attemptIdFilter.Value} for login='{login}'."
                    : $"NOT FOUND: no attempts for login='{login}'.");
                return;
            }

            attemptId = reader.GetInt32(0);
            recipientLogin = reader.GetString(1);
            deviceId = reader.GetString(2);
            deviceName = reader.IsDBNull(3) ? null : reader.GetString(3);
            ip = reader.IsDBNull(4) ? null : reader.GetString(4);
            ua = reader.IsDBNull(5) ? null : reader.GetString(5);
            status = reader.IsDBNull(6) ? null : reader.GetString(6);
        }

        if (!string.Equals(recipientLogin, login, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"ERROR: attempt recipient '{recipientLogin}' does not match login '{login}'.");
            return;
        }

        if (!string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"SKIP: attempt id={attemptId} already processed. status='{status}'.");
            return;
        }

        const string updateAttemptSql = @"
            UPDATE [App_SecurityLoginAttempts]
            SET [Status] = 'approved',
                [ApprovedAt] = GETUTCDATE()
            WHERE [Id] = @Id;";
        await using (var upd = new SqlCommand(updateAttemptSql, connection))
        {
            upd.Parameters.AddWithValue("@Id", attemptId);
            await upd.ExecuteNonQueryAsync();
        }

        const string upsertDeviceSql = @"
            IF EXISTS (SELECT 1 FROM [App_LoginDevices] WHERE [Login] = @Login AND [DeviceId] = @DeviceId)
                UPDATE [App_LoginDevices]
                SET [DeviceName] = @DeviceName,
                    [LastSeenAt] = GETUTCDATE(),
                    [LastIp] = @Ip,
                    [LastUserAgent] = @UA
                WHERE [Login] = @Login AND [DeviceId] = @DeviceId
            ELSE
                INSERT INTO [App_LoginDevices] ([Login], [DeviceId], [DeviceName], [FirstSeenAt], [LastSeenAt], [LastIp], [LastUserAgent])
                VALUES (@Login, @DeviceId, @DeviceName, GETUTCDATE(), GETUTCDATE(), @Ip, @UA);";
        await using (var upsert = new SqlCommand(upsertDeviceSql, connection))
        {
            upsert.Parameters.AddWithValue("@Login", login);
            upsert.Parameters.AddWithValue("@DeviceId", deviceId);
            upsert.Parameters.AddWithValue("@DeviceName", (object?)deviceName ?? DBNull.Value);
            upsert.Parameters.AddWithValue("@Ip", (object?)ip ?? DBNull.Value);
            upsert.Parameters.AddWithValue("@UA", (object?)ua ?? DBNull.Value);
            await upsert.ExecuteNonQueryAsync();
        }

        if (!string.IsNullOrWhiteSpace(ip))
        {
            const string deleteBlockSql = @"
                DELETE FROM [App_BlockedIps]
                WHERE [Login] = @Login AND [Ip] = @Ip;";
            await using var del = new SqlCommand(deleteBlockSql, connection);
            del.Parameters.AddWithValue("@Login", login);
            del.Parameters.AddWithValue("@Ip", ip);
            await del.ExecuteNonQueryAsync();
        }

        Console.WriteLine($"OK: approved login for '{login}'. attemptId={attemptId} deviceId='{deviceId}' deviceName='{deviceName ?? ""}' ip='{ip ?? ""}'.");
        return;
    }
}

var app = builder.Build();

app.UseMiddleware<ServerDiagnosticsMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseCors();
var staticContentTypes = new FileExtensionContentTypeProvider();
staticContentTypes.Mappings[".apk"] = "application/vnd.android.package-archive";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticContentTypes
});
app.MapControllers();
app.MapHub<ChatRealtimeHub>("/hubs/chat");

app.Run();

static async Task<int> UpsertCanCreatePostsAsync(string connectionString, string login, bool canCreatePosts)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    const string ensureSql = @"
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_UserPermissions')
        CREATE TABLE [App_UserPermissions] (
            [Login] NVARCHAR(100) NOT NULL PRIMARY KEY,
            [CanCreatePosts] BIT NOT NULL DEFAULT 0,
            [CanTechAdmin] BIT NOT NULL DEFAULT 0,
            [CanUseDevConsole] BIT NOT NULL DEFAULT 0,
            [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
        );
        IF COL_LENGTH('App_UserPermissions', 'CanTechAdmin') IS NULL
            ALTER TABLE [App_UserPermissions] ADD [CanTechAdmin] BIT NOT NULL CONSTRAINT [DF_App_UserPermissions_CanTechAdmin] DEFAULT 0;
        IF COL_LENGTH('App_UserPermissions', 'CanUseDevConsole') IS NULL
            ALTER TABLE [App_UserPermissions] ADD [CanUseDevConsole] BIT NOT NULL CONSTRAINT [DF_App_UserPermissions_CanUseDevConsole] DEFAULT 0;";
    await using (var ensure = new SqlCommand(ensureSql, connection))
    {
        await ensure.ExecuteNonQueryAsync();
    }

    const string upsertSql = @"
        IF EXISTS (SELECT 1 FROM [App_UserPermissions] WHERE [Login] = @Login)
            UPDATE [App_UserPermissions]
            SET [CanCreatePosts] = @CanCreatePosts,
                [UpdatedAt] = GETUTCDATE()
            WHERE [Login] = @Login;
        ELSE
            INSERT INTO [App_UserPermissions] ([Login], [CanCreatePosts], [UpdatedAt])
            VALUES (@Login, @CanCreatePosts, GETUTCDATE());

        SELECT @@ROWCOUNT;";

    await using var cmd = new SqlCommand(upsertSql, connection);
    cmd.Parameters.AddWithValue("@Login", login);
    cmd.Parameters.AddWithValue("@CanCreatePosts", canCreatePosts ? 1 : 0);
    var result = await cmd.ExecuteScalarAsync();
    return Convert.ToInt32(result ?? 0);
}

static async Task<int> UpsertCanUseDevConsoleAsync(string connectionString, string login, bool canUseDevConsole)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    const string ensureSql = @"
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_UserPermissions')
        CREATE TABLE [App_UserPermissions] (
            [Login] NVARCHAR(100) NOT NULL PRIMARY KEY,
            [CanCreatePosts] BIT NOT NULL DEFAULT 0,
            [CanTechAdmin] BIT NOT NULL DEFAULT 0,
            [CanUseDevConsole] BIT NOT NULL DEFAULT 0,
            [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
        );
        IF COL_LENGTH('App_UserPermissions', 'CanTechAdmin') IS NULL
            ALTER TABLE [App_UserPermissions] ADD [CanTechAdmin] BIT NOT NULL CONSTRAINT [DF_App_UserPermissions_CanTechAdmin] DEFAULT 0;
        IF COL_LENGTH('App_UserPermissions', 'CanUseDevConsole') IS NULL
            ALTER TABLE [App_UserPermissions] ADD [CanUseDevConsole] BIT NOT NULL CONSTRAINT [DF_App_UserPermissions_CanUseDevConsole] DEFAULT 0;";
    await using (var ensure = new SqlCommand(ensureSql, connection))
    {
        await ensure.ExecuteNonQueryAsync();
    }

    const string upsertSql = @"
        IF EXISTS (SELECT 1 FROM [App_UserPermissions] WHERE [Login] = @Login)
            UPDATE [App_UserPermissions]
            SET [CanUseDevConsole] = @CanUseDevConsole,
                [UpdatedAt] = GETUTCDATE()
            WHERE [Login] = @Login;
        ELSE
            INSERT INTO [App_UserPermissions] ([Login], [CanCreatePosts], [CanUseDevConsole], [UpdatedAt])
            VALUES (@Login, 0, @CanUseDevConsole, GETUTCDATE());
        SELECT @@ROWCOUNT;";

    await using var cmd = new SqlCommand(upsertSql, connection);
    cmd.Parameters.AddWithValue("@Login", login);
    cmd.Parameters.AddWithValue("@CanUseDevConsole", canUseDevConsole ? 1 : 0);
    var result = await cmd.ExecuteScalarAsync();
    return Convert.ToInt32(result ?? 0);
}

static async Task<int> UpsertCanTechAdminAsync(string connectionString, string login, bool canTechAdmin)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    const string ensureSql = @"
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_UserPermissions')
        CREATE TABLE [App_UserPermissions] (
            [Login] NVARCHAR(100) NOT NULL PRIMARY KEY,
            [CanCreatePosts] BIT NOT NULL DEFAULT 0,
            [CanTechAdmin] BIT NOT NULL DEFAULT 0,
            [CanUseDevConsole] BIT NOT NULL DEFAULT 0,
            [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
        );
        IF COL_LENGTH('App_UserPermissions', 'CanTechAdmin') IS NULL
            ALTER TABLE [App_UserPermissions] ADD [CanTechAdmin] BIT NOT NULL CONSTRAINT [DF_App_UserPermissions_CanTechAdmin] DEFAULT 0;
        IF COL_LENGTH('App_UserPermissions', 'CanUseDevConsole') IS NULL
            ALTER TABLE [App_UserPermissions] ADD [CanUseDevConsole] BIT NOT NULL CONSTRAINT [DF_App_UserPermissions_CanUseDevConsole] DEFAULT 0;";
    await using (var ensure = new SqlCommand(ensureSql, connection))
    {
        await ensure.ExecuteNonQueryAsync();
    }

    const string upsertSql = @"
        IF EXISTS (SELECT 1 FROM [App_UserPermissions] WHERE [Login] = @Login)
            UPDATE [App_UserPermissions]
            SET [CanTechAdmin] = @CanTechAdmin,
                [CanCreatePosts] = CASE WHEN @CanTechAdmin = 1 THEN 1 ELSE [CanCreatePosts] END,
                [CanUseDevConsole] = CASE WHEN @CanTechAdmin = 1 THEN 1 ELSE [CanUseDevConsole] END,
                [UpdatedAt] = GETUTCDATE()
            WHERE [Login] = @Login;
        ELSE
            INSERT INTO [App_UserPermissions] ([Login], [CanCreatePosts], [CanTechAdmin], [CanUseDevConsole], [UpdatedAt])
            VALUES (@Login, CASE WHEN @CanTechAdmin = 1 THEN 1 ELSE 0 END, @CanTechAdmin, CASE WHEN @CanTechAdmin = 1 THEN 1 ELSE 0 END, GETUTCDATE());
        SELECT @@ROWCOUNT;";

    await using var cmd = new SqlCommand(upsertSql, connection);
    cmd.Parameters.AddWithValue("@Login", login);
    cmd.Parameters.AddWithValue("@CanTechAdmin", canTechAdmin ? 1 : 0);
    var result = await cmd.ExecuteScalarAsync();
    return Convert.ToInt32(result ?? 0);
}

static async Task<int> InsertNotificationAsync(
    string connectionString,
    string? recipientLogin,
    string? type,
    string title,
    string? body,
    string? action,
    string? actionData)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    const string ensureSql = @"
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_Notifications')
        CREATE TABLE [App_Notifications] (
            [Id] INT IDENTITY(1,1) PRIMARY KEY,
            [RecipientLogin] NVARCHAR(100) NULL,
            [Type] NVARCHAR(50) NULL,
            [Title] NVARCHAR(200) NOT NULL,
            [Body] NVARCHAR(1000) NULL,
            [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
            [Action] NVARCHAR(50) NULL,
            [ActionData] NVARCHAR(500) NULL
        );

        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_NotificationReads')
        CREATE TABLE [App_NotificationReads] (
            [NotificationId] INT NOT NULL,
            [Login] NVARCHAR(100) NOT NULL,
            [ReadAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
            CONSTRAINT [PK_App_NotificationReads] PRIMARY KEY ([NotificationId], [Login])
        );";

    await using (var ensure = new SqlCommand(ensureSql, connection))
    {
        await ensure.ExecuteNonQueryAsync();
    }

    const string insertSql = @"
        INSERT INTO [App_Notifications] ([RecipientLogin], [Type], [Title], [Body], [CreatedAt], [Action], [ActionData])
        VALUES (@RecipientLogin, @Type, @Title, @Body, GETUTCDATE(), @Action, @ActionData);
        SELECT SCOPE_IDENTITY();";

    await using var cmd = new SqlCommand(insertSql, connection);
    cmd.Parameters.AddWithValue("@RecipientLogin", (object?)recipientLogin ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Type", (object?)type ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Title", title);
    cmd.Parameters.AddWithValue("@Body", (object?)body ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@Action", (object?)action ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@ActionData", (object?)actionData ?? DBNull.Value);

    var idObj = await cmd.ExecuteScalarAsync();
    return Convert.ToInt32(idObj ?? 0);
}

static async Task<int> ClearAllPostsAsync(SqlConnection connection)
{
    const string sql = @"
        IF OBJECT_ID('App_Posts', 'U') IS NOT NULL
        BEGIN
            DELETE FROM [App_Posts];
            SELECT @@ROWCOUNT;
        END
        ELSE
        BEGIN
            SELECT 0;
        END;";

    await using var cmd = new SqlCommand(sql, connection);
    var rowObj = await cmd.ExecuteScalarAsync();
    return Convert.ToInt32(rowObj ?? 0);
}

static async Task<(int reads, int notifications)> ClearAllNotificationsAsync(SqlConnection connection)
{
    // Delete reads first (even though there are no FK constraints, it's safer for cleanup).
    const string readsSql = @"
        IF OBJECT_ID('App_NotificationReads', 'U') IS NOT NULL
        BEGIN
            DELETE FROM [App_NotificationReads];
            SELECT @@ROWCOUNT;
        END
        ELSE
        BEGIN
            SELECT 0;
        END;";

    const string notifsSql = @"
        IF OBJECT_ID('App_Notifications', 'U') IS NOT NULL
        BEGIN
            DELETE FROM [App_Notifications];
            SELECT @@ROWCOUNT;
        END
        ELSE
        BEGIN
            SELECT 0;
        END;";

    await using var readsCmd = new SqlCommand(readsSql, connection);
    var readsObj = await readsCmd.ExecuteScalarAsync();

    await using var notifsCmd = new SqlCommand(notifsSql, connection);
    var notifsObj = await notifsCmd.ExecuteScalarAsync();

    return (Convert.ToInt32(readsObj ?? 0), Convert.ToInt32(notifsObj ?? 0));
}

static async Task EnsurePushTokensTableAsync(SqlConnection connection)
{
    // Keep this migration logic simple and robust (no huge IF/ELSE batch).
    const string createSql = @"
        IF OBJECT_ID('App_PushTokens', 'U') IS NULL
        CREATE TABLE [App_PushTokens] (
            [Login] NVARCHAR(100) NULL,
            [Token] NVARCHAR(300) NOT NULL,
            [DeviceId] NVARCHAR(100) NULL,
            [DeviceName] NVARCHAR(200) NULL,
            [Platform] NVARCHAR(30) NULL,
            [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
        );";
    await using (var create = new SqlCommand(createSql, connection))
    {
        await create.ExecuteNonQueryAsync();
    }

    const string getPkSql = @"
        SELECT TOP 1 kc.name
        FROM sys.key_constraints kc
        WHERE kc.parent_object_id = OBJECT_ID('App_PushTokens')
          AND kc.[type] = 'PK';";
    string? pkName = null;
    await using (var getPk = new SqlCommand(getPkSql, connection))
    {
        var o = await getPk.ExecuteScalarAsync();
        pkName = o == null || o == DBNull.Value ? null : o.ToString();
    }
    if (!string.IsNullOrWhiteSpace(pkName))
    {
        var dropPkSql = $"ALTER TABLE [App_PushTokens] DROP CONSTRAINT [{pkName}];";
        await using var drop = new SqlCommand(dropPkSql, connection);
        await drop.ExecuteNonQueryAsync();
    }

    const string alterLoginSql = @"
        IF EXISTS (
            SELECT 1
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID('App_PushTokens')
              AND c.name = 'Login'
              AND c.is_nullable = 0
        )
            ALTER TABLE [App_PushTokens] ALTER COLUMN [Login] NVARCHAR(100) NULL;";
    await using (var alter = new SqlCommand(alterLoginSql, connection))
    {
        await alter.ExecuteNonQueryAsync();
    }

    const string dedupeSql = @"
        ;WITH D AS (
            SELECT
                [Token],
                [UpdatedAt],
                ROW_NUMBER() OVER (PARTITION BY [Token] ORDER BY [UpdatedAt] DESC) AS rn
            FROM [App_PushTokens]
        )
        DELETE t
        FROM [App_PushTokens] t
        JOIN D ON D.[Token] = t.[Token] AND D.[UpdatedAt] = t.[UpdatedAt]
        WHERE D.rn > 1;";
    await using (var dedupe = new SqlCommand(dedupeSql, connection))
    {
        await dedupe.ExecuteNonQueryAsync();
    }

    const string addPkSql = @"
        IF NOT EXISTS (
            SELECT 1
            FROM sys.key_constraints kc
            WHERE kc.parent_object_id = OBJECT_ID('App_PushTokens')
              AND kc.[type] = 'PK'
        )
            ALTER TABLE [App_PushTokens] ADD CONSTRAINT [PK_App_PushTokens_Token] PRIMARY KEY ([Token]);";
    await using (var addPk = new SqlCommand(addPkSql, connection))
    {
        await addPk.ExecuteNonQueryAsync();
    }
}

static async Task<int> SendFcmUpdateBroadcastAsync(
    string connectionString,
    string serviceAccountJsonPath,
    string title,
    string body,
    string? versionCode)
{
    // Lazy init Firebase Admin
    if (FirebaseAdmin.FirebaseApp.DefaultInstance == null)
    {
        var appOptions = new FirebaseAdmin.AppOptions
        {
            Credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromFile(serviceAccountJsonPath)
        };
        FirebaseAdmin.FirebaseApp.Create(appOptions);
    }

    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await EnsurePushTokensTableAsync(connection);

    const string sql = @"
        SELECT [Token]
        FROM [App_PushTokens]
        WHERE ISNULL([Platform], 'android') = 'android';";
    await using var cmd = new SqlCommand(sql, connection);
    await using var reader = await cmd.ExecuteReaderAsync();

    var tokens = new List<string>();
    while (await reader.ReadAsync())
    {
        if (!reader.IsDBNull(0))
        {
            var t = reader.GetString(0)?.Trim();
            if (!string.IsNullOrWhiteSpace(t)) tokens.Add(t);
        }
    }

    if (tokens.Count == 0) return 0;

    // FCM multicast (up to 500 tokens per request)
    var messaging = FirebaseAdmin.Messaging.FirebaseMessaging.DefaultInstance;
    var sentTotal = 0;
    const int batchSize = 500;

    for (var i = 0; i < tokens.Count; i += batchSize)
    {
        var batch = tokens.Skip(i).Take(batchSize).ToList();
        var msg = new FirebaseAdmin.Messaging.MulticastMessage
        {
            Tokens = batch,
            Notification = new FirebaseAdmin.Messaging.Notification
            {
                Title = title,
                Body = body
            },
            Data = new Dictionary<string, string>
            {
                ["type"] = "update",
                ["action"] = "open_profile",
                ["versionCode"] = versionCode ?? ""
            }
        };

        var resp = await messaging.SendEachForMulticastAsync(msg);
        sentTotal += resp.SuccessCount;
    }

    return sentTotal;
}

static async Task<bool> UpsertDeviceAndDetectNewAsync(
    string connectionString,
    string login,
    string deviceId,
    string deviceName,
    string ip,
    string userAgent)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    const string ensureSql = @"
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_LoginDevices')
        CREATE TABLE [App_LoginDevices] (
            [Login] NVARCHAR(100) NOT NULL,
            [DeviceId] NVARCHAR(100) NOT NULL,
            [DeviceName] NVARCHAR(200) NULL,
            [FirstSeenAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
            [LastSeenAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
            [LastIp] NVARCHAR(100) NULL,
            [LastUserAgent] NVARCHAR(500) NULL,
            CONSTRAINT [PK_App_LoginDevices] PRIMARY KEY ([Login], [DeviceId])
        );";
    await using (var ensure = new SqlCommand(ensureSql, connection))
    {
        await ensure.ExecuteNonQueryAsync();
    }

    const string existsSql = @"SELECT 1 FROM [App_LoginDevices] WHERE [Login] = @Login AND [DeviceId] = @DeviceId;";
    await using (var exists = new SqlCommand(existsSql, connection))
    {
        exists.Parameters.AddWithValue("@Login", login);
        exists.Parameters.AddWithValue("@DeviceId", deviceId);
        var o = await exists.ExecuteScalarAsync();
        var isNew = o == null;
        if (isNew)
        {
            const string insertSql = @"
                INSERT INTO [App_LoginDevices] ([Login], [DeviceId], [DeviceName], [FirstSeenAt], [LastSeenAt], [LastIp], [LastUserAgent])
                VALUES (@Login, @DeviceId, @DeviceName, GETUTCDATE(), GETUTCDATE(), @Ip, @UA);";
            await using var ins = new SqlCommand(insertSql, connection);
            ins.Parameters.AddWithValue("@Login", login);
            ins.Parameters.AddWithValue("@DeviceId", deviceId);
            ins.Parameters.AddWithValue("@DeviceName", (object?)deviceName ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Ip", (object?)ip ?? DBNull.Value);
            ins.Parameters.AddWithValue("@UA", (object?)userAgent ?? DBNull.Value);
            await ins.ExecuteNonQueryAsync();
            return true;
        }
    }

    const string updateSql = @"
        UPDATE [App_LoginDevices]
        SET [LastSeenAt] = GETUTCDATE(),
            [DeviceName] = @DeviceName,
            [LastIp] = @Ip,
            [LastUserAgent] = @UA
        WHERE [Login] = @Login AND [DeviceId] = @DeviceId;";
    await using var upd = new SqlCommand(updateSql, connection);
    upd.Parameters.AddWithValue("@Login", login);
    upd.Parameters.AddWithValue("@DeviceId", deviceId);
    upd.Parameters.AddWithValue("@DeviceName", (object?)deviceName ?? DBNull.Value);
    upd.Parameters.AddWithValue("@Ip", (object?)ip ?? DBNull.Value);
    upd.Parameters.AddWithValue("@UA", (object?)userAgent ?? DBNull.Value);
    await upd.ExecuteNonQueryAsync();
    return false;
}

