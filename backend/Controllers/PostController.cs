using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Text.Json;
using EmployeeApi.Services;
using EmployeeApi.Services.Coins;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PostController : ControllerBase
{
    private const int MaxPostTextLength = 3333;
    private const int MaxPostImageCount = 15;
    private const int MaxPostVideoCount = 5;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ICoinsService _coinsService;

    public PostController(IConfiguration configuration, IWebHostEnvironment env, ICoinsService coinsService)
    {
        _configuration = configuration;
        _env = env;
        _coinsService = coinsService;
    }

    [HttpPost]
    public async Task<ActionResult<CreatePostResponse>> Create([FromBody] CreatePostRequest request)
    {
        var normalizedContent = request?.Content?.Trim() ?? string.Empty;
        var normalizedPoll = NormalizePoll(request?.Poll);
        var hasPoll = normalizedPoll != null;
        var hasContent = !string.IsNullOrWhiteSpace(normalizedContent);
        if (request == null || (!hasContent && !hasPoll))
        {
            return BadRequest(new CreatePostResponse(false, "Добавьте текст или опрос", null));
        }
        if (normalizedContent.Length > MaxPostTextLength)
        {
            return BadRequest(new CreatePostResponse(false, $"Максимум {MaxPostTextLength} символа(ов) в тексте новости", null));
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
            await EnsurePostMediaTableExistsAsync(connection);
            await EnsurePostEventTablesExistsAsync(connection);
            await EnsureUserPermissionsTableExistsAsync(connection);

            var allowed = await CanCreatePostsAsync(connection, request.AuthorLogin);
            if (!allowed)
            {
                return StatusCode(403, new CreatePostResponse(false, "Нет прав на создание новостей", null));
            }

            var authorName = await GetAuthorNameAsync(connection, request.AuthorLogin);
            var isImportant = request.IsImportant ?? false;
            var isEvent = request.IsEvent ?? false;
            var eventCoinReward = isEvent ? Math.Clamp(request.EventCoinReward ?? 0, 0, 100000) : 0;
            var isTechAdmin = await IsTechAdminAsync(connection, request.AuthorLogin);
            var eventGrantDelayDays = 0;
            if (isEvent)
            {
                var requestedDelay = request.EventGrantDelayDays ?? 2;
                if (requestedDelay != 1 && requestedDelay != 2) requestedDelay = 2;
                eventGrantDelayDays = (isTechAdmin && (request.EventGrantInstant ?? false)) ? 0 : requestedDelay;
            }
            DateTime? expiresAt = isImportant ? null : DateTime.UtcNow.AddDays(7);

            const string sql = @"
                INSERT INTO [App_Posts] ([AuthorLogin], [AuthorName], [Content], [CreatedAt], [IsImportant], [ExpiresAt], [LikesCount], [CommentsCount], [IsEvent], [EventCoinReward], [EventGrantDelayDays])
                VALUES (@AuthorLogin, @AuthorName, @Content, GETUTCDATE(), @IsImportant, @ExpiresAt, 0, 0, @IsEvent, @EventCoinReward, @EventGrantDelayDays);
                SELECT SCOPE_IDENTITY();";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@AuthorLogin", request.AuthorLogin);
            cmd.Parameters.AddWithValue("@AuthorName", authorName ?? request.AuthorLogin);
            cmd.Parameters.AddWithValue("@Content", normalizedContent);
            cmd.Parameters.AddWithValue("@IsImportant", isImportant);
            cmd.Parameters.AddWithValue("@ExpiresAt", (object?)expiresAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsEvent", isEvent);
            cmd.Parameters.AddWithValue("@EventCoinReward", eventCoinReward);
            cmd.Parameters.AddWithValue("@EventGrantDelayDays", eventGrantDelayDays);

            var newId = await cmd.ExecuteScalarAsync();
            var id = Convert.ToInt32(newId);
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
                Content: normalizedContent,
                CreatedAt: DateTime.UtcNow,
                ImageUrl: null,
                MediaUrls: null,
                IsImportant: isImportant,
                ExpiresAt: expiresAt,
                LikesCount: 0,
                CommentsCount: 0,
                Poll: normalizedPoll == null ? null : ToPollItem(normalizedPoll),
                IsEvent: isEvent,
                EventCoinReward: eventCoinReward,
                IsRegistered: false,
                EventRegistrations: null);

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

    [HttpGet("event-registrants/search")]
    public async Task<ActionResult<EventRegistrantsSearchResponse>> SearchEventRegistrants([FromQuery] string q = "", [FromQuery] int take = 20)
    {
        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(cs))
            return StatusCode(500, new EventRegistrantsSearchResponse(false, "Не настроено подключение к БД", null));

        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync();
            await EnsurePostEventTablesExistsAsync(connection);

            var query = (q ?? "").Trim();
            if (take <= 0) take = 20;
            if (take > 50) take = 50;

            // normalize: lower, strip spaces and punctuation commonly used in FIO
            static string Norm(string s) =>
                (s ?? "")
                    .Trim()
                    .ToLowerInvariant()
                    .Replace(" ", "")
                    .Replace("-", "")
                    .Replace(".", "");

            var nq = Norm(query);
            var like = nq.Length == 0 ? null : $"%{nq}%";

            const string sql = @"
                SELECT TOP (@Take)
                    R.[Login],
                    LTRIM(RTRIM(CONCAT(
                        ISNULL(C.[Фамилия], ''),
                        CASE WHEN C.[Имя] IS NULL OR LTRIM(RTRIM(C.[Имя])) = '' THEN '' ELSE ' ' + LTRIM(RTRIM(C.[Имя])) END,
                        CASE WHEN C.[Отчество] IS NULL OR LTRIM(RTRIM(C.[Отчество])) = '' THEN '' ELSE ' ' + LTRIM(RTRIM(C.[Отчество])) END
                    ))) AS FullName,
                    C.[Фамилия],
                    C.[Имя],
                    C.[Отчество],
                    UP.[AvatarFileName]
                FROM (SELECT DISTINCT [Login] FROM [App_PostEventRegistrations]) R
                LEFT JOIN [Lexema_Кадры_ЛичнаяКарточка] C ON C.[Логин] = R.[Login]
                LEFT JOIN [App_UserProfile] UP ON UP.[Login] = R.[Login]
                WHERE (@Like IS NULL) OR (
                    LOWER(REPLACE(REPLACE(REPLACE(CONCAT(ISNULL(C.[Фамилия], ''), ISNULL(C.[Имя], ''), ISNULL(C.[Отчество], '')), ' ', ''), '-', ''), '.', '')) LIKE @Like
                )
                ORDER BY FullName;";

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var list = new List<EventRegistrantMentionItem>();
            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Take", take);
            cmd.Parameters.AddWithValue("@Like", (object?)like ?? DBNull.Value);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var login = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var fullName = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var fam = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var im = reader.IsDBNull(3) ? "" : reader.GetString(3);
                var ot = reader.IsDBNull(4) ? "" : reader.GetString(4);
                var mentionKey = Norm($"{fam}{im}{ot}");
                if (string.IsNullOrWhiteSpace(mentionKey))
                    mentionKey = Norm(fullName);
                if (string.IsNullOrWhiteSpace(mentionKey))
                    mentionKey = Norm(login);
                var avatarFile = reader.IsDBNull(5) ? null : reader.GetString(5);
                var avatarUrl = string.IsNullOrWhiteSpace(avatarFile) ? null : $"{baseUrl}/uploads/avatars/{Uri.EscapeDataString(avatarFile)}";
                list.Add(new EventRegistrantMentionItem(login, string.IsNullOrWhiteSpace(fullName) ? login : fullName, mentionKey, avatarUrl));
            }
            return Ok(new EventRegistrantsSearchResponse(true, "OK", list));
        }
        catch (Exception ex)
        {
            return Ok(new EventRegistrantsSearchResponse(false, $"Ошибка: {ex.GetType().Name}: {ex.Message}", null));
        }
    }

    [HttpPost("media")]
    [RequestSizeLimit(25_000_000)] 
    public async Task<ActionResult<CreatePostResponse>> CreateWithMedia(
        [FromForm] string? content,
        [FromForm] string? authorLogin,
        [FromForm] bool? isImportant,
        [FromForm] string? pollJson,
        [FromForm] List<IFormFile>? media)
    {
        Console.WriteLine(
            $"POST /api/post/media: contentLen={(content?.Length ?? 0)} authorLogin='{authorLogin ?? ""}' isImportant='{isImportant?.ToString() ?? "null"}' mediaNull={(media == null ? "yes" : "no")}");
        if (media != null && media.Count > 0)
        {
            Console.WriteLine(
                $"POST /api/post/media: mediaCount={media.Count}");
        }

        var normalizedContent = content?.Trim() ?? string.Empty;
        var normalizedPoll = ParseAndNormalizePollOrNull(pollJson);
        var hasMedia = media != null && media.Any(m => m != null && m.Length > 0);
        var hasPoll = normalizedPoll != null;
        if (string.IsNullOrWhiteSpace(normalizedContent) && !hasMedia && !hasPoll)
        {
            return BadRequest(new CreatePostResponse(false, "Добавьте текст, медиа или опрос", null));
        }
        if (normalizedContent.Length > MaxPostTextLength)
        {
            return BadRequest(new CreatePostResponse(false, $"Максимум {MaxPostTextLength} символа(ов) в тексте новости", null));
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
            await EnsurePostMediaTableExistsAsync(connection);
            await EnsurePostEventTablesExistsAsync(connection);
            await EnsureUserPermissionsTableExistsAsync(connection);

            var allowed = await CanCreatePostsAsync(connection, authorLogin);
            if (!allowed)
            {
                return StatusCode(403, new CreatePostResponse(false, "Нет прав на создание новостей", null));
            }

            var authorName = await GetAuthorNameAsync(connection, authorLogin);
            var mediaValidationError = ValidateMediaLimits(media);
            if (mediaValidationError != null)
            {
                return BadRequest(new CreatePostResponse(false, mediaValidationError, null));
            }
            var mediaUrls = media != null && media.Count > 0
                ? await SaveMediaListAsync(media)
                : new List<string>();
            var imageUrl = mediaUrls.FirstOrDefault();
            var importantFlag = isImportant ?? false;
            var isEventFlag = requestBoolFromForm(Request.Form, "isEvent");
            var eventCoins = isEventFlag ? Math.Clamp(requestIntFromForm(Request.Form, "eventCoinReward"), 0, 100000) : 0;
            var wantsInstant = isEventFlag && requestBoolFromForm(Request.Form, "eventGrantInstant");
            var isTechAdmin = await IsTechAdminAsync(connection, authorLogin);
            var eventGrantDelayDays = 0;
            if (isEventFlag)
            {
                var requestedDelay = requestIntFromForm(Request.Form, "eventGrantDelayDays");
                if (requestedDelay != 1 && requestedDelay != 2) requestedDelay = 2;
                eventGrantDelayDays = (isTechAdmin && wantsInstant) ? 0 : requestedDelay;
            }
            DateTime? expiresAt = importantFlag ? null : DateTime.UtcNow.AddDays(7);

            const string sql = @"
                INSERT INTO [App_Posts] ([AuthorLogin], [AuthorName], [Content], [CreatedAt], [ImageUrl], [IsImportant], [ExpiresAt], [LikesCount], [CommentsCount], [IsEvent], [EventCoinReward], [EventGrantDelayDays])
                VALUES (@AuthorLogin, @AuthorName, @Content, GETUTCDATE(), @ImageUrl, @IsImportant, @ExpiresAt, 0, 0, @IsEvent, @EventCoinReward, @EventGrantDelayDays);
                SELECT SCOPE_IDENTITY();";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@AuthorLogin", authorLogin);
            cmd.Parameters.AddWithValue("@AuthorName", authorName ?? authorLogin);
            cmd.Parameters.AddWithValue("@Content", normalizedContent);
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)imageUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsImportant", importantFlag);
            cmd.Parameters.AddWithValue("@ExpiresAt", (object?)expiresAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsEvent", isEventFlag);
            cmd.Parameters.AddWithValue("@EventCoinReward", eventCoins);
            cmd.Parameters.AddWithValue("@EventGrantDelayDays", eventGrantDelayDays);

            var newId = await cmd.ExecuteScalarAsync();
            var id = Convert.ToInt32(newId, CultureInfo.InvariantCulture);
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
                Content: normalizedContent,
                CreatedAt: DateTime.UtcNow,
                ImageUrl: imageUrl,
                MediaUrls: mediaUrls.Count == 0 ? null : mediaUrls,
                IsImportant: importantFlag,
                ExpiresAt: expiresAt,
                LikesCount: 0,
                CommentsCount: 0,
                Poll: normalizedPoll == null ? null : ToPollItem(normalizedPoll),
                IsEvent: isEventFlag,
                EventCoinReward: eventCoins,
                IsRegistered: false,
                EventRegistrations: null);

            if (mediaUrls.Count > 0)
            {
                await SavePostMediaAsync(connection, id, mediaUrls);
            }

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
            await EnsurePostMediaTableExistsAsync(connection);
            await EnsurePostEventTablesExistsAsync(connection);

            
            const string cleanupSql = @"
                DELETE FROM [App_Posts]
                WHERE [ExpiresAt] IS NOT NULL AND [ExpiresAt] <= GETUTCDATE();";
            await using (var cleanup = new SqlCommand(cleanupSql, connection))
            {
                await cleanup.ExecuteNonQueryAsync();
            }

            const string sql = @"
                SELECT [Id], [AuthorLogin], [AuthorName], [Content], [CreatedAt], [ImageUrl], [IsImportant], [ExpiresAt], [LikesCount], [CommentsCount], ISNULL([IsEvent],0), ISNULL([EventCoinReward],0)
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
                        MediaUrls: null,
                        IsImportant: !reader.IsDBNull(6) && reader.GetBoolean(6),
                        ExpiresAt: reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                        LikesCount: reader.GetInt32(8),
                        CommentsCount: reader.GetInt32(9),
                        Poll: null,
                        IsEvent: !reader.IsDBNull(10) && Convert.ToInt32(reader.GetValue(10)) == 1,
                        EventCoinReward: reader.IsDBNull(11) ? 0 : Convert.ToInt32(reader.GetValue(11)),
                        IsRegistered: false,
                        EventRegistrations: null));
                }
            }

            for (var i = 0; i < posts.Count; i++)
            {
                var p = posts[i];
                var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
                var mediaUrls = await GetPostMediaUrlsAsync(connection, p.Id);
                posts[i] = p with
                {
                    MediaUrls = mediaUrls.Count == 0 ? (p.ImageUrl == null ? null : new List<string> { NormalizeUploadsUrl(p.ImageUrl) }) : mediaUrls,
                    Poll = await GetPollForPostAsync(connection, p.Id, login, p.AuthorLogin, baseUrl),
                    IsRegistered = p.IsEvent && await IsEventRegisteredAsync(connection, p.Id, login),
                    EventRegistrations = p.IsEvent && !string.IsNullOrWhiteSpace(login) && string.Equals(p.AuthorLogin, login, StringComparison.OrdinalIgnoreCase)
                        ? await GetEventRegistrationsAsync(connection, p.Id, baseUrl)
                        : null
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

    [HttpPost("{id:int}/register-event")]
    public async Task<ActionResult<EventRegisterResponse>> RegisterEvent(int id, [FromBody] EventRegisterRequest request)
    {
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Login))
            return BadRequest(new EventRegisterResponse(false, "Некорректные данные"));

        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(cs))
            return StatusCode(500, new EventRegisterResponse(false, "Не настроено подключение к БД"));

        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync();
            await EnsurePostEventTablesExistsAsync(connection);

            const string postSql = @"SELECT TOP 1 [AuthorLogin], ISNULL([IsEvent],0) FROM [App_Posts] WHERE [Id] = @Id;";
            string authorLogin = "";
            var isEvent = false;
            await using (var cmd = new SqlCommand(postSql, connection))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return Ok(new EventRegisterResponse(false, "Новость не найдена"));
                authorLogin = reader.IsDBNull(0) ? "" : reader.GetString(0);
                isEvent = !reader.IsDBNull(1) && Convert.ToInt32(reader.GetValue(1)) == 1;
            }
            if (!isEvent) return Ok(new EventRegisterResponse(false, "Это не мероприятие"));
            var login = request.Login.Trim();
            if (string.Equals(login, authorLogin, StringComparison.OrdinalIgnoreCase))
                return Ok(new EventRegisterResponse(false, "Автор не может зарегистрироваться"));

            const string insertSql = @"
                IF EXISTS (SELECT 1 FROM [App_PostEventRegistrations] WHERE [PostId] = @PostId AND [Login] = @Login)
                    SELECT 0
                ELSE
                BEGIN
                    INSERT INTO [App_PostEventRegistrations] ([PostId], [Login], [RegisteredAt])
                    VALUES (@PostId, @Login, GETUTCDATE());
                    SELECT 1
                END";
            int inserted;
            await using (var ins = new SqlCommand(insertSql, connection))
            {
                ins.Parameters.AddWithValue("@PostId", id);
                ins.Parameters.AddWithValue("@Login", login);
                inserted = Convert.ToInt32(await ins.ExecuteScalarAsync() ?? 0);
            }
            if (inserted == 0) return Ok(new EventRegisterResponse(true, "Вы уже зарегистрированы"));

            return Ok(new EventRegisterResponse(true, "Регистрация на мероприятие выполнена"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new EventRegisterResponse(false, $"Ошибка: {ex.Message}"));
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
                ALTER TABLE [App_Posts] ADD [ExpiresAt] DATETIME2 NULL;
            IF COL_LENGTH('App_Posts', 'IsEvent') IS NULL
                ALTER TABLE [App_Posts] ADD [IsEvent] BIT NOT NULL CONSTRAINT [DF_App_Posts_IsEvent] DEFAULT 0;
            IF COL_LENGTH('App_Posts', 'EventCoinReward') IS NULL
                ALTER TABLE [App_Posts] ADD [EventCoinReward] INT NOT NULL CONSTRAINT [DF_App_Posts_EventCoinReward] DEFAULT 0;
            IF COL_LENGTH('App_Posts', 'EventGrantDelayDays') IS NULL
                ALTER TABLE [App_Posts] ADD [EventGrantDelayDays] INT NOT NULL CONSTRAINT [DF_App_Posts_EventGrantDelayDays] DEFAULT 2;";

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

    private static async Task EnsurePostMediaTableExistsAsync(SqlConnection connection)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_PostMedia')
            CREATE TABLE [App_PostMedia] (
                [Id] INT IDENTITY(1,1) PRIMARY KEY,
                [PostId] INT NOT NULL,
                [MediaUrl] NVARCHAR(500) NOT NULL,
                [SortOrder] INT NOT NULL DEFAULT 0,
                [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );";
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsurePostEventTablesExistsAsync(SqlConnection connection)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_PostEventRegistrations')
            CREATE TABLE [App_PostEventRegistrations] (
                [PostId] INT NOT NULL,
                [Login] NVARCHAR(100) NOT NULL,
                [RegisteredAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT [PK_App_PostEventRegistrations] PRIMARY KEY ([PostId], [Login])
            );
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_PostEventPendingCoins')
            CREATE TABLE [App_PostEventPendingCoins] (
                [Id] INT IDENTITY(1,1) PRIMARY KEY,
                [PostId] INT NOT NULL,
                [Login] NVARCHAR(100) NOT NULL,
                [Coins] INT NOT NULL,
                [DueAt] DATETIME2 NOT NULL,
                [GrantedAt] DATETIME2 NULL,
                [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );";
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> IsEventRegisteredAsync(SqlConnection connection, int postId, string? login)
    {
        if (string.IsNullOrWhiteSpace(login)) return false;
        const string sql = @"SELECT TOP 1 1 FROM [App_PostEventRegistrations] WHERE [PostId] = @PostId AND [Login] = @Login;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@PostId", postId);
        cmd.Parameters.AddWithValue("@Login", login.Trim());
        var o = await cmd.ExecuteScalarAsync();
        return o != null && o != DBNull.Value;
    }

    private async Task<List<EventRegistrationItem>> GetEventRegistrationsAsync(SqlConnection connection, int postId, string baseUrl)
    {
        const string sql = @"
            SELECT R.[Login], COALESCE(LTRIM(RTRIM(CONCAT(C.[Фамилия], ' ', C.[Имя]))), R.[Login]) AS FullName, UP.[AvatarFileName]
            FROM [App_PostEventRegistrations] R
            LEFT JOIN [Lexema_Кадры_ЛичнаяКарточка] C ON C.[Логин] = R.[Login]
            LEFT JOIN [App_UserProfile] UP ON UP.[Login] = R.[Login]
            WHERE R.[PostId] = @PostId
            ORDER BY R.[RegisteredAt] DESC;";
        var list = new List<EventRegistrationItem>();
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@PostId", postId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var login = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var name = reader.IsDBNull(1) ? login : reader.GetString(1);
            var avatarFile = reader.IsDBNull(2) ? null : reader.GetString(2);
            var avatarUrl = string.IsNullOrWhiteSpace(avatarFile) ? null : $"{baseUrl}/uploads/avatars/{Uri.EscapeDataString(avatarFile)}";
            list.Add(new EventRegistrationItem(login, name, avatarUrl));
        }
        return list;
    }

    private static bool requestBoolFromForm(IFormCollection form, string key)
    {
        var raw = form[key].ToString().Trim();
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1" || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static int requestIntFromForm(IFormCollection form, string key)
    {
        var raw = form[key].ToString().Trim();
        return int.TryParse(raw, out var v) ? v : 0;
    }

    private static async Task<bool> IsTechAdminAsync(SqlConnection connection, string login)
    {
        var key = login.Trim();
        if (string.IsNullOrWhiteSpace(key)) return false;
        const string sql = @"SELECT TOP 1 ISNULL([CanTechAdmin], 0) FROM [App_UserPermissions] WHERE [Login] = @Login;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", key);
        var v = await cmd.ExecuteScalarAsync();
        return v != null && v != DBNull.Value && Convert.ToInt32(v) == 1;
    }

    private static async Task SavePostMediaAsync(SqlConnection connection, int postId, List<string> mediaUrls)
    {
        const string deleteSql = @"DELETE FROM [App_PostMedia] WHERE [PostId] = @PostId;";
        await using (var del = new SqlCommand(deleteSql, connection))
        {
            del.Parameters.AddWithValue("@PostId", postId);
            await del.ExecuteNonQueryAsync();
        }

        const string insertSql = @"
            INSERT INTO [App_PostMedia] ([PostId], [MediaUrl], [SortOrder], [CreatedAt])
            VALUES (@PostId, @MediaUrl, @SortOrder, GETUTCDATE());";
        for (var i = 0; i < mediaUrls.Count; i++)
        {
            await using var ins = new SqlCommand(insertSql, connection);
            ins.Parameters.AddWithValue("@PostId", postId);
            ins.Parameters.AddWithValue("@MediaUrl", mediaUrls[i]);
            ins.Parameters.AddWithValue("@SortOrder", i);
            await ins.ExecuteNonQueryAsync();
        }
    }

    private static async Task<List<string>> GetPostMediaUrlsAsync(SqlConnection connection, int postId)
    {
        const string sql = @"
            SELECT [MediaUrl]
            FROM [App_PostMedia]
            WHERE [PostId] = @PostId
            ORDER BY [SortOrder], [Id];";
        var result = new List<string>();
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@PostId", postId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0))
            {
                var url = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    result.Add(NormalizeUploadsUrl(url));
                }
            }
        }
        return result;
    }

    private static string NormalizeUploadsUrl(string raw)
    {
        var value = (raw ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(value)) return value;

        var uploadsIndex = value.IndexOf("/uploads/", StringComparison.OrdinalIgnoreCase);
        if (uploadsIndex >= 0) return value.Substring(uploadsIndex);
        if (value.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)) return "/" + value;

        var wwwrootUploadsIndex = value.IndexOf("wwwroot/uploads/", StringComparison.OrdinalIgnoreCase);
        if (wwwrootUploadsIndex >= 0)
        {
            var tail = value.Substring(wwwrootUploadsIndex + "wwwroot/".Length);
            return "/" + tail.TrimStart('/');
        }

        return value;
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
                [CanTechAdmin] BIT NOT NULL DEFAULT 0,
                [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );
            IF COL_LENGTH('App_UserPermissions', 'CanTechAdmin') IS NULL
                ALTER TABLE [App_UserPermissions] ADD [CanTechAdmin] BIT NOT NULL CONSTRAINT [DF_App_UserPermissions_CanTechAdmin] DEFAULT 0;";
        await using var cmd = new SqlCommand(createSql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> CanCreatePostsAsync(SqlConnection connection, string login)
    {
        const string sql = @"
            SELECT TOP 1
                CASE WHEN ISNULL([CanTechAdmin], 0) = 1 THEN 1 ELSE ISNULL([CanCreatePosts], 0) END
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

    private static string ResolveMediaExtension(IFormFile media, byte[]? header = null)
    {
        var ext = Path.GetExtension(media.FileName);
        if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 10)
            return ext.ToLowerInvariant();

        var contentType = (media.ContentType ?? "").Trim().ToLowerInvariant();
        var byMime = contentType switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/heic" => ".heic",
            "image/heif" => ".heif",
            "video/mp4" => ".mp4",
            "video/quicktime" => ".mov",
            "video/webm" => ".webm",
            "video/x-msvideo" => ".avi",
            "video/3gpp" => ".3gp",
            _ => ""
        };

        if (!string.IsNullOrWhiteSpace(byMime)) return byMime;

        if (header != null && header.Length >= 12)
        {
            // JPEG
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return ".jpg";
            // PNG
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) return ".png";
            // GIF
            if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46) return ".gif";
            // WEBP (RIFF....WEBP)
            if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
                && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50) return ".webp";
            // ftyp box (mp4/mov/heic/heif)
            if (header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
            {
                var brand = System.Text.Encoding.ASCII.GetString(header, 8, 4).ToLowerInvariant();
                if (brand.StartsWith("heic") || brand.StartsWith("heif") || brand.StartsWith("mif1") || brand.StartsWith("msf1")) return ".heic";
                if (brand.StartsWith("qt")) return ".mov";
                return ".mp4";
            }
        }

        return ".bin";
    }

    private async Task<string> SaveMediaAsync(IFormFile media)
    {
        var uploadsRoot = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "uploads");
        Directory.CreateDirectory(uploadsRoot);

        await using var source = media.OpenReadStream();
        var probe = new byte[64];
        var read = await source.ReadAsync(probe, 0, probe.Length);
        var header = read > 0 ? probe.Take(read).ToArray() : Array.Empty<byte>();
        if (source.CanSeek) source.Seek(0, SeekOrigin.Begin);

        var ext = ResolveMediaExtension(media, header);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadsRoot, fileName);

        await using (var fs = System.IO.File.Create(fullPath))
        {
            await source.CopyToAsync(fs);
        }

        return $"/uploads/{fileName}";
    }

    private async Task<List<string>> SaveMediaListAsync(List<IFormFile> mediaFiles)
    {
        var result = new List<string>();
        foreach (var file in mediaFiles)
        {
            if (file == null || file.Length <= 0) continue;
            var url = await SaveMediaAsync(file);
            if (!string.IsNullOrWhiteSpace(url))
            {
                result.Add(url);
            }
        }
        return result;
    }

    private static string? ValidateMediaLimits(List<IFormFile>? mediaFiles)
    {
        if (mediaFiles == null || mediaFiles.Count == 0) return null;
        var imageCount = 0;
        var videoCount = 0;
        foreach (var file in mediaFiles)
        {
            if (file == null || file.Length <= 0) continue;
            var contentType = (file.ContentType ?? "").Trim().ToLowerInvariant();
            var ext = Path.GetExtension(file.FileName ?? "").Trim().ToLowerInvariant();
            var isVideo = contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                          || ext is ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" or ".m4v" or ".3gp";
            if (isVideo) videoCount++;
            else imageCount++;
        }

        if (imageCount > MaxPostImageCount)
            return $"Максимум {MaxPostImageCount} фото в одной новости";
        if (videoCount > MaxPostVideoCount)
            return $"Максимум {MaxPostVideoCount} видео в одной новости";
        return null;
    }

}

