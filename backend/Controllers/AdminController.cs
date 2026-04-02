using EmployeeApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AdminController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("permissions")]
    public async Task<ActionResult<AdminActionResponse>> SetPermissions([FromBody] SetPermissionsRequest request)
    {
        if (!IsAuthorized()) return Unauthorized(new AdminActionResponse(false, "Unauthorized"));
        if (request == null || string.IsNullOrWhiteSpace(request.Login))
            return BadRequest(new AdminActionResponse(false, "Укажите login"));

        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, new AdminActionResponse(false, "Не настроено подключение к БД"));

        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        await EnsureUserPermissionsTableExistsAsync(conn);

        const string upsertSql = @"
            IF EXISTS (SELECT 1 FROM [App_UserPermissions] WHERE [Login] = @Login)
                UPDATE [App_UserPermissions]
                SET [CanCreatePosts] = @CanCreatePosts, [UpdatedAt] = GETUTCDATE()
                WHERE [Login] = @Login;
            ELSE
                INSERT INTO [App_UserPermissions] ([Login], [CanCreatePosts], [UpdatedAt])
                VALUES (@Login, @CanCreatePosts, GETUTCDATE());";
        await using (var cmd = new SqlCommand(upsertSql, conn))
        {
            cmd.Parameters.AddWithValue("@Login", request.Login.Trim());
            cmd.Parameters.AddWithValue("@CanCreatePosts", request.CanCreatePosts ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        var title = request.CanCreatePosts ? "Доступ выдан" : "Доступ отозван";
        var body = request.CanCreatePosts
            ? "Технический администратор выдал вам доступ к публикации новостей."
            : "Технический администратор отозвал у вас доступ к публикации новостей.";

        var id = await InsertNotificationAsync(cs, request.Login.Trim(), "permissions", title, body, "open_profile", null);
        if (FcmPush.IsConfigured())
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await FcmPush.SendToLoginAsync(cs, request.Login.Trim(), title, body, new Dictionary<string, string>
                    {
                        ["type"] = "permissions",
                        ["action"] = "open_profile",
                        ["notificationId"] = id.ToString()
                    });
                }
                catch { }
            });
        }

        return Ok(new AdminActionResponse(true, "OK"));
    }

    [HttpPost("notify/test")]
    public async Task<ActionResult<AdminActionResponse>> NotifyTest([FromBody] NotifyTestRequest request)
    {
        if (!IsAuthorized()) return Unauthorized(new AdminActionResponse(false, "Unauthorized"));
        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, new AdminActionResponse(false, "Не настроено подключение к БД"));

        var login = request?.Login?.Trim();
        if (!string.IsNullOrWhiteSpace(login))
        {
            var id = await InsertNotificationAsync(cs, login, "test", "Тестовое уведомление",
                $"Это тестовое уведомление для пользователя {login}.", "open_notifications", null);
            if (FcmPush.IsConfigured())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await FcmPush.SendToLoginAsync(cs, login, "Тестовое уведомление",
                            $"Это тестовое уведомление для пользователя {login}.",
                            new Dictionary<string, string> { ["type"] = "test", ["notificationId"] = id.ToString() });
                    }
                    catch { }
                });
            }
        }
        else
        {
            await InsertNotificationAsync(cs, null, "test", "Тестовое уведомление",
                "Это тестовое уведомление для всех пользователей.", "open_notifications", null);
            if (FcmPush.IsConfigured())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await FcmPush.SendBroadcastAsync(cs, "Тестовое уведомление", "Это тестовое уведомление для всех пользователей.",
                            new Dictionary<string, string> { ["type"] = "test" });
                    }
                    catch { }
                });
            }
        }

        return Ok(new AdminActionResponse(true, "OK"));
    }

    [HttpPost("notify/update")]
    public async Task<ActionResult<AdminActionResponse>> NotifyUpdate([FromBody] NotifyUpdateRequest request)
    {
        if (!IsAuthorized()) return Unauthorized(new AdminActionResponse(false, "Unauthorized"));
        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, new AdminActionResponse(false, "Не настроено подключение к БД"));

        var version = request?.VersionCode?.Trim() ?? "";
        var suffix = string.IsNullOrWhiteSpace(version) ? "" : $" (v{version})";
        var title = $"Доступно обновление приложения{suffix}";
        var body = "Вышла новая версия. Перейдите в Профиль и нажмите «Обновить приложение».";
        await InsertNotificationAsync(cs, null, "update", title, body, "open_profile", version);

        if (request?.SendPush == true && FcmPush.IsConfigured())
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await FcmPush.SendBroadcastAsync(cs, title, body, new Dictionary<string, string>
                    {
                        ["type"] = "update",
                        ["action"] = "open_profile",
                        ["actionData"] = version
                    });
                }
                catch { }
            });
        }

        return Ok(new AdminActionResponse(true, "OK"));
    }

    private bool IsAuthorized()
    {
        var expected = _configuration["Admin:Key"]?.Trim();
        if (string.IsNullOrWhiteSpace(expected)) return false;
        var provided = Request.Headers["X-Admin-Key"].ToString().Trim();
        return string.Equals(expected, provided, StringComparison.Ordinal);
    }

    private static async Task EnsureUserPermissionsTableExistsAsync(SqlConnection connection)
    {
        const string createSql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_UserPermissions')
            CREATE TABLE [App_UserPermissions] (
                [Login] NVARCHAR(100) NOT NULL PRIMARY KEY,
                [CanCreatePosts] BIT NOT NULL DEFAULT 0,
                [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );";
        await using var cmd = new SqlCommand(createSql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> InsertNotificationAsync(
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
            await ensure.ExecuteNonQueryAsync();

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
        var o = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(o ?? 0);
    }
}

public record SetPermissionsRequest(string Login, bool CanCreatePosts);
public record NotifyTestRequest(string? Login);
public record NotifyUpdateRequest(string? VersionCode, bool SendPush);
public record AdminActionResponse(bool Success, string Message);

