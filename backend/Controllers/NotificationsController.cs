using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public NotificationsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<ActionResult<NotificationsResponse>> Get([FromQuery] string login, [FromQuery] int take = 50)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            return BadRequest(new NotificationsResponse(false, "Укажите логин", 0, null));
        }

        take = Math.Clamp(take, 1, 200);

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            return StatusCode(500, new NotificationsResponse(false, "Не настроено подключение к БД", 0, null));
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsureNotificationsTablesAsync(connection);

            const string sql = @"
                SELECT TOP (@Take)
                    N.[Id],
                    N.[Type],
                    N.[Title],
                    N.[Body],
                    N.[CreatedAt],
                    N.[Action],
                    N.[ActionData],
                    CASE WHEN R.[NotificationId] IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS IsRead
                FROM [App_Notifications] N
                LEFT JOIN [App_NotificationReads] R
                    ON R.[NotificationId] = N.[Id] AND R.[Login] = @Login
                WHERE N.[RecipientLogin] IS NULL OR N.[RecipientLogin] = @Login
                ORDER BY N.[CreatedAt] DESC;";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Login", login.Trim());
            cmd.Parameters.AddWithValue("@Take", take);

            await using var reader = await cmd.ExecuteReaderAsync();
            var items = new List<NotificationItem>();
            var unread = 0;
            while (await reader.ReadAsync())
            {
                var isRead = reader.GetBoolean(7);
                if (!isRead) unread++;
                items.Add(new NotificationItem(
                    Id: reader.GetInt32(0),
                    Type: reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Title: reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Body: reader.IsDBNull(3) ? "" : reader.GetString(3),
                    CreatedAt: reader.GetDateTime(4),
                    Action: reader.IsDBNull(5) ? null : reader.GetString(5),
                    ActionData: reader.IsDBNull(6) ? null : reader.GetString(6),
                    IsRead: isRead
                ));
            }

            return Ok(new NotificationsResponse(true, "OK", unread, items));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new NotificationsResponse(false, $"Ошибка: {ex.Message}", 0, null));
        }
    }

    [HttpPost("mark-read")]
    public async Task<ActionResult<MarkReadResponse>> MarkRead([FromBody] MarkReadRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login))
        {
            return BadRequest(new MarkReadResponse(false, "Укажите логин"));
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            return StatusCode(500, new MarkReadResponse(false, "Не настроено подключение к БД"));
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsureNotificationsTablesAsync(connection);

            
            const string sql = @"
                INSERT INTO [App_NotificationReads] ([NotificationId], [Login], [ReadAt])
                SELECT N.[Id], @Login, GETUTCDATE()
                FROM [App_Notifications] N
                LEFT JOIN [App_NotificationReads] R
                    ON R.[NotificationId] = N.[Id] AND R.[Login] = @Login
                WHERE (N.[RecipientLogin] IS NULL OR N.[RecipientLogin] = @Login)
                  AND R.[NotificationId] IS NULL;";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Login", request.Login.Trim());
            await cmd.ExecuteNonQueryAsync();

            return Ok(new MarkReadResponse(true, "OK"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new MarkReadResponse(false, $"Ошибка: {ex.Message}"));
        }
    }

    private static async Task EnsureNotificationsTablesAsync(SqlConnection connection)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_Notifications')
            CREATE TABLE [App_Notifications] (
                [Id] INT IDENTITY(1,1) PRIMARY KEY,
                [RecipientLogin] NVARCHAR(100) NULL, -- NULL = всем
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

        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }
}

public record NotificationItem(
    int Id,
    string Type,
    string Title,
    string Body,
    DateTime CreatedAt,
    string? Action,
    string? ActionData,
    bool IsRead
);

public record NotificationsResponse(bool Success, string Message, int UnreadCount, List<NotificationItem>? Items);

public record MarkReadRequest(string Login);

public record MarkReadResponse(bool Success, string Message);

