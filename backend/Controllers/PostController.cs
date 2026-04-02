using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Globalization;
using EmployeeApi.Services;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PostController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    public PostController(IConfiguration configuration, IWebHostEnvironment env)
    {
        _configuration = configuration;
        _env = env;
    }

    [HttpPost]
    public async Task<ActionResult<CreatePostResponse>> Create([FromBody] CreatePostRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new CreatePostResponse(false, "Текст поста не может быть пустым", null));
        }

        if (string.IsNullOrWhiteSpace(request.AuthorLogin))
        {
            return BadRequest(new CreatePostResponse(false, "Укажите автора (логин)", null));
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            return StatusCode(500, new CreatePostResponse(false, "Не настроено подключение к БД", null));
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsurePostsTableExistsAsync(connection);
            await EnsureUserPermissionsTableExistsAsync(connection);

            var allowed = await CanCreatePostsAsync(connection, request.AuthorLogin);
            if (!allowed)
            {
                return StatusCode(403, new CreatePostResponse(false, "Нет прав на создание новостей", null));
            }

            var authorName = await GetAuthorNameAsync(connection, request.AuthorLogin);
            var isImportant = request.IsImportant ?? false;
            DateTime? expiresAt = isImportant ? null : DateTime.UtcNow.AddDays(7);

            const string sql = @"
                INSERT INTO [App_Posts] ([AuthorLogin], [AuthorName], [Content], [CreatedAt], [IsImportant], [ExpiresAt], [LikesCount], [CommentsCount])
                VALUES (@AuthorLogin, @AuthorName, @Content, GETUTCDATE(), @IsImportant, @ExpiresAt, 0, 0);
                SELECT SCOPE_IDENTITY();";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@AuthorLogin", request.AuthorLogin);
            cmd.Parameters.AddWithValue("@AuthorName", authorName ?? request.AuthorLogin);
            cmd.Parameters.AddWithValue("@Content", request.Content.Trim());
            cmd.Parameters.AddWithValue("@IsImportant", isImportant);
            cmd.Parameters.AddWithValue("@ExpiresAt", (object?)expiresAt ?? DBNull.Value);

            var newId = await cmd.ExecuteScalarAsync();
            var id = Convert.ToInt32(newId);

            var created = new PostItem(
                Id: id,
                AuthorLogin: request.AuthorLogin,
                AuthorName: authorName ?? request.AuthorLogin,
                Content: request.Content.Trim(),
                CreatedAt: DateTime.UtcNow,
                ImageUrl: null,
                IsImportant: isImportant,
                ExpiresAt: expiresAt,
                LikesCount: 0,
                CommentsCount: 0);

            await EnsureNotificationsTablesAsync(connection);
            await CreateBroadcastNotificationAsync(connection,
                title: "Новая новость",
                body: $"Опубликован новый пост от {(authorName ?? request.AuthorLogin)}",
                action: "open_feed",
                actionData: null);

            
            if (FcmPush.IsConfigured())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await FcmPush.SendBroadcastAsync(
                            connectionString,
                            "Новая новость",
                            $"Опубликован новый пост от {(authorName ?? request.AuthorLogin)}",
                            new Dictionary<string, string>
                            {
                                ["type"] = "post",
                                ["action"] = "open_feed"
                            });
                    }
                    catch
                    {
                        
                    }
                });
            }

            return Ok(new CreatePostResponse(true, "Пост опубликован", created));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new CreatePostResponse(false, $"Ошибка: {ex.Message}", null));
        }
    }

    [HttpPost("media")]
    [RequestSizeLimit(25_000_000)] 
    public async Task<ActionResult<CreatePostResponse>> CreateWithMedia(
        [FromForm] string? content,
        [FromForm] string? authorLogin,
        [FromForm] bool? isImportant,
        [FromForm] IFormFile? media)
    {
        Console.WriteLine(
            $"POST /api/post/media: contentLen={(content?.Length ?? 0)} authorLogin='{authorLogin ?? ""}' isImportant='{isImportant?.ToString() ?? "null"}' mediaNull={(media == null ? "yes" : "no")}");
        if (media != null)
        {
            Console.WriteLine(
                $"POST /api/post/media: media.FileName='{media.FileName}', media.ContentType='{media.ContentType}', media.Length={media.Length}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return BadRequest(new CreatePostResponse(false, "Текст поста не может быть пустым", null));
        }

        if (string.IsNullOrWhiteSpace(authorLogin))
        {
            return BadRequest(new CreatePostResponse(false, "Укажите автора (логин)", null));
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            return StatusCode(500, new CreatePostResponse(false, "Не настроено подключение к БД", null));
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsurePostsTableExistsAsync(connection);
            await EnsureUserPermissionsTableExistsAsync(connection);

            var allowed = await CanCreatePostsAsync(connection, authorLogin);
            if (!allowed)
            {
                return StatusCode(403, new CreatePostResponse(false, "Нет прав на создание новостей", null));
            }

            var authorName = await GetAuthorNameAsync(connection, authorLogin);
            var imageUrl = media != null ? await SaveMediaAsync(media) : null;
            var importantFlag = isImportant ?? false;
            DateTime? expiresAt = importantFlag ? null : DateTime.UtcNow.AddDays(7);

            const string sql = @"
                INSERT INTO [App_Posts] ([AuthorLogin], [AuthorName], [Content], [CreatedAt], [ImageUrl], [IsImportant], [ExpiresAt], [LikesCount], [CommentsCount])
                VALUES (@AuthorLogin, @AuthorName, @Content, GETUTCDATE(), @ImageUrl, @IsImportant, @ExpiresAt, 0, 0);
                SELECT SCOPE_IDENTITY();";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@AuthorLogin", authorLogin);
            cmd.Parameters.AddWithValue("@AuthorName", authorName ?? authorLogin);
            cmd.Parameters.AddWithValue("@Content", content.Trim());
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)imageUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsImportant", importantFlag);
            cmd.Parameters.AddWithValue("@ExpiresAt", (object?)expiresAt ?? DBNull.Value);

            var newId = await cmd.ExecuteScalarAsync();
            var id = Convert.ToInt32(newId, CultureInfo.InvariantCulture);

            var created = new PostItem(
                Id: id,
                AuthorLogin: authorLogin,
                AuthorName: authorName ?? authorLogin,
                Content: content.Trim(),
                CreatedAt: DateTime.UtcNow,
                ImageUrl: imageUrl,
                IsImportant: importantFlag,
                ExpiresAt: expiresAt,
                LikesCount: 0,
                CommentsCount: 0);

            await EnsureNotificationsTablesAsync(connection);
            await CreateBroadcastNotificationAsync(connection,
                title: "Новая новость",
                body: $"Опубликован новый пост от {(authorName ?? authorLogin)}",
                action: "open_feed",
                actionData: null);

            
            if (FcmPush.IsConfigured())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await FcmPush.SendBroadcastAsync(
                            connectionString,
                            "Новая новость",
                            $"Опубликован новый пост от {(authorName ?? authorLogin)}",
                            new Dictionary<string, string>
                            {
                                ["type"] = "post",
                                ["action"] = "open_feed"
                            });
                    }
                    catch
                    {
                        
                    }
                });
            }

            return Ok(new CreatePostResponse(true, "Пост опубликован", created));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new CreatePostResponse(false, $"Ошибка: {ex.Message}", null));
        }
    }

    [HttpGet("feed")]
    public async Task<ActionResult<FeedResponse>> GetFeed()
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            return StatusCode(500, new FeedResponse(false, "Не настроено подключение к БД", null));
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsurePostsTableExistsAsync(connection);

            
            const string cleanupSql = @"
                DELETE FROM [App_Posts]
                WHERE [ExpiresAt] IS NOT NULL AND [ExpiresAt] <= GETUTCDATE();";
            await using (var cleanup = new SqlCommand(cleanupSql, connection))
            {
                await cleanup.ExecuteNonQueryAsync();
            }

            const string sql = @"
                SELECT [Id], [AuthorLogin], [AuthorName], [Content], [CreatedAt], [ImageUrl], [IsImportant], [ExpiresAt], [LikesCount], [CommentsCount]
                FROM [App_Posts]
                WHERE [ExpiresAt] IS NULL OR [ExpiresAt] > GETUTCDATE()
                ORDER BY [CreatedAt] DESC;";

            await using var cmd = new SqlCommand(sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            var posts = new List<PostItem>();
            while (await reader.ReadAsync())
            {
                posts.Add(new PostItem(
                    Id: reader.GetInt32(0),
                    AuthorLogin: reader.GetString(1),
                    AuthorName: reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Content: reader.GetString(3),
                    CreatedAt: reader.GetDateTime(4),
                    ImageUrl: reader.IsDBNull(5) ? null : reader.GetString(5),
                    IsImportant: !reader.IsDBNull(6) && reader.GetBoolean(6),
                    ExpiresAt: reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    LikesCount: reader.GetInt32(8),
                    CommentsCount: reader.GetInt32(9)));
            }

            return Ok(new FeedResponse(true, "OK", posts));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new FeedResponse(false, $"Ошибка: {ex.Message}", null));
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<DeletePostResponse>> DeletePost(int id, [FromQuery] string login)
    {
        if (id <= 0)
            return BadRequest(new DeletePostResponse(false, "Некорректный id новости"));

        if (string.IsNullOrWhiteSpace(login))
            return BadRequest(new DeletePostResponse(false, "Укажите login"));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return StatusCode(500, new DeletePostResponse(false, "Не настроено подключение к БД"));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsurePostsTableExistsAsync(connection);
            await EnsureUserPermissionsTableExistsAsync(connection);

            var canDelete = await CanCreatePostsAsync(connection, login.Trim());
            if (!canDelete)
                return StatusCode(403, new DeletePostResponse(false, "Нет прав на удаление новостей"));

            const string sql = @"
                DELETE FROM [App_Posts]
                WHERE [Id] = @Id;
                SELECT @@ROWCOUNT;";
            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Id", id);
            var rowObj = await cmd.ExecuteScalarAsync();
            var rows = Convert.ToInt32(rowObj ?? 0);

            if (rows <= 0)
                return Ok(new DeletePostResponse(false, "Новость не найдена"));

            return Ok(new DeletePostResponse(true, "Новость удалена"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new DeletePostResponse(false, $"Ошибка: {ex.Message}"));
        }
    }

    private static async Task EnsurePostsTableExistsAsync(SqlConnection connection)
    {
        const string createSql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_Posts')
            CREATE TABLE [App_Posts] (
                [Id] INT IDENTITY(1,1) PRIMARY KEY,
                [AuthorLogin] NVARCHAR(100) NOT NULL,
                [AuthorName] NVARCHAR(200) NOT NULL,
                [Content] NVARCHAR(MAX) NOT NULL,
                [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                [ImageUrl] NVARCHAR(500) NULL,
                [IsImportant] BIT NOT NULL DEFAULT 0,
                [ExpiresAt] DATETIME2 NULL,
                [LikesCount] INT NOT NULL DEFAULT 0,
                [CommentsCount] INT NOT NULL DEFAULT 0
            );

            IF COL_LENGTH('App_Posts', 'IsImportant') IS NULL
                ALTER TABLE [App_Posts] ADD [IsImportant] BIT NOT NULL CONSTRAINT [DF_App_Posts_IsImportant] DEFAULT 0;

            IF COL_LENGTH('App_Posts', 'ExpiresAt') IS NULL
                ALTER TABLE [App_Posts] ADD [ExpiresAt] DATETIME2 NULL;";

        await using var cmd = new SqlCommand(createSql, connection);
        await cmd.ExecuteNonQueryAsync();
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

    private static async Task<bool> CanCreatePostsAsync(SqlConnection connection, string login)
    {
        const string sql = @"
            SELECT TOP 1 ISNULL([CanCreatePosts], 0)
            FROM [App_UserPermissions]
            WHERE [Login] = @Login;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        var o = await cmd.ExecuteScalarAsync();
        if (o == null || o == DBNull.Value) return false;
        return Convert.ToInt32(o) == 1;
    }

    private static async Task EnsureNotificationsTablesAsync(SqlConnection connection)
    {
        const string sql = @"
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
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task CreateBroadcastNotificationAsync(SqlConnection connection, string title, string body, string? action, string? actionData)
    {
        const string sql = @"
            INSERT INTO [App_Notifications] ([RecipientLogin], [Type], [Title], [Body], [CreatedAt], [Action], [ActionData])
            VALUES (NULL, 'post', @Title, @Body, GETUTCDATE(), @Action, @ActionData);";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Title", title);
        cmd.Parameters.AddWithValue("@Body", (object?)body ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Action", (object?)action ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ActionData", (object?)actionData ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string?> GetAuthorNameAsync(SqlConnection connection, string login)
    {
        try
        {
            const string sql = @"
                SELECT TOP 1 COALESCE(TRY_CONVERT(nvarchar(200), [Фамилия]), '') + ' ' + COALESCE(TRY_CONVERT(nvarchar(200), [Имя]), '')
                FROM [Lexema_Кадры_ЛичнаяКарточка]
                WHERE [Логин] = @Login;";
            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Login", login);
            var o = await cmd.ExecuteScalarAsync();
            if (o != null && o != DBNull.Value)
            {
                var s = o.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        catch
        {
            
        }

        return null;
    }

    private async Task<string> SaveMediaAsync(IFormFile media)
    {
        var uploadsRoot = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "uploads");
        Directory.CreateDirectory(uploadsRoot);

        var ext = Path.GetExtension(media.FileName);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 10) ext = ".bin";

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadsRoot, fileName);

        await using (var fs = System.IO.File.Create(fullPath))
        {
            await media.CopyToAsync(fs);
        }

        
        var baseUrl = $"{Request.Scheme}://{Request.Host.Value}".TrimEnd('/');
        return $"{baseUrl}/uploads/{fileName}";
    }
}

public record CreatePostRequest(string Content, string AuthorLogin, bool? IsImportant);

public record PostItem(
    int Id,
    string AuthorLogin,
    string AuthorName,
    string Content,
    DateTime CreatedAt,
    string? ImageUrl,
    bool IsImportant,
    DateTime? ExpiresAt,
    int LikesCount,
    int CommentsCount);

public record CreatePostResponse(bool Success, string Message, PostItem? Post);

public record FeedResponse(bool Success, string Message, List<PostItem>? Posts);
public record DeletePostResponse(bool Success, string Message);
