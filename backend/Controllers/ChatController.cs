using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using EmployeeApi.Services;
using System.Text.Json;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ChatMessageCipher _cipher;

    /// <summary>Боты опросов/анкет — не показываем и удаляем данные из БД (идемпотентно).</summary>
    private const string LegacyPollSurveyBotsInSql =
        "N'StekloPoll', N'StekloPolls', N'StekloSurvey', N'StekloSurveys', N'StekloOpros', N'StekloAnketa', N'StekloQuestionnaire', "
        + "N'StekloOprosy', N'StekloAnkety', N'StekloOprosyAnkety', N'StekloForms', N'StekloLentaOprosy', N'StekloFeedPolls'";

    /// <summary>SQL: строка чата — устаревший бот опросов (по BotId или по названию в ленте).</summary>
    private static string LegacyPollSurveyBotThreadMatchSql(string tableAlias)
    {
        var a = string.IsNullOrWhiteSpace(tableAlias) ? "" : $"{tableAlias}.";
        return $"ISNULL({a}[Type], 'bot') = 'bot' AND (" +
            $"{a}[BotId] IN ({LegacyPollSurveyBotsInSql}) OR " +
            $"LTRIM(RTRIM({a}[Title])) = N'Опросы и анкеты' OR " +
            $"({a}[Title] LIKE N'%опрос%' AND {a}[Title] LIKE N'%анкет%') OR " +
            $"{a}[Title] LIKE N'%анкетирован%')";
    }

    private static string LegacyPollSurveyBotThreadExcludeSql(string tableAlias) =>
        $"NOT ({LegacyPollSurveyBotThreadMatchSql(tableAlias)})";

    public ChatController(IConfiguration configuration, IWebHostEnvironment env, ChatMessageCipher cipher)
    {
        _configuration = configuration;
        _env = env;
        _cipher = cipher;
    }

    [HttpGet("threads")]
    public async Task<ActionResult<ThreadsResponse>> GetThreads([FromQuery] string login)
    {
        if (string.IsNullOrWhiteSpace(login))
            return BadRequest(new ThreadsResponse(false, "Укажите логин", null));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return StatusCode(500, new ThreadsResponse(false, "Не настроено подключение к БД", null));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsureChatTablesAsync(connection);
            await EnsureThreadReadsTableAsync(connection);
            await MaybeEncryptLegacyChatMessagesAsync(connection);

            // Support both "employeeId" and stored "card login"
            var normalizedLogin = login.Trim();
            var ownerLogin = await ResolveOwnerLoginAsync(connection, normalizedLogin);

            // Seed required threads
            await EnsureSecurityBotThreadAsync(connection, ownerLogin);
            if (await IsTechAdminAsync(connection, normalizedLogin))
            {
                await EnsureMonitorBotThreadAsync(connection, ownerLogin);
            }

            var sql = $@"
                SELECT T.[Id], T.[Type], T.[Title], T.[BotId], T.[CreatedAt],
                       M.[Text] AS LastText, M.[CreatedAt] AS LastAt, M.[MetaJson] AS LastMetaJson,
                       M.[SenderType] AS LastSenderType, M.[SenderId] AS LastSenderId,
                       M.[Id] AS LastMessageId,
                       T.[PeerLogin] AS PeerLogin,
                       ISNULL(U.UnreadCount, 0) AS UnreadCount,
                       ISNULL(P.[CanTechAdmin], 0) AS IsTechAdmin,
                       ISNULL(B.[IsOfficial], 0) AS IsOfficialBot,
                       CASE
                           WHEN ISNULL(T.[Type], 'bot') = 'user' THEN
                               CASE
                                   WHEN UP.[AvatarFileName] IS NULL OR LTRIM(RTRIM(UP.[AvatarFileName])) = '' THEN NULL
                                   ELSE UP.[AvatarFileName]
                               END
                           ELSE B.[AvatarUrl]
                       END AS AvatarRaw
                FROM [App_Threads] T
                OUTER APPLY (
                    SELECT TOP 1 [Id], [Text], [CreatedAt], [MetaJson], [SenderType], [SenderId]
                    FROM [App_Messages] MM
                    WHERE MM.[ThreadId] = T.[Id]
                    ORDER BY MM.[Id] DESC
                ) M
                OUTER APPLY (
                    SELECT COUNT(1) AS UnreadCount
                    FROM [App_Messages] MM2
                    LEFT JOIN [App_ThreadReads] R
                      ON R.[ThreadId] = T.[Id] AND R.[Login] = @Login
                    WHERE MM2.[ThreadId] = T.[Id]
                      AND MM2.[Id] > ISNULL(R.[LastReadMessageId], 0)
                ) U
                LEFT JOIN [App_UserPermissions] P
                  ON P.[Login] = COALESCE(T.[PeerLogin], T.[Title])
                LEFT JOIN [App_BotProfiles] B
                  ON B.[BotId] = T.[BotId]
                LEFT JOIN [App_UserProfile] UP
                  ON UP.[Login] = T.[PeerLogin]
                WHERE T.[OwnerLogin] = @Login
                  AND ISNULL(T.[Type], 'bot') <> 'channel'
                  AND {LegacyPollSurveyBotThreadExcludeSql("T")}
                ORDER BY ISNULL(M.[CreatedAt], T.[CreatedAt]) DESC;";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Login", ownerLogin);

            var list = new List<ThreadItem>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var avatarRaw = reader.IsDBNull(15) ? null : reader.GetString(15);
                    var avatarUrl = avatarRaw;
                    if (!string.IsNullOrWhiteSpace(avatarRaw) &&
                        !avatarRaw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !avatarRaw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        avatarUrl = BuildUserAvatarPublicUrl(avatarRaw);
                    }

                    var lastFromSelf = false;
                    if (!reader.IsDBNull(8) &&
                        string.Equals(reader.GetString(8), "user", StringComparison.OrdinalIgnoreCase))
                    {
                        var sid = reader.IsDBNull(9) ? "" : reader.GetString(9).Trim();
                        lastFromSelf = !string.IsNullOrEmpty(sid) &&
                            string.Equals(sid, ownerLogin, StringComparison.OrdinalIgnoreCase);
                    }

                    var threadType = reader.IsDBNull(1) ? "bot" : reader.GetString(1);
                    var peerLogin = reader.IsDBNull(11) ? null : reader.GetString(11);
                    var lastMessageId = reader.IsDBNull(10) ? 0 : Convert.ToInt32(reader.GetValue(10));
                    var lastMessageIsRead = false;
                    if (lastFromSelf &&
                        threadType.Equals("user", StringComparison.OrdinalIgnoreCase) &&
                        lastMessageId > 0 &&
                        !string.IsNullOrWhiteSpace(peerLogin))
                    {
                        var recipientThreadId = await GetDirectThreadIdAsync(connection, peerLogin.Trim(), ownerLogin);
                        if (recipientThreadId > 0)
                        {
                            lastMessageIsRead = await IsMirroredMessageReadByOwnerAsync(connection, lastMessageId, recipientThreadId, peerLogin.Trim());
                        }
                    }

                    var rawLastText = reader.IsDBNull(5) ? null : _cipher.UnprotectFieldNullable(reader.GetString(5));
                    var rawLastMeta = reader.IsDBNull(7) ? null : _cipher.UnprotectFieldNullable(reader.GetString(7));
                    var lastPreview = BuildThreadLastPreview(rawLastText, rawLastMeta);

                    list.Add(new ThreadItem(
                        Id: reader.GetInt32(0),
                        Type: threadType,
                        Title: reader.IsDBNull(2) ? "" : reader.GetString(2),
                        BotId: reader.IsDBNull(3) ? null : reader.GetString(3),
                        CreatedAtUtc: reader.GetDateTime(4),
                        LastMessageText: lastPreview,
                        LastMessageAtUtc: reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                        LastMessageFromSelf: lastFromSelf,
                        LastMessageIsRead: lastMessageIsRead,
                        UnreadCount: reader.IsDBNull(12) ? 0 : Convert.ToInt32(reader.GetValue(12)),
                        IsTechAdmin: !reader.IsDBNull(13) && Convert.ToInt32(reader.GetValue(13)) == 1,
                        IsOfficialBot: !reader.IsDBNull(14) && Convert.ToInt32(reader.GetValue(14)) == 1,
                        AvatarUrl: avatarUrl
                    ));
                }
            }

            return Ok(new ThreadsResponse(true, "OK", list));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            // Возвращаем 200, чтобы клиент всегда мог распарсить JSON и показать причину,
            // иначе Retrofit часто сводит это к "ошибка сети".
            return Ok(new ThreadsResponse(false, $"Ошибка: {ex.GetType().Name}: {ex.Message}", null));
        }
    }

    [HttpGet("colleagues/search")]
    public async Task<ActionResult<ColleagueSearchResponse>> SearchColleagues([FromQuery] string login, [FromQuery] string? q = null)
    {
        if (string.IsNullOrWhiteSpace(login))
            return BadRequest(new ColleagueSearchResponse(false, "Укажите логин", null));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return StatusCode(500, new ColleagueSearchResponse(false, "Не настроено подключение к БД", null));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsureChatTablesAsync(connection);
            await EnsureAppUserProfileTableAsync(connection);

            var ownerLogin = await ResolveOwnerLoginAsync(connection, login.Trim());
            var query = (q ?? string.Empty).Trim();
            var queryLike = $"%{query}%";

            const string sql = @"
                SELECT TOP 50
                    COALESCE(TRY_CONVERT(nvarchar(100), C.[Логин]), '') AS Login,
                    COALESCE(TRY_CONVERT(nvarchar(50), C.[ТабельныйНомер]), '') AS EmployeeId,
                    LTRIM(RTRIM(CONCAT(
                        COALESCE(TRY_CONVERT(nvarchar(200), C.[Фамилия]), ''),
                        CASE WHEN COALESCE(TRY_CONVERT(nvarchar(200), C.[Имя]), '') = '' THEN '' ELSE ' ' + COALESCE(TRY_CONVERT(nvarchar(200), C.[Имя]), '') END,
                        CASE WHEN COALESCE(TRY_CONVERT(nvarchar(200), C.[Отчество]), '') = '' THEN '' ELSE ' ' + COALESCE(TRY_CONVERT(nvarchar(200), C.[Отчество]), '') END
                    ))) AS FullName,
                    COALESCE(TRY_CONVERT(nvarchar(200), C.[Должность]), '') AS Position,
                    ISNULL(PM.[CanTechAdmin], 0) AS IsTechAdmin,
                    UP.[AvatarFileName] AS AvatarFileName
                FROM [Lexema_Кадры_ЛичнаяКарточка] C
                LEFT JOIN [App_UserPermissions] PM ON PM.[Login] = C.[Логин]
                LEFT JOIN [App_UserProfile] UP ON UP.[Login] = C.[Логин]
                WHERE ISNULL(C.[ЗарегВПриложении], 0) = 1
                  AND TRY_CONVERT(datetime2, C.[ДатаУвольнения]) IS NULL
                  AND LTRIM(RTRIM(COALESCE(TRY_CONVERT(nvarchar(100), C.[Логин]), ''))) <> ''
                  AND C.[Логин] <> @OwnerLogin
                  AND (
                      @Query = ''
                      OR C.[Логин] LIKE @QueryLike
                      OR TRY_CONVERT(nvarchar(50), C.[ТабельныйНомер]) LIKE @QueryLike
                      OR TRY_CONVERT(nvarchar(200), C.[Фамилия]) LIKE @QueryLike
                      OR TRY_CONVERT(nvarchar(200), C.[Имя]) LIKE @QueryLike
                      OR TRY_CONVERT(nvarchar(200), C.[Отчество]) LIKE @QueryLike
                  )
                ORDER BY
                    CASE WHEN @Query = '' THEN 1 ELSE 0 END,
                    TRY_CONVERT(nvarchar(200), C.[Фамилия]),
                    TRY_CONVERT(nvarchar(200), C.[Имя]);";

            var list = new List<ColleagueSearchItem>();
            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@OwnerLogin", ownerLogin);
            cmd.Parameters.AddWithValue("@Query", query);
            cmd.Parameters.AddWithValue("@QueryLike", queryLike);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var loginValue = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                if (string.IsNullOrWhiteSpace(loginValue)) continue;

                var avatarFileName = reader.IsDBNull(5) ? null : reader.GetString(5);
                list.Add(new ColleagueSearchItem(
                    Login: loginValue,
                    EmployeeId: reader.IsDBNull(1) ? "" : reader.GetString(1),
                    FullName: reader.IsDBNull(2) ? loginValue : reader.GetString(2),
                    Position: reader.IsDBNull(3) ? "" : reader.GetString(3),
                    IsTechAdmin: !reader.IsDBNull(4) && Convert.ToInt32(reader.GetValue(4)) == 1,
                    AvatarUrl: BuildUserAvatarPublicUrl(avatarFileName)
                ));
            }

            return Ok(new ColleagueSearchResponse(true, "OK", list));
        }
        catch (Exception ex)
        {
            return Ok(new ColleagueSearchResponse(false, $"Ошибка: {ex.GetType().Name}: {ex.Message}", null));
        }
    }

    [HttpPost("threads/direct/open")]
    public async Task<ActionResult<OpenDirectThreadResponse>> OpenDirectThread([FromBody] OpenDirectThreadRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.ColleagueLogin))
            return BadRequest(new OpenDirectThreadResponse(false, "Укажите login и colleagueLogin", null));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return StatusCode(500, new OpenDirectThreadResponse(false, "Не настроено подключение к БД", null));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsureChatTablesAsync(connection);
            await EnsureAppUserProfileTableAsync(connection);

            var ownerLogin = await ResolveOwnerLoginAsync(connection, request.Login.Trim());
            var peerLogin = request.ColleagueLogin.Trim();
            if (peerLogin.Equals(ownerLogin, StringComparison.OrdinalIgnoreCase))
                return Ok(new OpenDirectThreadResponse(false, "Нельзя открыть чат с самим собой", null));

            const string peerSql = @"
                SELECT TOP 1
                    COALESCE(TRY_CONVERT(nvarchar(100), C.[Логин]), '') AS Login,
                    COALESCE(TRY_CONVERT(nvarchar(50), C.[ТабельныйНомер]), '') AS EmployeeId,
                    LTRIM(RTRIM(CONCAT(
                        COALESCE(TRY_CONVERT(nvarchar(200), C.[Фамилия]), ''),
                        CASE WHEN COALESCE(TRY_CONVERT(nvarchar(200), C.[Имя]), '') = '' THEN '' ELSE ' ' + COALESCE(TRY_CONVERT(nvarchar(200), C.[Имя]), '') END
                    ))) AS DisplayName
                FROM [Lexema_Кадры_ЛичнаяКарточка] C
                WHERE C.[Логин] = @PeerLogin
                  AND ISNULL(C.[ЗарегВПриложении], 0) = 1
                  AND TRY_CONVERT(datetime2, C.[ДатаУвольнения]) IS NULL;";

            string peerEmployeeId;
            string peerDisplayName;
            await using (var peerCmd = new SqlCommand(peerSql, connection))
            {
                peerCmd.Parameters.AddWithValue("@PeerLogin", peerLogin);
                await using var peerReader = await peerCmd.ExecuteReaderAsync();
                if (!await peerReader.ReadAsync())
                    return Ok(new OpenDirectThreadResponse(false, "Коллега не найден или не зарегистрирован", null));
                peerEmployeeId = peerReader.IsDBNull(1) ? "" : peerReader.GetString(1);
                peerDisplayName = peerReader.IsDBNull(2) ? peerLogin : peerReader.GetString(2);
            }

            const string existingSql = @"
                SELECT TOP 1 [Id]
                FROM [App_Threads]
                WHERE [OwnerLogin] = @OwnerLogin
                  AND ISNULL([Type], 'bot') = 'user'
                  AND [PeerLogin] = @PeerLogin;";
            await using (var exCmd = new SqlCommand(existingSql, connection))
            {
                exCmd.Parameters.AddWithValue("@OwnerLogin", ownerLogin);
                exCmd.Parameters.AddWithValue("@PeerLogin", peerLogin);
                var existing = await exCmd.ExecuteScalarAsync();
                if (existing != null && existing != DBNull.Value)
                {
                    var thread = await GetThreadByIdAsync(connection, ownerLogin, Convert.ToInt32(existing));
                    return Ok(new OpenDirectThreadResponse(true, "OK", thread));
                }
            }

            const string insertSql = @"
                INSERT INTO [App_Threads] ([OwnerLogin], [Type], [Title], [BotId], [PeerLogin], [PeerEmployeeId], [CreatedAt])
                VALUES (@OwnerLogin, 'user', @Title, NULL, @PeerLogin, @PeerEmployeeId, GETUTCDATE());
                SELECT SCOPE_IDENTITY();";
            int threadId;
            await using (var ins = new SqlCommand(insertSql, connection))
            {
                ins.Parameters.AddWithValue("@OwnerLogin", ownerLogin);
                ins.Parameters.AddWithValue("@Title", string.IsNullOrWhiteSpace(peerDisplayName) ? peerLogin : peerDisplayName.Trim());
                ins.Parameters.AddWithValue("@PeerLogin", peerLogin);
                ins.Parameters.AddWithValue("@PeerEmployeeId", (object?)peerEmployeeId ?? DBNull.Value);
                var o = await ins.ExecuteScalarAsync();
                threadId = Convert.ToInt32(o);
            }

            var createdThread = await GetThreadByIdAsync(connection, ownerLogin, threadId);
            return Ok(new OpenDirectThreadResponse(true, "OK", createdThread));
        }
        catch (Exception ex)
        {
            return Ok(new OpenDirectThreadResponse(false, $"Ошибка: {ex.GetType().Name}: {ex.Message}", null));
        }
    }

    [HttpGet("threads/{threadId:int}/messages")]
    public async Task<ActionResult<MessagesResponse>> GetMessages(int threadId, [FromQuery] string login, [FromQuery] int take = 50, [FromQuery] int? beforeId = null)
    {
        if (string.IsNullOrWhiteSpace(login))
            return BadRequest(new MessagesResponse(false, "Укажите логин", null));
        if (threadId <= 0)
            return BadRequest(new MessagesResponse(false, "Некорректный threadId", null));

        take = Math.Clamp(take, 1, 200);

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return StatusCode(500, new MessagesResponse(false, "Не настроено подключение к БД", null));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsureChatTablesAsync(connection);
            await EnsureThreadReadsTableAsync(connection);
            await MaybeEncryptLegacyChatMessagesAsync(connection);

            var normalizedLogin = login.Trim();
            var ownerLogin = await ResolveOwnerLoginAsync(connection, normalizedLogin);

            // Authorization + thread metadata
            const string ownSql = @"
                SELECT TOP 1 ISNULL([Type], 'bot') AS ThreadType, [PeerLogin]
                FROM [App_Threads]
                WHERE [Id] = @Id AND [OwnerLogin] = @Login;";
            string threadType;
            string? peerLogin;
            await using (var own = new SqlCommand(ownSql, connection))
            {
                own.Parameters.AddWithValue("@Id", threadId);
                own.Parameters.AddWithValue("@Login", ownerLogin);
                await using var ownReader = await own.ExecuteReaderAsync();
                if (!await ownReader.ReadAsync())
                    return Ok(new MessagesResponse(false, "Диалог не найден", null));
                threadType = ownReader.IsDBNull(0) ? "bot" : ownReader.GetString(0);
                peerLogin = ownReader.IsDBNull(1) ? null : ownReader.GetString(1);
            }

            const string sql = @"
                SELECT TOP (@Take) [Id], [SenderType], [SenderId], [Text], [CreatedAt], [MetaJson]
                FROM [App_Messages]
                WHERE [ThreadId] = @ThreadId
                  AND (@BeforeId IS NULL OR [Id] < @BeforeId)
                ORDER BY [Id] DESC;";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Take", take);
            cmd.Parameters.AddWithValue("@ThreadId", threadId);
            cmd.Parameters.AddWithValue("@BeforeId", (object?)beforeId ?? DBNull.Value);

            var items = new List<MessageItem>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    items.Add(new MessageItem(
                        Id: reader.GetInt32(0),
                        SenderType: reader.IsDBNull(1) ? "system" : reader.GetString(1),
                        SenderId: reader.IsDBNull(2) ? null : reader.GetString(2),
                        SenderName: null,
                        Text: reader.IsDBNull(3) ? "" : _cipher.UnprotectField(reader.GetString(3)),
                        CreatedAtUtc: reader.GetDateTime(4),
                        MetaJson: reader.IsDBNull(5) ? null : _cipher.UnprotectFieldNullable(reader.GetString(5)),
                        SenderIsTechAdmin: false,
                        IsRead: false
                    ));
                }
            }

            var senderAdminCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < items.Count; i++)
            {
                var senderId = items[i].SenderId?.Trim();
                if (string.IsNullOrWhiteSpace(senderId)) continue;
                if (!items[i].SenderType.Equals("user", StringComparison.OrdinalIgnoreCase)) continue;
                if (!senderAdminCache.TryGetValue(senderId, out var isAdmin))
                {
                    isAdmin = await IsTechAdminAsync(connection, senderId);
                    senderAdminCache[senderId] = isAdmin;
                }
                var senderDisplayName = await ResolveDisplayNameByLoginAsync(connection, senderId);
                items[i] = items[i] with { SenderIsTechAdmin = isAdmin, SenderName = senderDisplayName };
            }

            // Accurate read state for outgoing messages in direct chats:
            // message is read when counterpart's thread read pointer reached mirrored message id.
            if (threadType.Equals("user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(peerLogin))
            {
                var recipientThreadId = await GetDirectThreadIdAsync(connection, peerLogin.Trim(), ownerLogin);
                if (recipientThreadId > 0)
                {
                    for (var i = 0; i < items.Count; i++)
                    {
                        var m = items[i];
                        if (!m.SenderType.Equals("user", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.Equals(m.SenderId?.Trim(), normalizedLogin, StringComparison.OrdinalIgnoreCase)) continue;
                        var read = await IsMirroredMessageReadByOwnerAsync(connection, m.Id, recipientThreadId, peerLogin.Trim());
                        items[i] = m with { IsRead = read };
                    }
                }
            }

            items.Reverse(); // oldest -> newest
            // Mark as read up to latest returned message
            var lastId = items.Count == 0 ? 0 : items.Max(x => x.Id);
            if (lastId > 0)
            {
                await UpsertThreadReadAsync(connection, ownerLogin, threadId, lastId);
            }
            return Ok(new MessagesResponse(true, "OK", items));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return Ok(new MessagesResponse(false, $"Ошибка: {ex.GetType().Name}: {ex.Message}", null));
        }
    }

    [HttpPost("threads/{threadId:int}/messages")]
    public async Task<ActionResult<SendMessageResponse>> SendMessage(int threadId, [FromBody] SendMessageRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login))
            return BadRequest(new SendMessageResponse(false, "Укажите логин", null));
        if (threadId <= 0)
            return BadRequest(new SendMessageResponse(false, "Некорректный threadId", null));

        var text = (request.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text) && !TryParseChatMediaMeta(request.MetaJson, out _, out _))
            return Ok(new SendMessageResponse(false, "Пустое сообщение", null));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return StatusCode(500, new SendMessageResponse(false, "Не настроено подключение к БД", null));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsureChatTablesAsync(connection);
            await EnsureThreadReadsTableAsync(connection);
            await MaybeEncryptLegacyChatMessagesAsync(connection);

            var normalizedLogin = request.Login.Trim();
            var ownerLogin = await ResolveOwnerLoginAsync(connection, normalizedLogin);

            // Authorization + thread info
            const string ownSql = @"
                SELECT TOP 1
                    ISNULL([Type], 'bot') AS ThreadType,
                    [PeerLogin],
                    [BotId]
                FROM [App_Threads]
                WHERE [Id] = @Id AND [OwnerLogin] = @Login;";
            string threadType;
            string? threadPeerLogin;
            string? threadBotId = null;
            await using (var own = new SqlCommand(ownSql, connection))
            {
                own.Parameters.AddWithValue("@Id", threadId);
                own.Parameters.AddWithValue("@Login", ownerLogin);
                await using var ownReader = await own.ExecuteReaderAsync();
                if (!await ownReader.ReadAsync())
                    return Ok(new SendMessageResponse(false, "Диалог не найден", null));
                threadType = ownReader.IsDBNull(0) ? "bot" : ownReader.GetString(0);
                threadPeerLogin = ownReader.IsDBNull(1) ? null : ownReader.GetString(1);
                threadBotId = ownReader.IsDBNull(2) ? null : ownReader.GetString(2);
            }

            // Insert message. IMPORTANT: SenderId uses client-provided login (employeeId),
            // so Android can consistently detect outgoing messages.
            const string insSql = @"
                INSERT INTO [App_Messages] ([ThreadId], [SenderType], [SenderId], [Text], [MetaJson], [CreatedAt])
                VALUES (@ThreadId, 'user', @SenderId, @Text, @MetaJson, GETUTCDATE());
                SELECT SCOPE_IDENTITY();";
            int newId;
            var textForDb = _cipher.ProtectField(text);
            var metaForDb = _cipher.ProtectFieldNullable(request.MetaJson);
            await using (var ins = new SqlCommand(insSql, connection))
            {
                ins.Parameters.AddWithValue("@ThreadId", threadId);
                ins.Parameters.AddWithValue("@SenderId", normalizedLogin);
                ins.Parameters.AddWithValue("@Text", textForDb);
                ins.Parameters.AddWithValue("@MetaJson", (object?)metaForDb ?? DBNull.Value);
                var o = await ins.ExecuteScalarAsync();
                newId = Convert.ToInt32(o);
            }

            // Mark thread as read for owner (up to this message)
            await UpsertThreadReadAsync(connection, ownerLogin, threadId, newId);

            if (threadType.Equals("bot", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(threadBotId))
            {
                await HandleTechBotCommandsAsync(connection, threadId, threadBotId, text, normalizedLogin);
            }

            // Mirror direct user-to-user messages into recipient's personal thread,
            // so the second account can see incoming message immediately.
            if (threadType.Equals("user", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(threadPeerLogin))
            {
                var recipientOwnerLogin = threadPeerLogin.Trim();
                var senderOwnerLogin = ownerLogin;
                if (!recipientOwnerLogin.Equals(senderOwnerLogin, StringComparison.OrdinalIgnoreCase))
                {
                    var reciprocalThreadId = await EnsureDirectUserThreadAsync(connection, recipientOwnerLogin, senderOwnerLogin);
                    await using var mirrorIns = new SqlCommand(insSql, connection);
                    mirrorIns.Parameters.AddWithValue("@ThreadId", reciprocalThreadId);
                    mirrorIns.Parameters.AddWithValue("@SenderId", normalizedLogin);
                    mirrorIns.Parameters.AddWithValue("@Text", textForDb);
                    mirrorIns.Parameters.AddWithValue("@MetaJson", (object?)metaForDb ?? DBNull.Value);
                    var mirroredIdObj = await mirrorIns.ExecuteScalarAsync();
                    var mirroredId = Convert.ToInt32(mirroredIdObj);
                    await UpsertMessageMirrorAsync(connection, ownerMessageId: newId, recipientMessageId: mirroredId);

                    // Push like Telegram preview: sender name + text snippet
                    if (FcmPush.IsConfigured())
                    {
                        var senderTitle = await ResolveDisplayNameByLoginAsync(connection, senderOwnerLogin);
                        var textOneLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
                        if (string.IsNullOrWhiteSpace(textOneLine) &&
                            TryParseChatMediaMeta(request.MetaJson, out _, out var pushKind))
                        {
                            textOneLine = string.Equals(pushKind, "video", StringComparison.OrdinalIgnoreCase) ? "Видео" : "Фото";
                        }
                        if (textOneLine.Length > 140) textOneLine = textOneLine.Substring(0, 140) + "…";
                        await FcmPush.SendToLoginAsync(
                            connectionString,
                            recipientOwnerLogin,
                            senderTitle,
                            textOneLine,
                            new Dictionary<string, string>
                            {
                                ["type"] = "chat",
                                ["threadId"] = reciprocalThreadId.ToString(),
                                ["senderLogin"] = senderOwnerLogin,
                                ["threadTitle"] = senderTitle
                            });
                    }
                }
            }

            var msg = new MessageItem(
                Id: newId,
                SenderType: "user",
                SenderId: normalizedLogin,
                SenderName: await ResolveDisplayNameByLoginAsync(connection, normalizedLogin),
                Text: text,
                CreatedAtUtc: DateTime.UtcNow,
                MetaJson: request.MetaJson,
                SenderIsTechAdmin: await IsTechAdminAsync(connection, normalizedLogin),
                IsRead: false
            );
            return Ok(new SendMessageResponse(true, "OK", msg));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return Ok(new SendMessageResponse(false, $"Ошибка: {ex.GetType().Name}: {ex.Message}", null));
        }
    }

    [HttpPost("threads/{threadId:int}/media")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ChatMediaUploadResponse>> UploadChatMedia(int threadId, [FromQuery] string login, IFormFile? file)
    {
        if (string.IsNullOrWhiteSpace(login))
            return BadRequest(new ChatMediaUploadResponse(false, "Укажите логин", null, null, null));
        if (threadId <= 0)
            return BadRequest(new ChatMediaUploadResponse(false, "Некорректный threadId", null, null, null));
        if (file == null || file.Length <= 0)
            return BadRequest(new ChatMediaUploadResponse(false, "Выберите файл", null, null, null));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return StatusCode(500, new ChatMediaUploadResponse(false, "Не настроено подключение к БД", null, null, null));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await EnsureChatTablesAsync(connection);

            var ownerLogin = await ResolveOwnerLoginAsync(connection, login.Trim());

            const string ownSql = @"SELECT TOP 1 1 FROM [App_Threads] WHERE [Id] = @Id AND [OwnerLogin] = @Login;";
            await using (var own = new SqlCommand(ownSql, connection))
            {
                own.Parameters.AddWithValue("@Id", threadId);
                own.Parameters.AddWithValue("@Login", ownerLogin);
                var o = await own.ExecuteScalarAsync();
                if (o == null || o == DBNull.Value)
                    return Ok(new ChatMediaUploadResponse(false, "Диалог не найден", null, null, null));
            }

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? "";
            if (string.IsNullOrWhiteSpace(ext) || ext.Length > 10) ext = ".bin";

            var ct = (file.ContentType ?? "").ToLowerInvariant();
            var isVideo = ct.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
                          ext is ".mp4" or ".mov" or ".webm" or ".m4v" or ".3gp";
            var isImage = ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                          ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif";

            if (!isVideo && !isImage)
                return Ok(new ChatMediaUploadResponse(false, "Допустимы фото или видео (jpg, png, webp, gif, mp4…)", null, null, null));

            var kind = isVideo ? "video" : "image";
            var url = await SaveChatMediaFileAsync(file);
            var mime = string.IsNullOrWhiteSpace(file.ContentType) ? (isVideo ? "video/mp4" : "image/jpeg") : file.ContentType;
            return Ok(new ChatMediaUploadResponse(true, "OK", url, mime, kind));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return Ok(new ChatMediaUploadResponse(false, $"Ошибка: {ex.GetType().Name}: {ex.Message}", null, null, null));
        }
    }

    [HttpDelete("threads/{threadId:int}/messages/{messageId:int}")]
    public async Task<ActionResult<DeleteMessageResponse>> DeleteMessage(int threadId, int messageId, [FromQuery] string login)
    {
        if (string.IsNullOrWhiteSpace(login))
            return BadRequest(new DeleteMessageResponse(false, "Укажите логин"));
        if (threadId <= 0 || messageId <= 0)
            return BadRequest(new DeleteMessageResponse(false, "Некорректные параметры"));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return StatusCode(500, new DeleteMessageResponse(false, "Не настроено подключение к БД"));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsureChatTablesAsync(connection);
            await EnsureThreadReadsTableAsync(connection);

            var normalizedLogin = login.Trim();
            var ownerLogin = await ResolveOwnerLoginAsync(connection, normalizedLogin);

            // Authorization: thread must belong to ownerLogin
            const string ownSql = @"SELECT TOP 1 1 FROM [App_Threads] WHERE [Id] = @Id AND [OwnerLogin] = @Login;";
            await using (var own = new SqlCommand(ownSql, connection))
            {
                own.Parameters.AddWithValue("@Id", threadId);
                own.Parameters.AddWithValue("@Login", ownerLogin);
                var ok = await own.ExecuteScalarAsync();
                if (ok == null) return Ok(new DeleteMessageResponse(false, "Диалог не найден"));
            }

            // Only allow deleting own messages (senderId = login). (Later can extend for admins/bots.)
            const string delSql = @"
                DELETE FROM [App_Messages]
                WHERE [Id] = @Id AND [ThreadId] = @ThreadId AND [SenderType] = 'user' AND [SenderId] = @SenderId;
                SELECT @@ROWCOUNT;";
            int rows;
            await using (var del = new SqlCommand(delSql, connection))
            {
                del.Parameters.AddWithValue("@Id", messageId);
                del.Parameters.AddWithValue("@ThreadId", threadId);
                del.Parameters.AddWithValue("@SenderId", normalizedLogin);
                var o = await del.ExecuteScalarAsync();
                rows = Convert.ToInt32(o ?? 0);
            }

            return Ok(rows > 0
                ? new DeleteMessageResponse(true, "OK")
                : new DeleteMessageResponse(false, "Нельзя удалить это сообщение"));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return Ok(new DeleteMessageResponse(false, $"Ошибка: {ex.GetType().Name}: {ex.Message}"));
        }
    }

    [HttpDelete("threads/{threadId:int}/history")]
    [HttpPost("threads/{threadId:int}/history/clear")]
    public async Task<ActionResult<ClearThreadHistoryResponse>> ClearThreadHistory(int threadId, [FromQuery] string login)
    {
        if (string.IsNullOrWhiteSpace(login))
            return Ok(new ClearThreadHistoryResponse(false, "Укажите логин"));
        if (threadId <= 0)
            return Ok(new ClearThreadHistoryResponse(false, "Некорректный threadId"));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return Ok(new ClearThreadHistoryResponse(false, "Не настроено подключение к БД"));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await EnsureChatTablesAsync(connection);
            await EnsureThreadReadsTableAsync(connection);

            var ownerLogin = await ResolveOwnerLoginAsync(connection, login.Trim());

            const string ownSql = @"SELECT TOP 1 1 FROM [App_Threads] WHERE [Id] = @Id AND [OwnerLogin] = @Login;";
            await using (var own = new SqlCommand(ownSql, connection))
            {
                own.Parameters.AddWithValue("@Id", threadId);
                own.Parameters.AddWithValue("@Login", ownerLogin);
                var ok = await own.ExecuteScalarAsync();
                if (ok == null) return Ok(new ClearThreadHistoryResponse(false, "Диалог не найден"));
            }

            using var tx = connection.BeginTransaction();
            try
            {
                const string delMirror = @"
                    DELETE FROM [App_MessageMirror]
                    WHERE [OwnerMessageId] IN (SELECT [Id] FROM [App_Messages] WHERE [ThreadId] = @T)
                       OR [RecipientMessageId] IN (SELECT [Id] FROM [App_Messages] WHERE [ThreadId] = @T);";
                await using (var m = new SqlCommand(delMirror, connection, tx))
                {
                    m.Parameters.AddWithValue("@T", threadId);
                    await m.ExecuteNonQueryAsync();
                }

                const string delMsg = @"DELETE FROM [App_Messages] WHERE [ThreadId] = @T;";
                await using (var d = new SqlCommand(delMsg, connection, tx))
                {
                    d.Parameters.AddWithValue("@T", threadId);
                    await d.ExecuteNonQueryAsync();
                }

                const string resetRead = @"
                    UPDATE [App_ThreadReads] SET [LastReadMessageId] = 0, [UpdatedAt] = GETUTCDATE()
                    WHERE [ThreadId] = @T;";
                await using (var rr = new SqlCommand(resetRead, connection, tx))
                {
                    rr.Parameters.AddWithValue("@T", threadId);
                    await rr.ExecuteNonQueryAsync();
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { /* ignore */ }
                throw;
            }

            return Ok(new ClearThreadHistoryResponse(true, "OK"));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return Ok(new ClearThreadHistoryResponse(false, $"Ошибка: {ex.GetType().Name}: {ex.Message}"));
        }
    }

    [HttpGet("bots/{botId}/profile")]
    public async Task<ActionResult<BotProfileResponse>> GetBotProfile(string botId, [FromQuery] string login)
    {
        if (string.IsNullOrWhiteSpace(botId) || string.IsNullOrWhiteSpace(login))
            return BadRequest(new BotProfileResponse(false, "Укажите botId и login", null));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return StatusCode(500, new BotProfileResponse(false, "Не настроено подключение к БД", null));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await EnsureChatTablesAsync(connection);

            var ownerLogin = await ResolveOwnerLoginAsync(connection, login.Trim());
            var safeBotId = botId.Trim();

            const string ownSql = @"SELECT TOP 1 1 FROM [App_Threads] WHERE [OwnerLogin] = @Login AND [BotId] = @BotId;";
            await using (var own = new SqlCommand(ownSql, connection))
            {
                own.Parameters.AddWithValue("@Login", ownerLogin);
                own.Parameters.AddWithValue("@BotId", safeBotId);
                var ok = await own.ExecuteScalarAsync();
                if (ok == null) return Ok(new BotProfileResponse(false, "Бот не найден", null));
            }

            var profile = await GetBotProfileInternalAsync(connection, safeBotId);
            return Ok(new BotProfileResponse(true, "OK", profile));
        }
        catch (Exception ex)
        {
            return Ok(new BotProfileResponse(false, $"Ошибка: {ex.GetType().Name}: {ex.Message}", null));
        }
    }

    [HttpPost("bots/{botId}/profile")]
    public async Task<ActionResult<UpdateBotProfileResponse>> UpdateBotProfile(string botId, [FromBody] UpdateBotProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(botId) || request == null || string.IsNullOrWhiteSpace(request.Login))
            return BadRequest(new UpdateBotProfileResponse(false, "Укажите botId и login", null));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return StatusCode(500, new UpdateBotProfileResponse(false, "Не настроено подключение к БД", null));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await EnsureChatTablesAsync(connection);

            var isAdmin = await IsTechAdminAsync(connection, request.Login.Trim());
            if (!isAdmin) return Ok(new UpdateBotProfileResponse(false, "Только техадмин может редактировать профиль бота", null));

            var safeBotId = botId.Trim();
            const string upsertSql = @"
                IF EXISTS (SELECT 1 FROM [App_BotProfiles] WHERE [BotId] = @BotId)
                    UPDATE [App_BotProfiles]
                    SET [DisplayName] = COALESCE(@DisplayName, [DisplayName]),
                        [Description] = @Description,
                        [IsOfficial] = COALESCE(@IsOfficial, [IsOfficial]),
                        [UpdatedAt] = GETUTCDATE()
                    WHERE [BotId] = @BotId;
                ELSE
                    INSERT INTO [App_BotProfiles] ([BotId], [DisplayName], [Description], [AvatarUrl], [IsOfficial], [UpdatedAt])
                    VALUES (@BotId, COALESCE(@DisplayName, @BotId), @Description, NULL, COALESCE(@IsOfficial, 0), GETUTCDATE());";
            await using (var cmd = new SqlCommand(upsertSql, connection))
            {
                cmd.Parameters.AddWithValue("@BotId", safeBotId);
                cmd.Parameters.AddWithValue("@DisplayName", (object?)request.DisplayName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Description", (object?)request.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IsOfficial", request.IsOfficial.HasValue ? (request.IsOfficial.Value ? 1 : 0) : DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            var profile = await GetBotProfileInternalAsync(connection, safeBotId);
            return Ok(new UpdateBotProfileResponse(true, "OK", profile));
        }
        catch (Exception ex)
        {
            return Ok(new UpdateBotProfileResponse(false, $"Ошибка: {ex.GetType().Name}: {ex.Message}", null));
        }
    }

    [HttpPost("bots/{botId}/avatar")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<UpdateBotProfileResponse>> UploadBotAvatar(string botId, [FromQuery] string login, IFormFile? file)
    {
        if (string.IsNullOrWhiteSpace(botId) || string.IsNullOrWhiteSpace(login) || file == null || file.Length == 0)
            return BadRequest(new UpdateBotProfileResponse(false, "Укажите botId/login и файл", null));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return StatusCode(500, new UpdateBotProfileResponse(false, "Не настроено подключение к БД", null));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await EnsureChatTablesAsync(connection);

            var isAdmin = await IsTechAdminAsync(connection, login.Trim());
            if (!isAdmin) return Ok(new UpdateBotProfileResponse(false, "Только техадмин может менять фото бота", null));

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif"))
                return Ok(new UpdateBotProfileResponse(false, "Допустимы: jpg, png, webp, gif", null));

            var dir = Path.Combine(_env.WebRootPath ?? "", "uploads", "bots");
            Directory.CreateDirectory(dir);
            var safeBotId = new string(botId.Trim().Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrWhiteSpace(safeBotId)) safeBotId = "bot";
            var fileName = $"{safeBotId}_{Guid.NewGuid():N}{ext}";
            var physical = Path.Combine(dir, fileName);
            await using (var fs = new FileStream(physical, FileMode.Create, FileAccess.Write))
            {
                await file.CopyToAsync(fs);
            }

            var req = Request;
            var baseUrl = $"{req.Scheme}://{req.Host}{req.PathBase}";
            var avatarUrl = $"{baseUrl}/uploads/bots/{Uri.EscapeDataString(fileName)}";

            const string upsertSql = @"
                IF EXISTS (SELECT 1 FROM [App_BotProfiles] WHERE [BotId] = @BotId)
                    UPDATE [App_BotProfiles]
                    SET [AvatarUrl] = @AvatarUrl, [UpdatedAt] = GETUTCDATE()
                    WHERE [BotId] = @BotId;
                ELSE
                    INSERT INTO [App_BotProfiles] ([BotId], [DisplayName], [Description], [AvatarUrl], [IsOfficial], [UpdatedAt])
                    VALUES (@BotId, @BotId, NULL, @AvatarUrl, 0, GETUTCDATE());";
            await using (var cmd = new SqlCommand(upsertSql, connection))
            {
                cmd.Parameters.AddWithValue("@BotId", botId.Trim());
                cmd.Parameters.AddWithValue("@AvatarUrl", avatarUrl);
                await cmd.ExecuteNonQueryAsync();
            }

            var profile = await GetBotProfileInternalAsync(connection, botId.Trim());
            return Ok(new UpdateBotProfileResponse(true, "OK", profile));
        }
        catch (Exception ex)
        {
            return Ok(new UpdateBotProfileResponse(false, $"Ошибка: {ex.GetType().Name}: {ex.Message}", null));
        }
    }

    private static async Task<string> ResolveOwnerLoginAsync(SqlConnection connection, string loginOrEmployeeId)
    {
        if (string.IsNullOrWhiteSpace(loginOrEmployeeId)) return "";
        var key = loginOrEmployeeId.Trim();

        // If threads already exist for this key, use it as-is.
        const string hasThreadsSql = @"SELECT TOP 1 1 FROM [App_Threads] WHERE [OwnerLogin] = @Login;";
        await using (var has = new SqlCommand(hasThreadsSql, connection))
        {
            has.Parameters.AddWithValue("@Login", key);
            var o = await has.ExecuteScalarAsync();
            if (o != null && o != DBNull.Value) return key;
        }

        // If key looks like employeeId (digits), try to resolve to stored card login from Lexema.
        if (key.All(char.IsDigit))
        {
            try
            {
                const string sql = @"
                    SELECT TOP 1 COALESCE(TRY_CONVERT(nvarchar(200), [Логин]), '')
                    FROM [Lexema_Кадры_ЛичнаяКарточка]
                    WHERE TRY_CONVERT(nvarchar(50), [ТабельныйНомер]) = @EmployeeId;";
                await using var cmd = new SqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@EmployeeId", key);
                var o = await cmd.ExecuteScalarAsync();
                var resolved = (o == null || o == DBNull.Value) ? "" : Convert.ToString(o)?.Trim();
                if (!string.IsNullOrWhiteSpace(resolved)) return resolved!;
            }
            catch
            {
                // Lexema может быть вне DefaultConnection или без прав. В этом случае работаем по key как есть.
            }
        }

        return key;
    }

    private static async Task<bool> IsTechAdminAsync(SqlConnection connection, string loginOrEmployeeId)
    {
        var key = loginOrEmployeeId.Trim();
        if (string.IsNullOrWhiteSpace(key)) return false;

        var resolved = key;
        if (key.All(char.IsDigit))
        {
            try
            {
                const string resolveSql = @"
                    SELECT TOP 1 COALESCE(TRY_CONVERT(nvarchar(200), [Логин]), '')
                    FROM [Lexema_Кадры_ЛичнаяКарточка]
                    WHERE TRY_CONVERT(nvarchar(50), [ТабельныйНомер]) = @EmployeeId;";
                await using var resolveCmd = new SqlCommand(resolveSql, connection);
                resolveCmd.Parameters.AddWithValue("@EmployeeId", key);
                var o = await resolveCmd.ExecuteScalarAsync();
                var login = (o == null || o == DBNull.Value) ? "" : Convert.ToString(o)?.Trim();
                if (!string.IsNullOrWhiteSpace(login)) resolved = login!;
            }
            catch
            {
            }
        }

        const string sql = @"SELECT TOP 1 ISNULL([CanTechAdmin], 0) FROM [App_UserPermissions] WHERE [Login] = @Login;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", resolved);
        var v = await cmd.ExecuteScalarAsync();
        return v != null && v != DBNull.Value && Convert.ToInt32(v) == 1;
    }

    private static async Task<int> EnsureDirectUserThreadAsync(SqlConnection connection, string ownerLogin, string peerLogin)
    {
        const string existingSql = @"
            SELECT TOP 1 [Id]
            FROM [App_Threads]
            WHERE [OwnerLogin] = @OwnerLogin
              AND ISNULL([Type], 'bot') = 'user'
              AND [PeerLogin] = @PeerLogin;";
        await using (var existing = new SqlCommand(existingSql, connection))
        {
            existing.Parameters.AddWithValue("@OwnerLogin", ownerLogin);
            existing.Parameters.AddWithValue("@PeerLogin", peerLogin);
            var o = await existing.ExecuteScalarAsync();
            if (o != null && o != DBNull.Value) return Convert.ToInt32(o);
        }

        var title = await ResolveDisplayNameByLoginAsync(connection, peerLogin);
        const string insertSql = @"
            INSERT INTO [App_Threads] ([OwnerLogin], [Type], [Title], [BotId], [PeerLogin], [PeerEmployeeId], [CreatedAt])
            VALUES (@OwnerLogin, 'user', @Title, NULL, @PeerLogin, NULL, GETUTCDATE());
            SELECT SCOPE_IDENTITY();";
        await using var ins = new SqlCommand(insertSql, connection);
        ins.Parameters.AddWithValue("@OwnerLogin", ownerLogin);
        ins.Parameters.AddWithValue("@Title", title);
        ins.Parameters.AddWithValue("@PeerLogin", peerLogin);
        var id = await ins.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    private static async Task<int> GetDirectThreadIdAsync(SqlConnection connection, string ownerLogin, string peerLogin)
    {
        const string sql = @"
            SELECT TOP 1 [Id]
            FROM [App_Threads]
            WHERE [OwnerLogin] = @OwnerLogin
              AND ISNULL([Type], 'bot') = 'user'
              AND [PeerLogin] = @PeerLogin;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@OwnerLogin", ownerLogin);
        cmd.Parameters.AddWithValue("@PeerLogin", peerLogin);
        var o = await cmd.ExecuteScalarAsync();
        return (o == null || o == DBNull.Value) ? 0 : Convert.ToInt32(o);
    }

    private static async Task UpsertMessageMirrorAsync(SqlConnection connection, int ownerMessageId, int recipientMessageId)
    {
        const string sql = @"
            IF EXISTS (SELECT 1 FROM [App_MessageMirror] WHERE [OwnerMessageId] = @OwnerMessageId)
                UPDATE [App_MessageMirror]
                SET [RecipientMessageId] = @RecipientMessageId,
                    [UpdatedAt] = GETUTCDATE()
                WHERE [OwnerMessageId] = @OwnerMessageId
            ELSE
                INSERT INTO [App_MessageMirror] ([OwnerMessageId], [RecipientMessageId], [UpdatedAt])
                VALUES (@OwnerMessageId, @RecipientMessageId, GETUTCDATE());";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@OwnerMessageId", ownerMessageId);
        cmd.Parameters.AddWithValue("@RecipientMessageId", recipientMessageId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> IsMirroredMessageReadByOwnerAsync(SqlConnection connection, int ownerMessageId, int recipientThreadId, string recipientOwnerLogin)
    {
        const string sql = @"
            SELECT TOP 1
                CASE
                    WHEN ISNULL(R.[LastReadMessageId], 0) >= M.[RecipientMessageId] THEN 1
                    ELSE 0
                END
            FROM [App_MessageMirror] M
            LEFT JOIN [App_ThreadReads] R
              ON R.[ThreadId] = @RecipientThreadId AND R.[Login] = @RecipientOwnerLogin
            WHERE M.[OwnerMessageId] = @OwnerMessageId;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@OwnerMessageId", ownerMessageId);
        cmd.Parameters.AddWithValue("@RecipientThreadId", recipientThreadId);
        cmd.Parameters.AddWithValue("@RecipientOwnerLogin", recipientOwnerLogin);
        var o = await cmd.ExecuteScalarAsync();
        return o != null && o != DBNull.Value && Convert.ToInt32(o) == 1;
    }

    private static async Task<string> ResolveDisplayNameByLoginAsync(SqlConnection connection, string login)
    {
        const string sql = @"
            SELECT TOP 1
                LTRIM(RTRIM(CONCAT(
                    COALESCE(TRY_CONVERT(nvarchar(200), [Фамилия]), ''),
                    CASE WHEN COALESCE(TRY_CONVERT(nvarchar(200), [Имя]), '') = '' THEN '' ELSE ' ' + COALESCE(TRY_CONVERT(nvarchar(200), [Имя]), '') END
                )))
            FROM [Lexema_Кадры_ЛичнаяКарточка]
            WHERE [Логин] = @Login;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        var o = await cmd.ExecuteScalarAsync();
        var name = (o == null || o == DBNull.Value) ? "" : Convert.ToString(o)?.Trim();
        return string.IsNullOrWhiteSpace(name) ? login : name!;
    }

    private string? BuildUserAvatarPublicUrl(string? avatarFileName)
    {
        if (string.IsNullOrWhiteSpace(avatarFileName)) return null;
        var req = Request;
        var baseUrl = $"{req.Scheme}://{req.Host}{req.PathBase}";
        return $"{baseUrl}/uploads/avatars/{Uri.EscapeDataString(avatarFileName)}";
    }

    private static async Task EnsureAppUserProfileTableAsync(SqlConnection connection)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_UserProfile')
            CREATE TABLE [App_UserProfile] (
                [Login] NVARCHAR(100) NOT NULL PRIMARY KEY,
                [AvatarFileName] NVARCHAR(260) NULL,
                [Experience] INT NOT NULL DEFAULT 0,
                [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );";
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<ThreadItem?> GetThreadByIdAsync(SqlConnection connection, string ownerLogin, int threadId)
    {
        var sql = $@"
            SELECT T.[Id], T.[Type], T.[Title], T.[BotId], T.[CreatedAt],
                   M.[Text] AS LastText, M.[CreatedAt] AS LastAt, M.[MetaJson] AS LastMetaJson,
                   M.[SenderType] AS LastSenderType, M.[SenderId] AS LastSenderId,
                   M.[Id] AS LastMessageId,
                   T.[PeerLogin] AS PeerLogin,
                   ISNULL(U.UnreadCount, 0) AS UnreadCount,
                   ISNULL(P.[CanTechAdmin], 0) AS IsTechAdmin,
                   ISNULL(B.[IsOfficial], 0) AS IsOfficialBot,
                   CASE
                       WHEN ISNULL(T.[Type], 'bot') = 'user' THEN
                           CASE
                               WHEN UP.[AvatarFileName] IS NULL OR LTRIM(RTRIM(UP.[AvatarFileName])) = '' THEN NULL
                               ELSE UP.[AvatarFileName]
                           END
                       ELSE B.[AvatarUrl]
                   END AS AvatarRaw
            FROM [App_Threads] T
            OUTER APPLY (
                SELECT TOP 1 [Id], [Text], [CreatedAt], [MetaJson], [SenderType], [SenderId]
                FROM [App_Messages] MM
                WHERE MM.[ThreadId] = T.[Id]
                ORDER BY MM.[Id] DESC
            ) M
            OUTER APPLY (
                SELECT COUNT(1) AS UnreadCount
                FROM [App_Messages] MM2
                LEFT JOIN [App_ThreadReads] R ON R.[ThreadId] = T.[Id] AND R.[Login] = @Login
                WHERE MM2.[ThreadId] = T.[Id]
                  AND MM2.[Id] > ISNULL(R.[LastReadMessageId], 0)
            ) U
            LEFT JOIN [App_UserPermissions] P ON P.[Login] = COALESCE(T.[PeerLogin], T.[Title])
            LEFT JOIN [App_BotProfiles] B ON B.[BotId] = T.[BotId]
            LEFT JOIN [App_UserProfile] UP ON UP.[Login] = T.[PeerLogin]
            WHERE T.[OwnerLogin] = @Login AND T.[Id] = @ThreadId
              AND {LegacyPollSurveyBotThreadExcludeSql("T")};";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", ownerLogin);
        cmd.Parameters.AddWithValue("@ThreadId", threadId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var avatarRaw = reader.IsDBNull(15) ? null : reader.GetString(15);
        var avatarUrl = avatarRaw;
        if (!string.IsNullOrWhiteSpace(avatarRaw) &&
            !avatarRaw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !avatarRaw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            avatarUrl = BuildUserAvatarPublicUrl(avatarRaw);
        }

        var lastFromSelf = false;
        if (!reader.IsDBNull(8) &&
            string.Equals(reader.GetString(8), "user", StringComparison.OrdinalIgnoreCase))
        {
            var sid = reader.IsDBNull(9) ? "" : reader.GetString(9).Trim();
            lastFromSelf = !string.IsNullOrEmpty(sid) &&
                string.Equals(sid, ownerLogin, StringComparison.OrdinalIgnoreCase);
        }

        var threadType = reader.IsDBNull(1) ? "bot" : reader.GetString(1);
        var peerLogin = reader.IsDBNull(11) ? null : reader.GetString(11);
        var lastMessageId = reader.IsDBNull(10) ? 0 : Convert.ToInt32(reader.GetValue(10));
        var lastMessageIsRead = false;
        if (lastFromSelf &&
            threadType.Equals("user", StringComparison.OrdinalIgnoreCase) &&
            lastMessageId > 0 &&
            !string.IsNullOrWhiteSpace(peerLogin))
        {
            var recipientThreadId = await GetDirectThreadIdAsync(connection, peerLogin.Trim(), ownerLogin);
            if (recipientThreadId > 0)
            {
                lastMessageIsRead = await IsMirroredMessageReadByOwnerAsync(connection, lastMessageId, recipientThreadId, peerLogin.Trim());
            }
        }

        var rawLastText = reader.IsDBNull(5) ? null : _cipher.UnprotectFieldNullable(reader.GetString(5));
        var rawLastMeta = reader.IsDBNull(7) ? null : _cipher.UnprotectFieldNullable(reader.GetString(7));
        var lastPreview = BuildThreadLastPreview(rawLastText, rawLastMeta);

        return new ThreadItem(
            Id: reader.GetInt32(0),
            Type: threadType,
            Title: reader.IsDBNull(2) ? "" : reader.GetString(2),
            BotId: reader.IsDBNull(3) ? null : reader.GetString(3),
            CreatedAtUtc: reader.GetDateTime(4),
            LastMessageText: lastPreview,
            LastMessageAtUtc: reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            LastMessageFromSelf: lastFromSelf,
            LastMessageIsRead: lastMessageIsRead,
            UnreadCount: reader.IsDBNull(12) ? 0 : Convert.ToInt32(reader.GetValue(12)),
            IsTechAdmin: !reader.IsDBNull(13) && Convert.ToInt32(reader.GetValue(13)) == 1,
            IsOfficialBot: !reader.IsDBNull(14) && Convert.ToInt32(reader.GetValue(14)) == 1,
            AvatarUrl: avatarUrl
        );
    }

    public static async Task EnsureChatTablesAsync(SqlConnection connection)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_Threads')
            CREATE TABLE [App_Threads] (
                [Id] INT IDENTITY(1,1) PRIMARY KEY,
                [OwnerLogin] NVARCHAR(100) NOT NULL,
                [Type] NVARCHAR(20) NOT NULL DEFAULT 'bot',
                [Title] NVARCHAR(200) NOT NULL,
                [BotId] NVARCHAR(100) NULL,
                [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_Messages')
            CREATE TABLE [App_Messages] (
                [Id] INT IDENTITY(1,1) PRIMARY KEY,
                [ThreadId] INT NOT NULL,
                [SenderType] NVARCHAR(20) NOT NULL DEFAULT 'system',
                [SenderId] NVARCHAR(100) NULL,
                [Text] NVARCHAR(MAX) NOT NULL,
                [MetaJson] NVARCHAR(MAX) NULL,
                [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_MessageMirror')
            CREATE TABLE [App_MessageMirror] (
                [OwnerMessageId] INT NOT NULL PRIMARY KEY,
                [RecipientMessageId] INT NOT NULL,
                [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_UserPermissions')
            CREATE TABLE [App_UserPermissions] (
                [Login] NVARCHAR(100) NOT NULL PRIMARY KEY,
                [CanCreatePosts] BIT NOT NULL DEFAULT 0,
                [CanTechAdmin] BIT NOT NULL DEFAULT 0,
                [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );
            IF COL_LENGTH('App_UserPermissions', 'CanTechAdmin') IS NULL
                ALTER TABLE [App_UserPermissions] ADD [CanTechAdmin] BIT NOT NULL CONSTRAINT [DF_App_UserPermissions_CanTechAdmin] DEFAULT 0;

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_BotProfiles')
            CREATE TABLE [App_BotProfiles] (
                [BotId] NVARCHAR(100) NOT NULL PRIMARY KEY,
                [DisplayName] NVARCHAR(200) NOT NULL,
                [Description] NVARCHAR(1000) NULL,
                [AvatarUrl] NVARCHAR(500) NULL,
                [IsOfficial] BIT NOT NULL DEFAULT 0,
                [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );
            IF COL_LENGTH('App_BotProfiles', 'DisplayName') IS NULL
                ALTER TABLE [App_BotProfiles] ADD [DisplayName] NVARCHAR(200) NOT NULL CONSTRAINT [DF_App_BotProfiles_DisplayName] DEFAULT 'Bot';
            IF COL_LENGTH('App_BotProfiles', 'Description') IS NULL
                ALTER TABLE [App_BotProfiles] ADD [Description] NVARCHAR(1000) NULL;
            IF COL_LENGTH('App_BotProfiles', 'AvatarUrl') IS NULL
                ALTER TABLE [App_BotProfiles] ADD [AvatarUrl] NVARCHAR(500) NULL;
            IF COL_LENGTH('App_BotProfiles', 'IsOfficial') IS NULL
                ALTER TABLE [App_BotProfiles] ADD [IsOfficial] BIT NOT NULL CONSTRAINT [DF_App_BotProfiles_IsOfficial] DEFAULT 0;
            IF COL_LENGTH('App_BotProfiles', 'UpdatedAt') IS NULL
                ALTER TABLE [App_BotProfiles] ADD [UpdatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_App_BotProfiles_UpdatedAt] DEFAULT GETUTCDATE();

            IF NOT EXISTS (SELECT 1 FROM [App_BotProfiles] WHERE [BotId] = 'StekloSecurity')
                INSERT INTO [App_BotProfiles] ([BotId], [DisplayName], [Description], [AvatarUrl], [IsOfficial], [UpdatedAt])
                VALUES ('StekloSecurity', 'StekloSecurity', N'Служебный бот безопасности аккаунта', NULL, 1, GETUTCDATE());

            IF NOT EXISTS (SELECT 1 FROM [App_BotProfiles] WHERE [BotId] = 'StekloMonitor')
                INSERT INTO [App_BotProfiles] ([BotId], [DisplayName], [Description], [AvatarUrl], [IsOfficial], [UpdatedAt])
                VALUES ('StekloMonitor', N'Мониторинг', N'Для тех. админов: команда /stats — сводка по сообщениям, постам и событиям безопасности.', NULL, 1, GETUTCDATE());

            -- Lightweight schema migrations (in case tables existed from older versions)
            IF COL_LENGTH('App_Threads', 'Type') IS NULL
                ALTER TABLE [App_Threads] ADD [Type] NVARCHAR(20) NOT NULL CONSTRAINT [DF_App_Threads_Type] DEFAULT 'bot';
            IF COL_LENGTH('App_Threads', 'BotId') IS NULL
                ALTER TABLE [App_Threads] ADD [BotId] NVARCHAR(100) NULL;
            IF COL_LENGTH('App_Threads', 'CreatedAt') IS NULL
                ALTER TABLE [App_Threads] ADD [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_App_Threads_CreatedAt] DEFAULT GETUTCDATE();
            IF COL_LENGTH('App_Threads', 'PeerLogin') IS NULL
                ALTER TABLE [App_Threads] ADD [PeerLogin] NVARCHAR(100) NULL;
            IF COL_LENGTH('App_Threads', 'PeerEmployeeId') IS NULL
                ALTER TABLE [App_Threads] ADD [PeerEmployeeId] NVARCHAR(50) NULL;

            IF COL_LENGTH('App_Messages', 'SenderType') IS NULL
                ALTER TABLE [App_Messages] ADD [SenderType] NVARCHAR(20) NOT NULL CONSTRAINT [DF_App_Messages_SenderType] DEFAULT 'system';
            IF COL_LENGTH('App_Messages', 'SenderId') IS NULL
                ALTER TABLE [App_Messages] ADD [SenderId] NVARCHAR(100) NULL;
            IF COL_LENGTH('App_Messages', 'MetaJson') IS NULL
                ALTER TABLE [App_Messages] ADD [MetaJson] NVARCHAR(MAX) NULL;
            IF COL_LENGTH('App_Messages', 'CreatedAt') IS NULL
                ALTER TABLE [App_Messages] ADD [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_App_Messages_CreatedAt] DEFAULT GETUTCDATE();

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_App_Messages_ThreadId_Id' AND object_id = OBJECT_ID('App_Messages'))
                CREATE INDEX [IX_App_Messages_ThreadId_Id] ON [App_Messages]([ThreadId], [Id] DESC);

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_AppMigrations')
            CREATE TABLE [App_AppMigrations] (
                [Name] NVARCHAR(128) NOT NULL PRIMARY KEY,
                [AppliedAtUtc] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );";

        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
        await RemoveLegacyPollSurveyBotDataAsync(connection);
    }

    private static async Task<BotProfileItem> GetBotProfileInternalAsync(SqlConnection connection, string botId)
    {
        const string sql = @"
            SELECT TOP 1
                COALESCE([DisplayName], @BotId),
                [Description],
                [AvatarUrl],
                ISNULL([IsOfficial], 0)
            FROM [App_BotProfiles]
            WHERE [BotId] = @BotId;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@BotId", botId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            return new BotProfileItem(
                BotId: botId,
                DisplayName: r.IsDBNull(0) ? botId : r.GetString(0),
                Description: r.IsDBNull(1) ? null : r.GetString(1),
                AvatarUrl: r.IsDBNull(2) ? null : r.GetString(2),
                IsOfficial: !r.IsDBNull(3) && Convert.ToInt32(r.GetValue(3)) == 1
            );
        }
        return new BotProfileItem(botId, botId, null, null, false);
    }

    private static async Task EnsureThreadReadsTableAsync(SqlConnection connection)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_ThreadReads')
            CREATE TABLE [App_ThreadReads] (
                [Login] NVARCHAR(100) NOT NULL,
                [ThreadId] INT NOT NULL,
                [LastReadMessageId] INT NOT NULL DEFAULT 0,
                [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT [PK_App_ThreadReads] PRIMARY KEY ([Login], [ThreadId])
            );

            -- Lightweight schema migrations
            IF COL_LENGTH('App_ThreadReads', 'LastReadMessageId') IS NULL
                ALTER TABLE [App_ThreadReads] ADD [LastReadMessageId] INT NOT NULL CONSTRAINT [DF_App_ThreadReads_LastReadMessageId] DEFAULT 0;
            IF COL_LENGTH('App_ThreadReads', 'UpdatedAt') IS NULL
                ALTER TABLE [App_ThreadReads] ADD [UpdatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_App_ThreadReads_UpdatedAt] DEFAULT GETUTCDATE();";
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task RemoveLegacyPollSurveyBotDataAsync(SqlConnection connection)
    {
        var matchThreads = $"SELECT [Id] FROM [App_Threads] WHERE {LegacyPollSurveyBotThreadMatchSql("")}";
        var sql = $@"
            DELETE M FROM [App_MessageMirror] M
            WHERE EXISTS (
                SELECT 1 FROM [App_Messages] X WHERE X.[Id] = M.[OwnerMessageId]
                  AND X.[ThreadId] IN ({matchThreads})
            )
            OR EXISTS (
                SELECT 1 FROM [App_Messages] X WHERE X.[Id] = M.[RecipientMessageId]
                  AND X.[ThreadId] IN ({matchThreads})
            );

            DELETE FROM [App_ThreadReads] WHERE [ThreadId] IN ({matchThreads});

            DELETE FROM [App_Messages] WHERE [ThreadId] IN ({matchThreads});

            DELETE FROM [App_Threads] WHERE {LegacyPollSurveyBotThreadMatchSql("")};

            DELETE FROM [App_BotProfiles] WHERE [BotId] IN ({LegacyPollSurveyBotsInSql});";
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task MaybeEncryptLegacyChatMessagesAsync(SqlConnection connection)
    {
        if (!_cipher.IsEnabled) return;

        const string checkSql = @"SELECT TOP 1 1 FROM [App_AppMigrations] WHERE [Name] = N'ChatEncryptV1'";
        await using (var chk = new SqlCommand(checkSql, connection))
        {
            var o = await chk.ExecuteScalarAsync();
            if (o != null && o != DBNull.Value) return;
        }

        const string selSql = @"
            SELECT TOP 300 [Id], [Text], [MetaJson] FROM [App_Messages]
            WHERE ([Text] IS NOT NULL AND [Text] <> N'' AND [Text] NOT LIKE N'enc1:%')
               OR ([MetaJson] IS NOT NULL AND [MetaJson] <> N'' AND [MetaJson] NOT LIKE N'enc1:%');";

        while (true)
        {
            var batch = new List<(int Id, string? Text, string? Meta)>();
            await using (var sel = new SqlCommand(selSql, connection))
            await using (var r = await sel.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    batch.Add((
                        r.GetInt32(0),
                        r.IsDBNull(1) ? null : r.GetString(1),
                        r.IsDBNull(2) ? null : r.GetString(2)));
                }
            }

            if (batch.Count == 0) break;

            foreach (var row in batch)
            {
                var newText = row.Text;
                if (!string.IsNullOrEmpty(newText) && !newText.StartsWith(ChatMessageCipher.Prefix, StringComparison.Ordinal))
                    newText = _cipher.ProtectField(newText);
                else if (newText == null)
                    newText = "";

                string? newMeta = row.Meta;
                if (!string.IsNullOrEmpty(newMeta) && !newMeta.StartsWith(ChatMessageCipher.Prefix, StringComparison.Ordinal))
                    newMeta = _cipher.ProtectFieldNullable(newMeta);

                const string upd = @"UPDATE [App_Messages] SET [Text] = @T, [MetaJson] = @M WHERE [Id] = @Id;";
                await using var u = new SqlCommand(upd, connection);
                u.Parameters.AddWithValue("@T", newText);
                u.Parameters.AddWithValue("@M", (object?)newMeta ?? DBNull.Value);
                u.Parameters.AddWithValue("@Id", row.Id);
                await u.ExecuteNonQueryAsync();
            }
        }

        const string markSql = @"
            IF NOT EXISTS (SELECT 1 FROM [App_AppMigrations] WHERE [Name] = N'ChatEncryptV1')
                INSERT INTO [App_AppMigrations] ([Name]) VALUES (N'ChatEncryptV1');";
        await using (var done = new SqlCommand(markSql, connection))
            await done.ExecuteNonQueryAsync();
    }

    private static async Task UpsertThreadReadAsync(SqlConnection connection, string login, int threadId, int lastReadMessageId)
    {
        const string sql = @"
            IF EXISTS (SELECT 1 FROM [App_ThreadReads] WHERE [Login] = @Login AND [ThreadId] = @ThreadId)
                UPDATE [App_ThreadReads]
                SET [LastReadMessageId] = CASE WHEN [LastReadMessageId] < @LastRead THEN @LastRead ELSE [LastReadMessageId] END,
                    [UpdatedAt] = GETUTCDATE()
                WHERE [Login] = @Login AND [ThreadId] = @ThreadId
            ELSE
                INSERT INTO [App_ThreadReads] ([Login], [ThreadId], [LastReadMessageId], [UpdatedAt])
                VALUES (@Login, @ThreadId, @LastRead, GETUTCDATE());";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        cmd.Parameters.AddWithValue("@ThreadId", threadId);
        cmd.Parameters.AddWithValue("@LastRead", lastReadMessageId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static bool TryParseChatMediaMeta(string? metaJson, out string? mediaUrl, out string? mediaKind)
    {
        mediaUrl = null;
        mediaKind = null;
        if (string.IsNullOrWhiteSpace(metaJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(metaJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("media", out var mediaArr) &&
                mediaArr.ValueKind == JsonValueKind.Array &&
                mediaArr.GetArrayLength() > 0)
            {
                var first = mediaArr[0];
                if (first.ValueKind == JsonValueKind.Object &&
                    first.TryGetProperty("url", out var u0))
                {
                    var u = u0.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(u))
                    {
                        mediaUrl = u;
                        if (first.TryGetProperty("kind", out var k0))
                            mediaKind = k0.GetString()?.Trim();
                        return true;
                    }
                }
            }
            if (!root.TryGetProperty("mediaUrl", out var uEl)) return false;
            var u = uEl.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(u)) return false;
            mediaUrl = u;
            if (root.TryGetProperty("mediaKind", out var kEl))
                mediaKind = kEl.GetString()?.Trim();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? BuildThreadLastPreview(string? text, string? metaJson)
    {
        var t = text?.Trim();
        if (!string.IsNullOrWhiteSpace(t)) return t;
        if (TryParseChatMediaMeta(metaJson, out _, out var kind))
        {
            return string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase) ? "Видео" : "Изображение";
        }
        return t;
    }

    private async Task<string> SaveChatMediaFileAsync(IFormFile media)
    {
        var uploadsRoot = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "uploads");
        Directory.CreateDirectory(uploadsRoot);

        var ext = Path.GetExtension(media.FileName);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 10) ext = ".bin";

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadsRoot, fileName);

        await using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
        {
            await media.CopyToAsync(fs);
        }

        var req = Request;
        var baseUrl = $"{req.Scheme}://{req.Host}{req.PathBase}".TrimEnd('/');
        return $"{baseUrl}/uploads/{fileName}";
    }

    public static async Task<int> EnsureMonitorBotThreadAsync(SqlConnection connection, string login)
    {
        const string getSql = @"SELECT TOP 1 [Id] FROM [App_Threads] WHERE [OwnerLogin] = @Login AND [BotId] = 'StekloMonitor';";
        await using (var get = new SqlCommand(getSql, connection))
        {
            get.Parameters.AddWithValue("@Login", login);
            var o = await get.ExecuteScalarAsync();
            if (o != null && o != DBNull.Value) return Convert.ToInt32(o);
        }

        const string insSql = @"
            INSERT INTO [App_Threads] ([OwnerLogin], [Type], [Title], [BotId], [CreatedAt])
            VALUES (@Login, 'bot', N'Мониторинг', 'StekloMonitor', GETUTCDATE());
            SELECT SCOPE_IDENTITY();";
        await using var ins = new SqlCommand(insSql, connection);
        ins.Parameters.AddWithValue("@Login", login);
        var newId = await ins.ExecuteScalarAsync();
        return Convert.ToInt32(newId);
    }

    public static async Task<int> EnsureSecurityBotThreadAsync(SqlConnection connection, string login)
    {
        const string getSql = @"SELECT TOP 1 [Id] FROM [App_Threads] WHERE [OwnerLogin] = @Login AND [BotId] = 'StekloSecurity';";
        await using (var get = new SqlCommand(getSql, connection))
        {
            get.Parameters.AddWithValue("@Login", login);
            var o = await get.ExecuteScalarAsync();
            if (o != null && o != DBNull.Value) return Convert.ToInt32(o);
        }

        const string insSql = @"
            INSERT INTO [App_Threads] ([OwnerLogin], [Type], [Title], [BotId], [CreatedAt])
            VALUES (@Login, 'bot', 'StekloSecurity', 'StekloSecurity', GETUTCDATE());
            SELECT SCOPE_IDENTITY();";
        await using var ins = new SqlCommand(insSql, connection);
        ins.Parameters.AddWithValue("@Login", login);
        var newId = await ins.ExecuteScalarAsync();
        return Convert.ToInt32(newId);
    }

    public static async Task InsertSecurityMessageAsync(
        SqlConnection connection,
        string login,
        string text,
        string? metaJson = null,
        ChatMessageCipher? cipher = null)
    {
        var threadId = await EnsureSecurityBotThreadAsync(connection, login);
        var encText = cipher?.ProtectField(text) ?? text;
        var encMeta = cipher?.ProtectFieldNullable(metaJson);
        const string sql = @"
            INSERT INTO [App_Messages] ([ThreadId], [SenderType], [SenderId], [Text], [MetaJson], [CreatedAt])
            VALUES (@ThreadId, 'bot', 'StekloSecurity', @Text, @MetaJson, GETUTCDATE());";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@ThreadId", threadId);
        cmd.Parameters.AddWithValue("@Text", encText);
        cmd.Parameters.AddWithValue("@MetaJson", (object?)encMeta ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<int> EnsureNewsChannelThreadAsync(SqlConnection connection, string login)
    {
        const string getSql = @"SELECT TOP 1 [Id] FROM [App_Threads] WHERE [OwnerLogin] = @Login AND [Type] = 'channel' AND [Title] = N'Новости';";
        await using (var get = new SqlCommand(getSql, connection))
        {
            get.Parameters.AddWithValue("@Login", login);
            var o = await get.ExecuteScalarAsync();
            if (o != null && o != DBNull.Value) return Convert.ToInt32(o);
        }

        const string insSql = @"
            INSERT INTO [App_Threads] ([OwnerLogin], [Type], [Title], [BotId], [CreatedAt])
            VALUES (@Login, 'channel', N'Новости', NULL, GETUTCDATE());
            SELECT SCOPE_IDENTITY();";
        await using var ins = new SqlCommand(insSql, connection);
        ins.Parameters.AddWithValue("@Login", login);
        var newId = await ins.ExecuteScalarAsync();
        return Convert.ToInt32(newId);
    }

    private async Task HandleTechBotCommandsAsync(
        SqlConnection connection,
        int threadId,
        string threadBotId,
        string text,
        string normalizedLogin)
    {
        var cmdText = text.Trim();
        if (threadBotId.Equals("StekloMonitor", StringComparison.OrdinalIgnoreCase) &&
            cmdText.Equals("/stats", StringComparison.OrdinalIgnoreCase))
        {
            var body = await IsTechAdminAsync(connection, normalizedLogin)
                ? await BuildTechMonitorReportAsync(connection)
                : "Команда /stats доступна только тех. администраторам.";
            await InsertThreadBotReplyAsync(connection, threadId, "StekloMonitor", body);
            return;
        }
    }

    private async Task InsertThreadBotReplyAsync(SqlConnection connection, int threadId, string botId, string text)
    {
        var encText = _cipher.ProtectField(text);
        const string sql = @"
            INSERT INTO [App_Messages] ([ThreadId], [SenderType], [SenderId], [Text], [MetaJson], [CreatedAt])
            VALUES (@ThreadId, 'bot', @BotId, @Text, NULL, GETUTCDATE());";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@ThreadId", threadId);
        cmd.Parameters.AddWithValue("@BotId", botId);
        cmd.Parameters.AddWithValue("@Text", encText);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> BuildTechMonitorReportAsync(SqlConnection connection)
    {
        var sb = new StringBuilder();
        sb.AppendLine("📊 Мониторинг (UTC)");
        sb.AppendLine();

        async Task AddLineAsync(string label, string sql)
        {
            try
            {
                await using var cmd = new SqlCommand(sql, connection);
                var o = await cmd.ExecuteScalarAsync();
                var n = o == null || o == DBNull.Value ? 0 : Convert.ToInt32(o);
                sb.AppendLine($"{label}: {n}");
            }
            catch
            {
                sb.AppendLine($"{label}: н/д");
            }
        }

        await AddLineAsync("Сообщений в чатах за последний час",
            "SELECT COUNT(1) FROM [App_Messages] WHERE [CreatedAt] >= DATEADD(HOUR, -1, GETUTCDATE());");
        await AddLineAsync("Сообщений в чатах за 24 ч",
            "SELECT COUNT(1) FROM [App_Messages] WHERE [CreatedAt] >= DATEADD(HOUR, -24, GETUTCDATE());");
        await AddLineAsync("Уникальных авторов в чатах за 24 ч",
            @"SELECT COUNT(DISTINCT [SenderId]) FROM [App_Messages]
              WHERE [CreatedAt] >= DATEADD(HOUR, -24, GETUTCDATE())
                AND [SenderType] = 'user'
                AND [SenderId] IS NOT NULL AND LTRIM(RTRIM([SenderId])) <> '';");
        await AddLineAsync("Всего диалогов (threads)",
            "SELECT COUNT(1) FROM [App_Threads];");

        try
        {
            await using var cmd = new SqlCommand(
                "SELECT COUNT(1) FROM [App_Posts] WHERE [CreatedAt] >= DATEADD(HOUR, -24, GETUTCDATE());", connection);
            var o = await cmd.ExecuteScalarAsync();
            var n = o == null || o == DBNull.Value ? 0 : Convert.ToInt32(o);
            sb.AppendLine($"Постов в ленте за 24 ч: {n}");
        }
        catch
        {
            sb.AppendLine("Постов в ленте за 24 ч: н/д");
        }

        await AddLineAsync("Ожидают подтверждения входа (pending)",
            "SELECT COUNT(1) FROM [App_SecurityLoginAttempts] WHERE [Status] = 'pending';");
        await AddLineAsync("Отклонённых входов за 24 ч",
            @"SELECT COUNT(1) FROM [App_SecurityLoginAttempts]
              WHERE [Status] = 'denied' AND [DeniedAt] IS NOT NULL AND [DeniedAt] >= DATEADD(HOUR, -24, GETUTCDATE());");

        sb.AppendLine();
        sb.AppendLine(ServerDiagnosticsBuffer.FormatRecentForStats(maxTotalChars: 3000));
        sb.AppendLine();
        sb.AppendLine("Полные логи — на хосте (файл/консоль). Буфер только для текущего процесса.");
        return sb.ToString().TrimEnd();
    }
}

public record ThreadItem(
    int Id,
    string Type,
    string Title,
    string? BotId,
    DateTime CreatedAtUtc,
    string? LastMessageText,
    DateTime? LastMessageAtUtc,
    bool LastMessageFromSelf,
    bool LastMessageIsRead,
    int UnreadCount,
    bool IsTechAdmin,
    bool IsOfficialBot,
    string? AvatarUrl
);

public record ThreadsResponse(bool Success, string Message, List<ThreadItem>? Threads);

public record MessageItem(
    int Id,
    string SenderType,
    string? SenderId,
    string? SenderName,
    string Text,
    DateTime CreatedAtUtc,
    string? MetaJson,
    bool SenderIsTechAdmin,
    bool IsRead
);

public record MessagesResponse(bool Success, string Message, List<MessageItem>? Messages);

public record SendMessageRequest(string Login, string Text, string? MetaJson = null);
public record SendMessageResponse(bool Success, string Message, MessageItem? Item);
public record ChatMediaUploadResponse(bool Success, string Message, string? Url, string? Mime, string? Kind);
public record DeleteMessageResponse(bool Success, string Message);
public record ClearThreadHistoryResponse(bool Success, string Message);
public record BotProfileItem(string BotId, string DisplayName, string? Description, string? AvatarUrl, bool IsOfficial);
public record BotProfileResponse(bool Success, string Message, BotProfileItem? Profile);
public record UpdateBotProfileRequest(string Login, string? DisplayName = null, string? Description = null, bool? IsOfficial = null);
public record UpdateBotProfileResponse(bool Success, string Message, BotProfileItem? Profile);
public record ColleagueSearchItem(
    string Login,
    string EmployeeId,
    string FullName,
    string Position,
    bool IsTechAdmin,
    string? AvatarUrl
);
public record ColleagueSearchResponse(bool Success, string Message, List<ColleagueSearchItem>? Colleagues);
public record OpenDirectThreadRequest(string Login, string ColleagueLogin);
public record OpenDirectThreadResponse(bool Success, string Message, ThreadItem? Thread);