public record CreatePostRequest(
    string Content,
    string AuthorLogin,
    bool? IsImportant,
    PollCreateRequest? Poll,
    bool? IsEvent = null,
    int? EventCoinReward = null,
    int? EventGrantDelayDays = null,
    bool? EventGrantInstant = null
);

public record EventRegistrantMentionItem(string Login, string FullName, string MentionKey, string? AvatarUrl);
public record EventRegistrantsSearchResponse(bool Success, string Message, List<EventRegistrantMentionItem>? Items);

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
    List<string>? MediaUrls,
    bool IsImportant,
    DateTime? ExpiresAt,
    int LikesCount,
    int CommentsCount,
    PollItem? Poll,
    bool IsEvent,
    int EventCoinReward,
    bool IsRegistered,
    List<EventRegistrationItem>? EventRegistrations);

public record CreatePostResponse(bool Success, string Message, PostItem? Post);

public record FeedResponse(bool Success, string Message, List<PostItem>? Posts);
public record DeletePostResponse(bool Success, string Message);
public record VoteRequest(string Login, int OptionId);
public record VoteResponse(bool Success, string Message, PollItem? Poll = null);
public record EventRegisterRequest(string Login);
public record EventRegisterResponse(bool Success, string Message);
public record EventRegistrationItem(string Login, string Name, string? AvatarUrl);
