using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Text.Json;
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
            await EnsurePostPollTablesExistsAsync(connection);
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
            var normalizedPoll = NormalizePoll(request.Poll);
            if (request.Poll != null && normalizedPoll == null)
            {
                return BadRequest(new CreatePostResponse(false, "Некорректный опрос: минимум 2 разных варианта ответа", null));
            }
            if (normalizedPoll != null)
            {
                await SavePollAsync(connection, id, normalizedPoll);
            }

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
                CommentsCount: 0,
                Poll: normalizedPoll == null ? null : ToPollItem(normalizedPoll));

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
        [FromForm] string? pollJson,
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
            await EnsurePostPollTablesExistsAsync(connection);
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
            var normalizedPoll = ParseAndNormalizePollOrNull(pollJson);
            if (!string.IsNullOrWhiteSpace(pollJson) && normalizedPoll == null)
            {
                return BadRequest(new CreatePostResponse(false, "Некорректный опрос: минимум 2 разных варианта ответа", null));
            }
            if (normalizedPoll != null)
            {
                await SavePollAsync(connection, id, normalizedPoll);
            }

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
                CommentsCount: 0,
                Poll: normalizedPoll == null ? null : ToPollItem(normalizedPoll));

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
    public async Task<ActionResult<FeedResponse>> GetFeed([FromQuery] string? login)
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
            await EnsurePostPollTablesExistsAsync(connection);

            
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

            var posts = new List<PostItem>();
            await using (var cmd = new SqlCommand(sql, connection))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
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
                        CommentsCount: reader.GetInt32(9),
                        Poll: null));
                }
            }

            for (var i = 0; i < posts.Count; i++)
            {
                var p = posts[i];
                var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
                posts[i] = p with
                {
                    Poll = await GetPollForPostAsync(connection, p.Id, login, p.AuthorLogin, baseUrl)
                };
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

    [HttpPost("{id:int}/vote")]
    public async Task<ActionResult<VoteResponse>> Vote(int id, [FromBody] VoteRequest request)
    {
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Login) || request.OptionId <= 0)
        {
            return BadRequest(new VoteResponse(false, "Некорректные данные"));
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            return StatusCode(500, new VoteResponse(false, "Не настроено подключение к БД"));
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await EnsurePostPollTablesExistsAsync(connection);

            const string pollSql = @"
                SELECT TOP 1 [AllowRevote], [EndsAtUtc]
                FROM [App_PostPolls]
                WHERE [PostId] = @PostId;";
            bool allowRevote;
            DateTime? endsAt;
            await using (var cmd = new SqlCommand(pollSql, connection))
            {
                cmd.Parameters.AddWithValue("@PostId", id);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return Ok(new VoteResponse(false, "Опрос не найден"));
                }
                allowRevote = !reader.IsDBNull(0) && reader.GetBoolean(0);
                endsAt = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
            }

            if (endsAt.HasValue && endsAt.Value <= DateTime.UtcNow)
            {
                return Ok(new VoteResponse(false, "Срок опроса истек"));
            }

            const string optionExistsSql = @"SELECT TOP 1 1 FROM [App_PostPollOptions] WHERE [Id] = @OptionId AND [PostId] = @PostId;";
            await using (var check = new SqlCommand(optionExistsSql, connection))
            {
                check.Parameters.AddWithValue("@OptionId", request.OptionId);
                check.Parameters.AddWithValue("@PostId", id);
                var exists = await check.ExecuteScalarAsync();
                if (exists == null)
                {
                    return Ok(new VoteResponse(false, "Вариант не найден"));
                }
            }

            const string hasVoteSql = @"SELECT TOP 1 [OptionId] FROM [App_PostPollVotes] WHERE [PostId] = @PostId AND [Login] = @Login;";
            int? existingOptionId = null;
            await using (var hasVote = new SqlCommand(hasVoteSql, connection))
            {
                hasVote.Parameters.AddWithValue("@PostId", id);
                hasVote.Parameters.AddWithValue("@Login", request.Login.Trim());
                var o = await hasVote.ExecuteScalarAsync();
                if (o != null && o != DBNull.Value) existingOptionId = Convert.ToInt32(o);
            }

            if (existingOptionId.HasValue && !allowRevote)
            {
                return Ok(new VoteResponse(false, "Переголосование отключено"));
            }

            const string upsertSql = @"
                IF EXISTS (SELECT 1 FROM [App_PostPollVotes] WHERE [PostId] = @PostId AND [Login] = @Login)
                    UPDATE [App_PostPollVotes]
                    SET [OptionId] = @OptionId, [UpdatedAt] = GETUTCDATE()
                    WHERE [PostId] = @PostId AND [Login] = @Login
                ELSE
                    INSERT INTO [App_PostPollVotes] ([PostId], [OptionId], [Login], [CreatedAt], [UpdatedAt])
                    VALUES (@PostId, @OptionId, @Login, GETUTCDATE(), GETUTCDATE());";
            await using (var upsert = new SqlCommand(upsertSql, connection))
            {
                upsert.Parameters.AddWithValue("@PostId", id);
                upsert.Parameters.AddWithValue("@OptionId", request.OptionId);
                upsert.Parameters.AddWithValue("@Login", request.Login.Trim());
                await upsert.ExecuteNonQueryAsync();
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
            var pollForViewer = await GetPollForPostAsync(connection, id, request.Login.Trim(), await GetAuthorLoginByPostIdAsync(connection, id), baseUrl);
            return Ok(new VoteResponse(true, "Голос учтен", pollForViewer));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new VoteResponse(false, $"Ошибка: {ex.Message}"));
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

    private static async Task EnsurePostPollTablesExistsAsync(SqlConnection connection)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_PostPolls')
            CREATE TABLE [App_PostPolls] (
                [PostId] INT NOT NULL PRIMARY KEY,
                [Question] NVARCHAR(300) NOT NULL,
                [Description] NVARCHAR(1000) NULL,
                [AllowMediaInQuestionAndOptions] BIT NOT NULL DEFAULT 0,
                [ShowVoters] BIT NOT NULL DEFAULT 0,
                [AllowRevote] BIT NOT NULL DEFAULT 1,
                [ShuffleOptions] BIT NOT NULL DEFAULT 0,
                [EndsAtUtc] DATETIME2 NULL,
                [HideResultsUntilEnd] BIT NOT NULL DEFAULT 0,
                [CreatorCanViewWithoutVoting] BIT NOT NULL DEFAULT 1,
                [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_PostPollOptions')
            CREATE TABLE [App_PostPollOptions] (
                [Id] INT IDENTITY(1,1) PRIMARY KEY,
                [PostId] INT NOT NULL,
                [OptionText] NVARCHAR(300) NOT NULL,
                [SortOrder] INT NOT NULL
            );

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_PostPollVotes')
            CREATE TABLE [App_PostPollVotes] (
                [PostId] INT NOT NULL,
                [OptionId] INT NOT NULL,
                [Login] NVARCHAR(100) NOT NULL,
                [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT [PK_App_PostPollVotes] PRIMARY KEY ([PostId], [Login])
            );";
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private static PollCreateRequest? ParseAndNormalizePollOrNull(string? pollJson)
    {
        if (string.IsNullOrWhiteSpace(pollJson)) return null;
        try
        {
            var parsed = JsonSerializer.Deserialize<PollCreateRequest>(pollJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return NormalizePoll(parsed);
        }
        catch
        {
            return null;
        }
    }

    private static PollCreateRequest? NormalizePoll(PollCreateRequest? raw)
    {
        if (raw == null || string.IsNullOrWhiteSpace(raw.Question)) return null;
        var options = (raw.Options ?? new List<string>())
            .Select(x => (x ?? "").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(20)
            .ToList();
        var uniqueCount = options.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (uniqueCount < 2) return null;
        return raw with
        {
            Question = raw.Question.Trim(),
            Description = string.IsNullOrWhiteSpace(raw.Description) ? null : raw.Description.Trim(),
            Options = options
        };
    }

    private static PollItem ToPollItem(PollCreateRequest p) => new(
        Question: p.Question,
        Description: p.Description,
        Options: p.Options.Select((x, i) => new PollOptionItem(i + 1, x, 0, null)).ToList(),
        AllowMediaInQuestionAndOptions: p.AllowMediaInQuestionAndOptions,
        ShowVoters: p.ShowVoters,
        AllowRevote: p.AllowRevote,
        ShuffleOptions: p.ShuffleOptions,
        EndsAtUtc: p.EndsAtUtc,
        HideResultsUntilEnd: p.HideResultsUntilEnd,
        CreatorCanViewWithoutVoting: p.CreatorCanViewWithoutVoting,
        TotalVotes: 0,
        HasVoted: false,
        SelectedOptionId: null,
        CanViewResults: true);

    private static async Task SavePollAsync(SqlConnection connection, int postId, PollCreateRequest poll)
    {
        const string insertPollSql = @"
            INSERT INTO [App_PostPolls]
            ([PostId], [Question], [Description], [AllowMediaInQuestionAndOptions], [ShowVoters], [AllowRevote], [ShuffleOptions], [EndsAtUtc], [HideResultsUntilEnd], [CreatorCanViewWithoutVoting], [CreatedAt])
            VALUES
            (@PostId, @Question, @Description, @AllowMedia, @ShowVoters, @AllowRevote, @ShuffleOptions, @EndsAtUtc, @HideResults, @CreatorCanView, GETUTCDATE());";
        await using (var cmd = new SqlCommand(insertPollSql, connection))
        {
            cmd.Parameters.AddWithValue("@PostId", postId);
            cmd.Parameters.AddWithValue("@Question", poll.Question);
            cmd.Parameters.AddWithValue("@Description", (object?)poll.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AllowMedia", poll.AllowMediaInQuestionAndOptions);
            cmd.Parameters.AddWithValue("@ShowVoters", poll.ShowVoters);
            cmd.Parameters.AddWithValue("@AllowRevote", poll.AllowRevote);
            cmd.Parameters.AddWithValue("@ShuffleOptions", poll.ShuffleOptions);
            cmd.Parameters.AddWithValue("@EndsAtUtc", (object?)poll.EndsAtUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@HideResults", poll.HideResultsUntilEnd);
            cmd.Parameters.AddWithValue("@CreatorCanView", poll.CreatorCanViewWithoutVoting);
            await cmd.ExecuteNonQueryAsync();
        }

        const string insertOptionSql = @"
            INSERT INTO [App_PostPollOptions] ([PostId], [OptionText], [SortOrder])
            VALUES (@PostId, @OptionText, @SortOrder);";
        for (var i = 0; i < poll.Options.Count; i++)
        {
            await using var cmd = new SqlCommand(insertOptionSql, connection);
            cmd.Parameters.AddWithValue("@PostId", postId);
            cmd.Parameters.AddWithValue("@OptionText", poll.Options[i]);
            cmd.Parameters.AddWithValue("@SortOrder", i);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<PollItem?> GetPollForPostAsync(SqlConnection connection, int postId, string? viewerLogin, string authorLogin, string? baseUrl)
    {
        const string pollSql = @"
            SELECT TOP 1
                [Question], [Description], [AllowMediaInQuestionAndOptions], [ShowVoters], [AllowRevote],
                [ShuffleOptions], [EndsAtUtc], [HideResultsUntilEnd], [CreatorCanViewWithoutVoting]
            FROM [App_PostPolls]
            WHERE [PostId] = @PostId;";
        string question;
        string? description;
        bool allowMedia;
        bool showVoters;
        bool allowRevote;
        bool shuffleOptions;
        DateTime? endsAt;
        bool hideResultsUntilEnd;
        bool creatorCanViewWithoutVoting;
        await using (var pollCmd = new SqlCommand(pollSql, connection))
        {
            pollCmd.Parameters.AddWithValue("@PostId", postId);
            await using var reader = await pollCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            question = reader.IsDBNull(0) ? "" : reader.GetString(0);
            description = reader.IsDBNull(1) ? null : reader.GetString(1);
            allowMedia = !reader.IsDBNull(2) && reader.GetBoolean(2);
            showVoters = !reader.IsDBNull(3) && reader.GetBoolean(3);
            allowRevote = reader.IsDBNull(4) || reader.GetBoolean(4);
            shuffleOptions = !reader.IsDBNull(5) && reader.GetBoolean(5);
            endsAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6);
            hideResultsUntilEnd = !reader.IsDBNull(7) && reader.GetBoolean(7);
            creatorCanViewWithoutVoting = reader.IsDBNull(8) || reader.GetBoolean(8);
        }

        var normalizedLogin = viewerLogin?.Trim() ?? "";
        var isCreator = !string.IsNullOrWhiteSpace(normalizedLogin) &&
                        string.Equals(normalizedLogin, authorLogin, StringComparison.OrdinalIgnoreCase);
        var hasEnded = !endsAt.HasValue || endsAt.Value <= DateTime.UtcNow;

        int? selectedOptionId = null;
        if (!string.IsNullOrWhiteSpace(normalizedLogin))
        {
            const string selectedSql = @"SELECT TOP 1 [OptionId] FROM [App_PostPollVotes] WHERE [PostId] = @PostId AND [Login] = @Login;";
            await using var selectedCmd = new SqlCommand(selectedSql, connection);
            selectedCmd.Parameters.AddWithValue("@PostId", postId);
            selectedCmd.Parameters.AddWithValue("@Login", normalizedLogin);
            var o = await selectedCmd.ExecuteScalarAsync();
            if (o != null && o != DBNull.Value) selectedOptionId = Convert.ToInt32(o);
        }

        var hasVoted = selectedOptionId.HasValue;
        var canViewResults = !hideResultsUntilEnd || hasEnded || hasVoted || (isCreator && creatorCanViewWithoutVoting);

        var options = new List<PollOptionItem>();
        const string optionsSql = @"
            SELECT O.[Id], O.[OptionText], O.[SortOrder],
                   ISNULL(V.Cnt, 0) AS VotesCount
            FROM [App_PostPollOptions] O
            OUTER APPLY (
                SELECT COUNT(1) AS Cnt
                FROM [App_PostPollVotes] VV
                WHERE VV.[PostId] = O.[PostId] AND VV.[OptionId] = O.[Id]
            ) V
            WHERE O.[PostId] = @PostId
            ORDER BY O.[SortOrder], O.[Id];";
        await using (var optionsCmd = new SqlCommand(optionsSql, connection))
        {
            optionsCmd.Parameters.AddWithValue("@PostId", postId);
            await using var reader = await optionsCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var optionId = reader.GetInt32(0);
                var text = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var votesCount = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3));
                options.Add(new PollOptionItem(
                    Id: optionId,
                    Text: text,
                    VotesCount: canViewResults ? votesCount : 0,
                    Voters: null
                ));
            }
        }

        if (shuffleOptions && !string.IsNullOrWhiteSpace(normalizedLogin))
        {
            var seed = HashCode.Combine(postId, normalizedLogin.ToLowerInvariant());
            var rng = new Random(seed);
            options = options.OrderBy(_ => rng.Next()).ToList();
        }

        if (showVoters && canViewResults)
        {
            for (var i = 0; i < options.Count; i++)
            {
                const string votersSql = @"
                    SELECT TOP 50
                        V.[Login],
                        P.[AvatarFileName]
                    FROM [App_PostPollVotes] V
                    LEFT JOIN [App_UserProfile] P ON P.[Login] = V.[Login]
                    WHERE V.[PostId] = @PostId AND V.[OptionId] = @OptionId
                    ORDER BY V.[UpdatedAt] DESC;";
                await using var votersCmd = new SqlCommand(votersSql, connection);
                votersCmd.Parameters.AddWithValue("@PostId", postId);
                votersCmd.Parameters.AddWithValue("@OptionId", options[i].Id);
                var voters = new List<PollVoterItem>();
                await using var r = await votersCmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var login = r.IsDBNull(0) ? "" : r.GetString(0);
                    var avatarFileName = r.IsDBNull(1) ? null : r.GetString(1);
                    string? avatarUrl = null;
                    if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(avatarFileName))
                    {
                        avatarUrl = $"{baseUrl}/uploads/avatars/{Uri.EscapeDataString(avatarFileName)}";
                    }
                    if (!string.IsNullOrWhiteSpace(login))
                    {
                        voters.Add(new PollVoterItem(login, avatarUrl));
                    }
                }
                options[i] = options[i] with { Voters = voters };
            }
        }

        var totalVotes = canViewResults ? options.Sum(x => x.VotesCount) : 0;
        return new PollItem(
            Question: question,
            Description: description,
            Options: options,
            AllowMediaInQuestionAndOptions: allowMedia,
            ShowVoters: showVoters,
            AllowRevote: allowRevote,
            ShuffleOptions: shuffleOptions,
            EndsAtUtc: endsAt,
            HideResultsUntilEnd: hideResultsUntilEnd,
            CreatorCanViewWithoutVoting: creatorCanViewWithoutVoting,
            TotalVotes: totalVotes,
            HasVoted: hasVoted,
            SelectedOptionId: selectedOptionId,
            CanViewResults: canViewResults
        );
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

    private static async Task<string> GetAuthorLoginByPostIdAsync(SqlConnection connection, int postId)
    {
        const string sql = @"SELECT TOP 1 [AuthorLogin] FROM [App_Posts] WHERE [Id] = @PostId;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@PostId", postId);
        var o = await cmd.ExecuteScalarAsync();
        return o?.ToString()?.Trim() ?? string.Empty;
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

public record CreatePostRequest(string Content, string AuthorLogin, bool? IsImportant, PollCreateRequest? Poll);

public record PollCreateRequest(
    string Question,
    string? Description,
    List<string> Options,
    bool AllowMediaInQuestionAndOptions,
    bool ShowVoters,
    bool AllowRevote,
    bool ShuffleOptions,
    DateTime? EndsAtUtc,
    bool HideResultsUntilEnd,
    bool CreatorCanViewWithoutVoting);

public record PollItem(
    string Question,
    string? Description,
    List<PollOptionItem> Options,
    bool AllowMediaInQuestionAndOptions,
    bool ShowVoters,
    bool AllowRevote,
    bool ShuffleOptions,
    DateTime? EndsAtUtc,
    bool HideResultsUntilEnd,
    bool CreatorCanViewWithoutVoting,
    int TotalVotes,
    bool HasVoted,
    int? SelectedOptionId,
    bool CanViewResults);

public record PollOptionItem(
    int Id,
    string Text,
    int VotesCount,
    List<PollVoterItem>? Voters);

public record PollVoterItem(
    string Login,
    string? AvatarUrl);

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
    int CommentsCount,
    PollItem? Poll);

public record CreatePostResponse(bool Success, string Message, PostItem? Post);

public record FeedResponse(bool Success, string Message, List<PostItem>? Posts);
public record DeletePostResponse(bool Success, string Message);
public record VoteRequest(string Login, int OptionId);
public record VoteResponse(bool Success, string Message, PollItem? Poll = null);
