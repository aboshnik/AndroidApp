using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using EmployeeApi.Services;
using EmployeeApi.Services.Coins;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmployeeController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ChatMessageCipher _chatCipher;
    private readonly ICoinsService _coinsService;

    public EmployeeController(IConfiguration configuration, IWebHostEnvironment env, ChatMessageCipher chatCipher, ICoinsService coinsService)
    {
        _configuration = configuration;
        _env = env;
        _chatCipher = chatCipher;
        _coinsService = coinsService;
    }

    [HttpPost("verify")]
    public async Task<ActionResult<VerifyResponse>> Verify([FromBody] VerifyRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.LastName) || string.IsNullOrWhiteSpace(request.PhoneNormalized))
        {
            return BadRequest(new VerifyResponse(false, false, false, "Неверные данные запроса"));
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            return StatusCode(500, new VerifyResponse(false, false, false, "Не настроено подключение к базе данных"));
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var phoneLast10 = GetPhoneLast10(request.PhoneNormalized);

            const string sql = @"
                SELECT TOP 1
                    CAST(1 AS bit) AS ExistsFlag,
                    CAST(ISNULL([ЗарегВПриложении], 0) AS bit) AS RegisteredInApp
                FROM [Lexema_Кадры_ЛичнаяКарточка]
                WHERE
                    (
                        -- Совпадение по табельному номеру
                        [ТабельныйНомер] = @EmployeeId
                        OR
                        (
                            -- Совпадение по ФИО + телефону (последние 10 цифр)
                            [Фамилия] = @LastName
                            AND [Имя] = @FirstName
                            AND [Отчество] = @Patronymic
                            AND RIGHT(
                                REPLACE(
                                    REPLACE(
                                        REPLACE(
                                            REPLACE(
                                                REPLACE([Сотовый], ' ', ''),
                                            '-', ''),
                                        '+', ''),
                                    '(', ''),
                                ')', ''),
                                10
                            ) = @PhoneLast10
                        )
                    )
                    AND TRY_CONVERT(datetime2, [ДатаУвольнения]) IS NULL;";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@EmployeeId", request.EmployeeId ?? "");
            cmd.Parameters.AddWithValue("@LastName", request.LastName ?? "");
            cmd.Parameters.AddWithValue("@FirstName", request.FirstName ?? "");
            cmd.Parameters.AddWithValue("@Patronymic", request.Patronymic ?? "");
            cmd.Parameters.AddWithValue("@PhoneLast10", phoneLast10);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var exists = reader.GetBoolean(0);
                var registeredInApp = reader.GetBoolean(1);

                return Ok(new VerifyResponse(true, exists, registeredInApp,
                    exists
                        ? (registeredInApp
                            ? "Сотрудник уже зарегистрирован в приложении"
                            : "Сотрудник найден и ещё не зарегистрирован")
                        : "Сотрудник не найден"));
            }

            return Ok(new VerifyResponse(true, false, false, "Сотрудник не найден"));
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            
            return Ok(new VerifyResponse(true, false, false, "Сотрудник не найден"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new VerifyResponse(false, false, false, $"Ошибка БД: {ex.Message}"));
        }
    }

    [HttpPost("register")]
    public async Task<ActionResult<VerifyResponse>> Register([FromBody] VerifyRequest request)
    {
        if (request == null
            || string.IsNullOrWhiteSpace(request.LastName)
            || string.IsNullOrWhiteSpace(request.PhoneNormalized)
            || string.IsNullOrWhiteSpace(request.EmployeeId))
        {
            return BadRequest(new VerifyResponse(false, false, false, "Неверные данные запроса"));
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            return StatusCode(500, new VerifyResponse(false, false, false, "Не настроено подключение к базе данных"));
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var loginBase = ToLoginBase(request.LastName);
            var fioCount = await GetFioCountAsync(connection, request.LastName, request.FirstName, request.Patronymic);
            var useNumbering = fioCount > 1;
            var startSuffix = useNumbering
                ? await GetFioRowNumberAsync(connection, request.LastName, request.FirstName, request.Patronymic, request.EmployeeId)
                : 1;
            var login = await GenerateUniqueLoginAsync(connection, loginBase, useNumbering, startSuffix);
            var passwordHash = HashPassword(login);

            const string sql = @"
                UPDATE TOP (1) [Lexema_Кадры_ЛичнаяКарточка]
                SET [ЗарегВПриложении] = 1,
                    [Логин] = @Login,
                    [Пароль] = @PasswordHash
                WHERE
                    (
                        [ТабельныйНомер] = @EmployeeId
                        OR
                        (
                            [Фамилия] = @LastName
                            AND [Имя] = @FirstName
                            AND [Отчество] = @Patronymic
                            AND RIGHT(
                                REPLACE(
                                    REPLACE(
                                        REPLACE(
                                            REPLACE(
                                                REPLACE([Сотовый], ' ', ''),
                                            '-', ''),
                                        '+', ''),
                                    '(', ''),
                                ')', ''),
                                10
                            ) = @PhoneLast10
                        )
                    )
                    AND TRY_CONVERT(datetime2, [ДатаУвольнения]) IS NULL;
                SELECT @@ROWCOUNT;";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@EmployeeId", request.EmployeeId ?? "");
            
            cmd.Parameters.AddWithValue("@LastName", "__nomatch__");
            cmd.Parameters.AddWithValue("@FirstName", "__nomatch__");
            cmd.Parameters.AddWithValue("@Patronymic", "__nomatch__");
            cmd.Parameters.AddWithValue("@PhoneLast10", "0000000000");
            cmd.Parameters.AddWithValue("@Login", login);
            cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);

            var result = await cmd.ExecuteScalarAsync();
            var affected = Convert.ToInt32(result ?? 0);

            if (affected > 0)
            {
                // Legacy register flow: also notify credentials in StekloSecurity chat.
                // Here password equals login by current business rule.
                try
                {
                    await ChatController.EnsureChatTablesAsync(connection);
                    var securityText = $"""
Добро пожаловать в наш корпоративный портал.

Ваш логин:
{login}

Ваш пароль:
{login}

Как скопировать данные:
1) Нажмите на это сообщение.
2) Выберите "Копировать текст" или "Выделить текст".
3) Вставьте данные в форму входа.

Не передавайте эти данные другим людям.
""".Trim();
                    await ChatController.InsertSecurityMessageAsync(connection, login, securityText, metaJson: null, _chatCipher);
                    if (!string.Equals(request.EmployeeId, login, StringComparison.OrdinalIgnoreCase))
                    {
                        await ChatController.InsertSecurityMessageAsync(connection, request.EmployeeId, securityText, metaJson: null, _chatCipher);
                    }
                }
                catch
                {
                    // Do not fail registration if chat notification failed.
                }

                return Ok(new VerifyResponse(
                    true,
                    true,
                    true,
                    "Сотрудник успешно отмечен как зарегистрированный в приложении",
                    login,
                    login));
            }

            return Ok(new VerifyResponse(true, false, false, "Сотрудник не найден или уже отмечен как зарегистрированный"));
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            
            return Ok(new VerifyResponse(true, false, false, "Сотрудник не найден"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new VerifyResponse(false, false, false, $"Ошибка БД: {ex.Message}"));
        }
    }

    [HttpGet("profile")]
    public async Task<ActionResult<ProfileResponse>> Profile([FromQuery] string? employeeId, [FromQuery] string? login)
    {
        if (string.IsNullOrWhiteSpace(employeeId) && string.IsNullOrWhiteSpace(login))
        {
            return BadRequest(new ProfileResponse(false, "Неверные данные запроса", null));
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            return StatusCode(500, new ProfileResponse(false, "Не настроено подключение к базе данных", null));
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await EnsureAppUserProfileTableAsync(connection);
            var isDismissed = await IsDismissedEmployeeAsync(connection, employeeId, login);
            if (isDismissed)
                return Ok(new ProfileResponse(false, "Профиль заблокирован: сотрудник уволен", null));

            const string sql = @"
                SELECT TOP 1
                    COALESCE(TRY_CONVERT(nvarchar(200), C.[Фамилия]), '')        AS LastName,
                    COALESCE(TRY_CONVERT(nvarchar(200), C.[Имя]), '')           AS FirstName,
                    COALESCE(TRY_CONVERT(nvarchar(200), C.[Сотовый]), '')       AS Phone,
                    COALESCE(TRY_CONVERT(nvarchar(50),  C.[ТабельныйНомер]), '') AS EmployeeId,
                    COALESCE(TRY_CONVERT(nvarchar(200), C.[Должность]), '')     AS Position,
                    COALESCE(TRY_CONVERT(nvarchar(200), C.[Подразделение]), '') AS Subdivision,
                    COALESCE(TRY_CONVERT(nvarchar(100), C.[Логин]), '')         AS CardLogin,
                    ISNULL(P.[Experience], 0)                                     AS Experience,
                    P.[AvatarFileName]                                            AS AvatarFileName
                FROM [Lexema_Кадры_ЛичнаяКарточка] C
                LEFT JOIN [App_UserProfile] P ON P.[Login] = C.[Логин]
                WHERE
                    (
                        (@EmployeeId <> '' AND TRY_CONVERT(nvarchar(50), C.[ТабельныйНомер]) = @EmployeeId)
                        OR
                        (@Login <> '' AND C.[Логин] = @Login)
                    )
                    AND TRY_CONVERT(datetime2, C.[ДатаУвольнения]) IS NULL;";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@EmployeeId", employeeId ?? string.Empty);
            cmd.Parameters.AddWithValue("@Login", login ?? string.Empty);

            string lastName;
            string firstName;
            string phone;
            string employeeIdValue;
            string position;
            string subdivision;
            string cardLogin;
            int experience;
            string? avatarFileName;

            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                {
                    return Ok(new ProfileResponse(true, "Сотрудник не найден", null));
                }

                lastName = reader.GetString(0);
                firstName = reader.GetString(1);
                phone = reader.GetString(2);
                employeeIdValue = reader.GetString(3);
                position = reader.GetString(4);
                subdivision = reader.GetString(5);
                cardLogin = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim();
                experience = reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetValue(7));
                avatarFileName = reader.IsDBNull(8) ? null : reader.GetString(8);
            }

            var employeeIdFromCard = employeeIdValue.Trim();
            if (string.IsNullOrWhiteSpace(avatarFileName))
            {
                avatarFileName = await TryResolveAvatarFileNameAsync(
                    connection,
                    login,
                    cardLogin,
                    employeeIdFromCard
                );
            }
            var level = Math.Max(1, 1 + experience / 100);
            var xpToNext = ComputeXpToNextWithinLevel(experience);
            var coinInfo = await _coinsService.GetBalanceAsync(cardLogin);

            var avatarUrl = BuildAvatarPublicUrl(avatarFileName);

            var profile = new EmployeeProfile(
                LastName: lastName,
                FirstName: firstName,
                Phone: phone,
                EmployeeId: employeeIdValue,
                Position: position,
                Subdivision: subdivision,
                AvatarUrl: avatarUrl,
                Level: level,
                Experience: experience,
                XpToNext: xpToNext,
                CoinBalance: coinInfo.Success ? coinInfo.Balance : 0,
                NextPayoutDays: coinInfo.Success ? coinInfo.NextPayoutDays : 7
            );

            return Ok(new ProfileResponse(true, "OK", profile));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ProfileResponse(false, $"Ошибка БД: {ex.Message}", null));
        }
    }

    [HttpPost("avatar")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<AvatarUploadResponse>> UploadAvatar([FromQuery] string? login, IFormFile? file)
    {
        if (string.IsNullOrWhiteSpace(login) || file == null || file.Length == 0)
        {
            return BadRequest(new AvatarUploadResponse(false, "Неверные данные запроса", null));
        }

        login = login.Trim();
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext))
        {
            ext = ".jpg";
        }

        ext = ext.ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif"))
        {
            return BadRequest(new AvatarUploadResponse(false, "Допустимы изображения: jpg, png, webp, gif", null));
        }

        if (file.Length > 5 * 1024 * 1024)
        {
            return BadRequest(new AvatarUploadResponse(false, "Файл больше 5 МБ", null));
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            return StatusCode(500, new AvatarUploadResponse(false, "Не настроено подключение к базе данных", null));
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await EnsureAppUserProfileTableAsync(connection);

            const string existsSql = @"
                SELECT TOP 1 1
                FROM [Lexema_Кадры_ЛичнаяКарточка]
                WHERE [Логин] = @Login AND ISNULL([ЗарегВПриложении], 0) = 1;";
            await using (var existsCmd = new SqlCommand(existsSql, connection))
            {
                existsCmd.Parameters.AddWithValue("@Login", login);
                var ok = await existsCmd.ExecuteScalarAsync();
                if (ok == null || ok == DBNull.Value)
                {
                    return Ok(new AvatarUploadResponse(false, "Пользователь не найден", null));
                }
            }

            var safeLogin = new string(login.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrEmpty(safeLogin))
            {
                safeLogin = "user";
            }

            var fileName = $"{safeLogin}_{Guid.NewGuid():N}{ext}";
            var dir = Path.Combine(_env.WebRootPath ?? "", "uploads", "avatars");
            Directory.CreateDirectory(dir);

            var oldName = await GetAvatarFileNameAsync(connection, login);
            var physicalPath = Path.Combine(dir, fileName);
            await using (var fs = new FileStream(physicalPath, FileMode.Create, FileAccess.Write))
            {
                await file.CopyToAsync(fs);
            }

            if (!string.IsNullOrWhiteSpace(oldName))
            {
                var oldPath = Path.Combine(dir, oldName);
                if (!string.Equals(oldPath, physicalPath, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (System.IO.File.Exists(oldPath))
                        {
                            System.IO.File.Delete(oldPath);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            await UpsertAvatarFileNameAsync(connection, login, fileName);

            var url = BuildAvatarPublicUrl(fileName);
            return Ok(new AvatarUploadResponse(true, "OK", url));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new AvatarUploadResponse(false, $"Ошибка: {ex.Message}", null));
        }
    }

    private string? BuildAvatarPublicUrl(string? avatarFileName)
    {
        if (string.IsNullOrWhiteSpace(avatarFileName))
        {
            return null;
        }

        var raw = avatarFileName.Trim();
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        if (raw.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            var reqDirect = Request;
            var directBase = $"{reqDirect.Scheme}://{reqDirect.Host}{reqDirect.PathBase}";
            return $"{directBase}{raw}";
        }

        var req = Request;
        var baseUrl = $"{req.Scheme}://{req.Host}{req.PathBase}";
        return $"{baseUrl}/uploads/avatars/{Uri.EscapeDataString(raw)}";
    }

    private static int ComputeXpToNextWithinLevel(int experience)
    {
        var mod = experience % 100;
        return mod == 0 && experience > 0 ? 100 : 100 - mod;
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

    private static async Task<string?> GetAvatarFileNameAsync(SqlConnection connection, string login)
    {
        const string sql = @"
            SELECT TOP 1 [AvatarFileName]
            FROM [App_UserProfile]
            WHERE LOWER(LTRIM(RTRIM([Login]))) = LOWER(@Login);";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        var o = await cmd.ExecuteScalarAsync();
        if (o == null || o == DBNull.Value)
        {
            return null;
        }

        var s = o.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static async Task<string?> TryResolveAvatarFileNameAsync(
        SqlConnection connection,
        string? requestedLogin,
        string? cardLogin,
        string? employeeId)
    {
        var rawCandidates = new[]
        {
            requestedLogin?.Trim(),
            cardLogin?.Trim(),
            employeeId?.Trim()
        };

        var candidates = new List<string>();
        foreach (var raw in rawCandidates)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var value = raw.Trim();
            candidates.Add(value);
            if (value.Contains('\\')) candidates.Add(value[(value.LastIndexOf('\\') + 1)..].Trim());
            if (value.Contains('@')) candidates.Add(value[..value.IndexOf('@')].Trim());
            candidates.Add(value.ToLowerInvariant());
            candidates.Add(value.ToUpperInvariant());
        }

        candidates = candidates
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var key in candidates)
        {
            var file = await GetAvatarFileNameAsync(connection, key!);
            if (!string.IsNullOrWhiteSpace(file)) return file;
        }

        return null;
    }

    private static async Task UpsertAvatarFileNameAsync(SqlConnection connection, string login, string fileName)
    {
        const string sql = @"
            IF EXISTS (SELECT 1 FROM [App_UserProfile] WHERE [Login] = @Login)
                UPDATE [App_UserProfile]
                SET [AvatarFileName] = @FileName, [UpdatedAt] = GETUTCDATE()
                WHERE [Login] = @Login;
            ELSE
                INSERT INTO [App_UserProfile] ([Login], [AvatarFileName], [Experience], [UpdatedAt])
                VALUES (@Login, @FileName, 0, GETUTCDATE());";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        cmd.Parameters.AddWithValue("@FileName", fileName);
        await cmd.ExecuteNonQueryAsync();
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new LoginResponse(false, "Неверные данные запроса", null));
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            return StatusCode(500, new LoginResponse(false, "Не настроено подключение к базе данных", null));
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsureUserPermissionsTableExistsAsync(connection);
            await EnsureSecurityTablesAsync(connection);
            await EnsureNotificationsTablesAsync(connection);

            var passwordHash = HashPassword(request.Password);

            const string sql = @"
                SELECT TOP 1
                    COALESCE(TRY_CONVERT(nvarchar(200), [Фамилия]), '')        AS LastName,
                    COALESCE(TRY_CONVERT(nvarchar(200), [Имя]), '')           AS FirstName,
                    COALESCE(TRY_CONVERT(nvarchar(200), [Сотовый]), '')       AS Phone,
                    COALESCE(TRY_CONVERT(nvarchar(50),  [ТабельныйНомер]), '') AS EmployeeId,
                    ISNULL(P.[CanCreatePosts], 0)                              AS CanCreatePosts,
                    ISNULL(P.[CanTechAdmin], 0)                                AS IsTechAdmin,
                    ISNULL(P.[CanUseDevConsole], 0)                            AS CanUseDevConsole
                FROM [Lexema_Кадры_ЛичнаяКарточка]
                LEFT JOIN [App_UserPermissions] P ON P.[Login] = @Login
                WHERE
                    [Логин] = @Login
                    AND ISNULL([Пароль], '') = @PasswordHash
                    AND ISNULL([ЗарегВПриложении], 0) = 1
                    AND TRY_CONVERT(datetime2, [ДатаУвольнения]) IS NULL;";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Login", request.Login);
            cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);

            string lastName = "";
            string firstName = "";
            string phone = "";
            string employeeId = "";
            bool canCreatePosts = false;
            bool isTechAdmin = false;
            bool canUseDevConsole = false;
            var userFound = false;

            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync()) { }
                else
                {
                    userFound = true;

                    lastName = reader.GetString(0);
                    firstName = reader.GetString(1);
                    phone = reader.GetString(2);
                    employeeId = reader.GetString(3);
                    isTechAdmin = Convert.ToInt32(reader.GetValue(5)) == 1;
                    canCreatePosts = Convert.ToInt32(reader.GetValue(4)) == 1 || isTechAdmin;
                    canUseDevConsole = Convert.ToInt32(reader.GetValue(6)) == 1;
                }
            }  

            if (!userFound)
            {
                var dismissed = await IsDismissedEmployeeAsync(connection, null, request.Login);
                if (dismissed)
                    return Ok(new LoginResponse(false, "Профиль заблокирован: сотрудник уволен", null));
                return Ok(new LoginResponse(false, "Неверный логин или пароль", null));
            }

            var result = new LoginResult(
                LastName: lastName,
                FirstName: firstName,
                Phone: phone,
                EmployeeId: employeeId,
                CanCreatePosts: canCreatePosts,
                IsTechAdmin: isTechAdmin,
                CanUseDevConsole: canUseDevConsole
            );

            var deviceId = (request.DeviceId ?? string.Empty).Trim();
            var deviceName = (request.DeviceName ?? string.Empty).Trim();
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var userAgent = Request.Headers.UserAgent.ToString();
            var login = request.Login.Trim();
            var ipNorm = NormalizeIp(ip);

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return Ok(new LoginResponse(false, "Недостаточно данных устройства. Повторите вход.", null));
            }

           
            if (!string.IsNullOrWhiteSpace(ipNorm) && await IsIpBlockedAsync(connection, login, ipNorm))
            {
                return Ok(new LoginResponse(false, "Вход запрещен: IP заблокирован. Попробуйте позже.", null));
            }

            
            var hasAnyDevices = await IsAnyDeviceKnownAsync(connection, login);
            var canSkipDeviceCode = request.ReloginBypass == true;

           
            var isKnownDevice = await IsDeviceKnownAsync(connection, login, deviceId);
            if (isKnownDevice)
            {
                await UpdateDeviceSeenAsync(connection, login, deviceId, deviceName, ipNorm, userAgent);
            }
            else
            {
                
                if (!hasAnyDevices || canSkipDeviceCode)
                {
                    await TrustFirstDeviceAsync(connection, login, deviceId, deviceName, ipNorm, userAgent);
                }
                else
                {
                    var (attemptId, plainCode) = await CreatePendingDeviceLoginAttemptAsync(
                        connection, login, deviceId, deviceName, ipNorm, userAgent);

                    await ChatController.EnsureChatTablesAsync(connection);
                    var securityText = $"""
Код для входа с другого устройства: {plainCode}

Действителен 10 минут. Запрос с: {deviceName}

""".Trim();
                    await ChatController.InsertSecurityMessageAsync(connection, login, securityText, metaJson: null, _chatCipher);
                    // Mirror security code into employeeId owner chat as well.
                    // Some active sessions still read chats under employeeId key.
                    if (!string.IsNullOrWhiteSpace(employeeId) &&
                        !string.Equals(employeeId, login, StringComparison.OrdinalIgnoreCase))
                    {
                        await ChatController.InsertSecurityMessageAsync(connection, employeeId, securityText, metaJson: null, _chatCipher);
                    }

                    await InsertNotificationAsync(
                        connection,
                        recipientLogin: login,
                        type: "security",
                        title: "StekloSecurity: код для входа",
                        body: $"Откройте чат со StekloSecurity — там код для входа с устройства «{deviceName}».",
                        action: "security_device_code",
                        actionData: attemptId.ToString());

                    var otherDeviceCount = await CountPushTokensForLoginExceptDeviceAsync(connection, login, deviceId);
                    if (FcmPush.IsConfigured() && otherDeviceCount > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await FcmPush.SendToLoginExceptDeviceAsync(
                                    connectionString,
                                    login,
                                    deviceId,
                                    "StekloSecurity: код для входа",
                                    "Откройте чат со StekloSecurity в приложении — там 6-значный код.",
                                    new Dictionary<string, string>
                                    {
                                        ["type"] = "security",
                                        ["action"] = "security_device_code",
                                        ["actionData"] = attemptId.ToString()
                                    });
                            }
                            catch
                            {
                            }
                        });
                    }

                    return Ok(new LoginResponse(
                        false,
                        "Введите код из чата со StekloSecurity на другом устройстве.",
                        null,
                        RequiresDeviceCode: true,
                        PendingAttemptId: attemptId));
                }
            }

            return Ok(new LoginResponse(true, "OK", result));
        }
        catch (Exception ex)
        {
            Console.WriteLine("LOGIN ERROR:");
            Console.WriteLine(ex.ToString());
            return StatusCode(500, new LoginResponse(false, $"Ошибка БД: {ex.Message}", null));
        }
    }

    [HttpPost("confirm-device-login")]
    public async Task<ActionResult<LoginResponse>> ConfirmDeviceLogin([FromBody] ConfirmDeviceLoginRequest request)
    {
        if (request == null ||
            string.IsNullOrWhiteSpace(request.Login) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.DeviceId) ||
            request.AttemptId <= 0)
            return BadRequest(new LoginResponse(false, "Неверные данные запроса", null));

        var codeRaw = (request.Code ?? "").Trim();
        if (codeRaw.Length != 6 || !codeRaw.All(char.IsDigit))
            return Ok(new LoginResponse(false, "Введите 6-значный код из уведомления", null));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return StatusCode(500, new LoginResponse(false, "Не настроено подключение к базе данных", null));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsureUserPermissionsTableExistsAsync(connection);
            await EnsureSecurityTablesAsync(connection);

            var passwordHash = HashPassword(request.Password);

            const string userSql = @"
                SELECT TOP 1
                    COALESCE(TRY_CONVERT(nvarchar(200), [Фамилия]), '')        AS LastName,
                    COALESCE(TRY_CONVERT(nvarchar(200), [Имя]), '')           AS FirstName,
                    COALESCE(TRY_CONVERT(nvarchar(200), [Сотовый]), '')       AS Phone,
                    COALESCE(TRY_CONVERT(nvarchar(50),  [ТабельныйНомер]), '') AS EmployeeId,
                    ISNULL(P.[CanCreatePosts], 0)                              AS CanCreatePosts,
                    ISNULL(P.[CanTechAdmin], 0)                                AS IsTechAdmin,
                    ISNULL(P.[CanUseDevConsole], 0)                            AS CanUseDevConsole
                FROM [Lexema_Кадры_ЛичнаяКарточка]
                LEFT JOIN [App_UserPermissions] P ON P.[Login] = @Login
                WHERE
                    [Логин] = @Login
                    AND ISNULL([Пароль], '') = @PasswordHash
                    AND ISNULL([ЗарегВПриложении], 0) = 1
                    AND TRY_CONVERT(datetime2, [ДатаУвольнения]) IS NULL;";

            string lastName = "";
            string firstName = "";
            string phone = "";
            string employeeId = "";
            bool canCreatePosts = false;
            bool isTechAdmin = false;
            bool canUseDevConsole = false;
            var userFound = false;

            await using (var cmd = new SqlCommand(userSql, connection))
            {
                cmd.Parameters.AddWithValue("@Login", request.Login.Trim());
                cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) { }
                else
                {
                    userFound = true;
                    lastName = reader.GetString(0);
                    firstName = reader.GetString(1);
                    phone = reader.GetString(2);
                    employeeId = reader.GetString(3);
                    isTechAdmin = Convert.ToInt32(reader.GetValue(5)) == 1;
                    canCreatePosts = Convert.ToInt32(reader.GetValue(4)) == 1 || isTechAdmin;
                    canUseDevConsole = Convert.ToInt32(reader.GetValue(6)) == 1;
                }
            }

            if (!userFound)
            {
                var dismissed = await IsDismissedEmployeeAsync(connection, null, request.Login.Trim());
                if (dismissed)
                    return Ok(new LoginResponse(false, "Профиль заблокирован: сотрудник уволен", null));
                return Ok(new LoginResponse(false, "Неверный логин или пароль", null));
            }

            const string attSql = @"
                SELECT TOP 1
                    [Status],
                    [CodeHash],
                    [CodeExpiresAt],
                    [CodeFailedAttempts],
                    [RecipientLogin],
                    [DeviceId],
                    [DeviceName],
                    [Ip],
                    [UserAgent]
                FROM [App_SecurityLoginAttempts]
                WHERE [Id] = @Id;";
            string? status = null;
            string? codeHashDb = null;
            DateTime? codeExpiresAt = null;
            int failed = 0;
            var recipientLogin = "";
            var attDeviceId = "";
            string? attDeviceName = null;
            string? attIp = null;
            string? attUa = null;

            await using (var acmd = new SqlCommand(attSql, connection))
            {
                acmd.Parameters.AddWithValue("@Id", request.AttemptId);
                await using var ar = await acmd.ExecuteReaderAsync();
                if (!await ar.ReadAsync())
                    return Ok(new LoginResponse(false, "Запрос не найден", null));

                status = ar.IsDBNull(0) ? null : ar.GetString(0);
                codeHashDb = ar.IsDBNull(1) ? null : ar.GetString(1);
                codeExpiresAt = ar.IsDBNull(2) ? null : ar.GetDateTime(2);
                failed = ar.IsDBNull(3) ? 0 : Convert.ToInt32(ar.GetValue(3));
                recipientLogin = ar.GetString(4);
                attDeviceId = ar.GetString(5);
                attDeviceName = ar.IsDBNull(6) ? null : ar.GetString(6);
                attIp = ar.IsDBNull(7) ? null : ar.GetString(7);
                attUa = ar.IsDBNull(8) ? null : ar.GetString(8);
            }

            var loginNorm = request.Login.Trim();
            if (!string.Equals(recipientLogin, loginNorm, StringComparison.OrdinalIgnoreCase))
                return Ok(new LoginResponse(false, "Запрос не найден", null));

            if (!string.Equals(attDeviceId, request.DeviceId.Trim(), StringComparison.Ordinal))
                return Ok(new LoginResponse(false, "Запрос не найден", null));

            if (!string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
                return Ok(new LoginResponse(false, "Код уже использован или отменён. Войдите снова.", null));

            if (codeExpiresAt.HasValue && codeExpiresAt.Value < DateTime.UtcNow)
                return Ok(new LoginResponse(false, "Срок действия кода истёк. Войдите снова.", null));

            if (failed >= 5)
                return Ok(new LoginResponse(false, "Слишком много неверных попыток. Войдите снова.", null));

            if (string.IsNullOrWhiteSpace(codeHashDb))
                return Ok(new LoginResponse(false, "Код недоступен для этого запроса", null));

            var codeTry = HashPassword(codeRaw);
            if (!string.Equals(codeTry, codeHashDb, StringComparison.Ordinal))
            {
                const string bumpSql = @"
                    UPDATE [App_SecurityLoginAttempts]
                    SET [CodeFailedAttempts] = [CodeFailedAttempts] + 1,
                        [Status] = CASE WHEN [CodeFailedAttempts] + 1 >= 5 THEN 'denied' ELSE [Status] END,
                        [DeniedAt] = CASE WHEN [CodeFailedAttempts] + 1 >= 5 THEN GETUTCDATE() ELSE [DeniedAt] END
                    WHERE [Id] = @Id;";
                await using (var bump = new SqlCommand(bumpSql, connection))
                {
                    bump.Parameters.AddWithValue("@Id", request.AttemptId);
                    await bump.ExecuteNonQueryAsync();
                }
                return Ok(new LoginResponse(false, "Неверный код", null));
            }

            const string approveSql = @"
                UPDATE [App_SecurityLoginAttempts]
                SET [Status] = 'approved',
                    [ApprovedAt] = GETUTCDATE()
                WHERE [Id] = @Id;";
            await using (var appr = new SqlCommand(approveSql, connection))
            {
                appr.Parameters.AddWithValue("@Id", request.AttemptId);
                await appr.ExecuteNonQueryAsync();
            }

            var ipNorm = NormalizeIp(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
            var ua = Request.Headers.UserAgent.ToString();
            var deviceName = (request.DeviceName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(deviceName)) deviceName = attDeviceName ?? "";

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
                upsert.Parameters.AddWithValue("@Login", loginNorm);
                upsert.Parameters.AddWithValue("@DeviceId", request.DeviceId.Trim());
                upsert.Parameters.AddWithValue("@DeviceName", (object?)deviceName ?? DBNull.Value);
                upsert.Parameters.AddWithValue("@Ip", (object?)(string.IsNullOrWhiteSpace(ipNorm) ? attIp : ipNorm) ?? DBNull.Value);
                upsert.Parameters.AddWithValue("@UA", (object?)(string.IsNullOrWhiteSpace(ua) ? attUa : ua) ?? DBNull.Value);
                await upsert.ExecuteNonQueryAsync();
            }

            if (!string.IsNullOrWhiteSpace(attIp))
            {
                const string deleteBlockSql = @"
                    DELETE FROM [App_BlockedIps]
                    WHERE [Login] = @Login AND [Ip] = @Ip;";
                await using var del = new SqlCommand(deleteBlockSql, connection);
                del.Parameters.AddWithValue("@Login", loginNorm);
                del.Parameters.AddWithValue("@Ip", attIp);
                await del.ExecuteNonQueryAsync();
            }

            var loginResult = new LoginResult(
                LastName: lastName,
                FirstName: firstName,
                Phone: phone,
                EmployeeId: employeeId,
                CanCreatePosts: canCreatePosts,
                IsTechAdmin: isTechAdmin,
                CanUseDevConsole: canUseDevConsole
            );

            return Ok(new LoginResponse(true, "OK", loginResult));
        }
        catch (Exception ex)
        {
            Console.WriteLine("CONFIRM DEVICE LOGIN ERROR:");
            Console.WriteLine(ex.ToString());
            return StatusCode(500, new LoginResponse(false, $"Ошибка БД: {ex.Message}", null));
        }
    }

    [HttpGet("work-schedule")]
    public async Task<ActionResult<WorkScheduleResponse>> GetWorkSchedule([FromQuery] string? employeeId, [FromQuery] string? login)
    {
        if (string.IsNullOrWhiteSpace(employeeId) && string.IsNullOrWhiteSpace(login))
        {
            return BadRequest(new WorkScheduleResponse(false, "Неверные данные запроса", null));
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            return StatusCode(500, new WorkScheduleResponse(false, "Не настроено подключение к базе данных", null));
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            var schedule = await TryReadWorkScheduleAsync(connection, employeeId, login);
            if (schedule == null)
            {
                return Ok(new WorkScheduleResponse(true, "Данные графика не найдены", null));
            }

            return Ok(new WorkScheduleResponse(true, "OK", schedule));
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            return Ok(new WorkScheduleResponse(true, "Таблица графиков не создана", null));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new WorkScheduleResponse(false, $"Ошибка БД: {ex.Message}", null));
        }
    }

    private static async Task<WorkSchedule?> TryReadWorkScheduleAsync(SqlConnection connection, string? employeeId, string? login)
    {
        var variants = new[]
        {
           
            @"
                SELECT TOP 1
                    COALESCE(TRY_CONVERT(nvarchar(20),  G.[График]), '')                                 AS WorkPattern,
                    COALESCE(CONVERT(nvarchar(5), TRY_CONVERT(time, G.[НачалоСмены]), 108), '')          AS ShiftStart,
                    COALESCE(CONVERT(nvarchar(5), TRY_CONVERT(time, G.[КонецСмены]), 108), '')           AS ShiftEnd,
                    COALESCE(CONVERT(nvarchar(10), TRY_CONVERT(date, G.[НачалоОтпуска]), 23), '')        AS VacationStart,
                    COALESCE(CONVERT(nvarchar(10), TRY_CONVERT(date, G.[КонецОтпуска]), 23), '')         AS VacationEnd
                FROM [EmployeeРабочийГрафик] G
                WHERE
                    (@EmployeeId <> '' AND TRY_CONVERT(nvarchar(50), G.[EmployeeId]) = @EmployeeId)
                    OR
                    (@Login <> '' AND EXISTS (
                        SELECT 1
                        FROM [Lexema_Кадры_ЛичнаяКарточка] C
                        WHERE C.[Логин] = @Login
                          AND TRY_CONVERT(nvarchar(50), C.[ТабельныйНомер]) = TRY_CONVERT(nvarchar(50), G.[EmployeeId])
                    ));",
            
            @"
                SELECT TOP 1
                    COALESCE(TRY_CONVERT(nvarchar(20),  G.[График]), '')                                 AS WorkPattern,
                    COALESCE(CONVERT(nvarchar(5), TRY_CONVERT(time, G.[НачалоСмены]), 108), '')          AS ShiftStart,
                    COALESCE(CONVERT(nvarchar(5), TRY_CONVERT(time, G.[КонецСмены]), 108), '')           AS ShiftEnd,
                    COALESCE(CONVERT(nvarchar(10), TRY_CONVERT(date, G.[НачалоОтпуска]), 23), '')        AS VacationStart,
                    COALESCE(CONVERT(nvarchar(10), TRY_CONVERT(date, G.[КонецОтпуска]), 23), '')         AS VacationEnd
                FROM [EmployeeРабочийГрафик] G
                WHERE
                    (@EmployeeId <> '' AND TRY_CONVERT(nvarchar(50), G.[ТабельныйНомер]) = @EmployeeId)
                    OR
                    (@Login <> '' AND EXISTS (
                        SELECT 1
                        FROM [Lexema_Кадры_ЛичнаяКарточка] C
                        WHERE C.[Логин] = @Login
                          AND TRY_CONVERT(nvarchar(50), C.[ТабельныйНомер]) = TRY_CONVERT(nvarchar(50), G.[ТабельныйНомер])
                    ));",
            
            @"
                SELECT TOP 1
                    COALESCE(TRY_CONVERT(nvarchar(20),  G.[WorkPattern]), '')                             AS WorkPattern,
                    COALESCE(CONVERT(nvarchar(5), TRY_CONVERT(time, G.[ShiftStart]), 108), '')            AS ShiftStart,
                    COALESCE(CONVERT(nvarchar(5), TRY_CONVERT(time, G.[ShiftEnd]), 108), '')              AS ShiftEnd,
                    COALESCE(CONVERT(nvarchar(10), TRY_CONVERT(date, G.[VacationStart]), 23), '')         AS VacationStart,
                    COALESCE(CONVERT(nvarchar(10), TRY_CONVERT(date, G.[VacationEnd]), 23), '')           AS VacationEnd
                FROM [EmployeeРабочийГрафик] G
                WHERE
                    (@EmployeeId <> '' AND TRY_CONVERT(nvarchar(50), G.[EmployeeId]) = @EmployeeId)
                    OR
                    (@Login <> '' AND EXISTS (
                        SELECT 1
                        FROM [Lexema_Кадры_ЛичнаяКарточка] C
                        WHERE C.[Логин] = @Login
                          AND TRY_CONVERT(nvarchar(50), C.[ТабельныйНомер]) = TRY_CONVERT(nvarchar(50), G.[EmployeeId])
                    ));"
        };

        foreach (var sql in variants)
        {
            try
            {
                await using var cmd = new SqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@EmployeeId", employeeId ?? string.Empty);
                cmd.Parameters.AddWithValue("@Login", login ?? string.Empty);

                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    continue;
                }

                return new WorkSchedule(
                    WorkPattern: reader.GetString(0),
                    ShiftStart: reader.GetString(1),
                    ShiftEnd: reader.GetString(2),
                    VacationStart: reader.GetString(3),
                    VacationEnd: reader.GetString(4)
                );
            }
            catch (SqlException ex) when (ex.Number == 207 || ex.Number == 208)
            {
               
                continue;
            }
        }

        return null;
    }

    private static async Task EnsureUserPermissionsTableExistsAsync(SqlConnection connection)
    {
        const string createSql = @"
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
        await using var cmd = new SqlCommand(createSql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureSecurityTablesAsync(SqlConnection connection)
    {
        const string sql = @"
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
            );

            IF COL_LENGTH('App_SecurityLoginAttempts', 'CodeHash') IS NULL
                ALTER TABLE [App_SecurityLoginAttempts] ADD [CodeHash] NVARCHAR(100) NULL;
            IF COL_LENGTH('App_SecurityLoginAttempts', 'CodeExpiresAt') IS NULL
                ALTER TABLE [App_SecurityLoginAttempts] ADD [CodeExpiresAt] DATETIME2 NULL;
            IF COL_LENGTH('App_SecurityLoginAttempts', 'CodeFailedAttempts') IS NULL
                ALTER TABLE [App_SecurityLoginAttempts] ADD [CodeFailedAttempts] INT NOT NULL CONSTRAINT [DF_App_SecurityLoginAttempts_CodeFailed] DEFAULT 0;
            ";

        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string NormalizeIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return string.Empty;
        var s = ip.Trim();
        if (s.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring("::ffff:".Length);
        }
        return s;
    }

    private static async Task<bool> IsIpBlockedAsync(SqlConnection connection, string login, string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;

        const string sql = @"
            SELECT TOP 1 1
            FROM [App_BlockedIps]
            WHERE [Login] = @Login AND [Ip] = @Ip AND [BlockedUntil] > GETUTCDATE();";

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        cmd.Parameters.AddWithValue("@Ip", ip);
        var o = await cmd.ExecuteScalarAsync();
        return o != null;
    }

    private static async Task<bool> IsDeviceKnownAsync(SqlConnection connection, string login, string deviceId)
    {
        const string sql = @"SELECT TOP 1 1 FROM [App_LoginDevices] WHERE [Login] = @Login AND [DeviceId] = @DeviceId;";

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        cmd.Parameters.AddWithValue("@DeviceId", deviceId);
        var o = await cmd.ExecuteScalarAsync();
        return o != null;
    }

    private static async Task<bool> IsAnyDeviceKnownAsync(SqlConnection connection, string login)
    {
        const string sql = @"SELECT TOP 1 1 FROM [App_LoginDevices] WHERE [Login] = @Login;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        var o = await cmd.ExecuteScalarAsync();
        return o != null;
    }

    private static async Task TrustFirstDeviceAsync(
        SqlConnection connection,
        string login,
        string deviceId,
        string? deviceName,
        string ip,
        string userAgent)
    {
        const string sql = @"
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
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        cmd.Parameters.AddWithValue("@DeviceId", deviceId);
        cmd.Parameters.AddWithValue("@DeviceName", (object?)deviceName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Ip", (object?)ip ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UA", (object?)userAgent ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task UpdateDeviceSeenAsync(SqlConnection connection, string login, string deviceId, string? deviceName, string ip, string userAgent)
    {
        const string sql = @"
            UPDATE [App_LoginDevices]
            SET [LastSeenAt] = GETUTCDATE(),
                [DeviceName] = @DeviceName,
                [LastIp] = @Ip,
                [LastUserAgent] = @UA
            WHERE [Login] = @Login AND [DeviceId] = @DeviceId;";

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        cmd.Parameters.AddWithValue("@DeviceId", deviceId);
        cmd.Parameters.AddWithValue("@DeviceName", (object?)deviceName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Ip", (object?)ip ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UA", (object?)userAgent ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string GenerateSixDigitCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static async Task<int> CountPushTokensForLoginExceptDeviceAsync(
        SqlConnection connection,
        string login,
        string excludeDeviceId)
    {
        const string sql = @"
            IF OBJECT_ID('App_PushTokens', 'U') IS NULL
                SELECT 0;
            ELSE
                SELECT COUNT(1)
                FROM [App_PushTokens]
                WHERE [Login] = @Login
                  AND [Token] IS NOT NULL AND LTRIM(RTRIM([Token])) <> ''
                  AND ([DeviceId] IS NULL OR [DeviceId] <> @ExcludeDeviceId);";

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        cmd.Parameters.AddWithValue("@ExcludeDeviceId", excludeDeviceId);
        var o = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(o ?? 0);
    }

    private static async Task<(int AttemptId, string PlainCode)> CreatePendingDeviceLoginAttemptAsync(
        SqlConnection connection,
        string login,
        string deviceId,
        string? deviceName,
        string ip,
        string userAgent)
    {
        var plain = GenerateSixDigitCode();
        var hash = HashPassword(plain);
        var expires = DateTime.UtcNow.AddMinutes(10);

        const string sql = @"
            INSERT INTO [App_SecurityLoginAttempts] (
                [RecipientLogin], [DeviceId], [DeviceName], [Ip], [UserAgent], [Status],
                [CodeHash], [CodeExpiresAt], [CodeFailedAttempts])
            VALUES (@Login, @DeviceId, @DeviceName, @Ip, @UA, 'pending', @CodeHash, @Expires, 0);
            SELECT SCOPE_IDENTITY();";

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        cmd.Parameters.AddWithValue("@DeviceId", deviceId);
        cmd.Parameters.AddWithValue("@DeviceName", (object?)deviceName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Ip", (object?)ip ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UA", (object?)userAgent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CodeHash", hash);
        cmd.Parameters.AddWithValue("@Expires", expires);

        var o = await cmd.ExecuteScalarAsync();
        var id = Convert.ToInt32(o);
        return (id, plain);
    }

    private static async Task<bool> UpsertDeviceAndDetectNewAsync(
        SqlConnection connection,
        string login,
        string deviceId,
        string deviceName,
        string ip,
        string userAgent)
    {
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

    private static async Task InsertNotificationAsync(
        SqlConnection connection,
        string? recipientLogin,
        string? type,
        string title,
        string? body,
        string? action,
        string? actionData)
    {
        const string sql = @"
            INSERT INTO [App_Notifications] ([RecipientLogin], [Type], [Title], [Body], [CreatedAt], [Action], [ActionData])
            VALUES (@RecipientLogin, @Type, @Title, @Body, GETUTCDATE(), @Action, @ActionData);";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@RecipientLogin", (object?)recipientLogin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Type", (object?)type ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Title", title);
        cmd.Parameters.AddWithValue("@Body", (object?)body ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Action", (object?)action ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ActionData", (object?)actionData ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string GetPhoneLast10(string phoneNormalized)
    {
        if (string.IsNullOrWhiteSpace(phoneNormalized))
        {
            return string.Empty;
        }

        return phoneNormalized.Length >= 10
            ? phoneNormalized[^10..]
            : phoneNormalized;
    }

    private static string HashPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return string.Empty;
        }

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static string ToLoginBase(string lastName)
    {
        if (string.IsNullOrWhiteSpace(lastName)) return "user";
        lastName = lastName.Trim();

        var map = new Dictionary<char, string>
        {
            ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d",
            ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
            ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
            ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
            ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch",
            ['ш'] = "sh", ['щ'] = "sch", ['ы'] = "y", ['э'] = "e", ['ю'] = "yu",
            ['я'] = "ya",
            ['ь'] = "", ['ъ'] = ""
        };

        var sb = new StringBuilder();
        foreach (var ch in lastName.ToLowerInvariant())
        {
            if (map.TryGetValue(ch, out var s))
            {
                sb.Append(s);
            }
            else if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }

        var res = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(res)) return "user";
        return char.ToUpperInvariant(res[0]) + res.Substring(1);
    }

    private static async Task<bool> IsLoginTakenAsync(SqlConnection connection, string login)
    {
        const string sql = @"
            SELECT TOP 1 1
            FROM [Lexema_Кадры_ЛичнаяКарточка]
            WHERE [Логин] = @Login;";

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        var o = await cmd.ExecuteScalarAsync();
        return o != null && o != DBNull.Value;
    }

    private static async Task<bool> IsDismissedEmployeeAsync(SqlConnection connection, string? employeeId, string? login)
    {
        const string sql = @"
            SELECT TOP 1
                CASE WHEN TRY_CONVERT(datetime2, [ДатаУвольнения]) IS NULL THEN 0 ELSE 1 END
            FROM [Lexema_Кадры_ЛичнаяКарточка]
            WHERE
                (@EmployeeId <> '' AND TRY_CONVERT(nvarchar(50), [ТабельныйНомер]) = @EmployeeId)
                OR
                (@Login <> '' AND [Логин] = @Login);";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@EmployeeId", employeeId?.Trim() ?? string.Empty);
        cmd.Parameters.AddWithValue("@Login", login?.Trim() ?? string.Empty);
        var o = await cmd.ExecuteScalarAsync();
        return o != null && o != DBNull.Value && Convert.ToInt32(o) == 1;
    }

    private static async Task<int> GetFioCountAsync(
        SqlConnection connection,
        string lastName,
        string firstName,
        string patronymic)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM [Lexema_Кадры_ЛичнаяКарточка]
            WHERE [Фамилия] = @LastName
              AND [Имя] = @FirstName
              AND [Отчество] = @Patronymic;";

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@LastName", lastName);
        cmd.Parameters.AddWithValue("@FirstName", firstName);
        cmd.Parameters.AddWithValue("@Patronymic", patronymic);
        var o = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(o ?? 0);
    }

    private static async Task<int> GetFioRowNumberAsync(
        SqlConnection connection,
        string lastName,
        string firstName,
        string patronymic,
        string employeeId)
    {
        const string sql = @"
            WITH X AS (
                SELECT
                    [ТабельныйНомер] AS EmpId,
                    ROW_NUMBER() OVER (
                        ORDER BY
                            TRY_CONVERT(INT, [ТабельныйНомер]) ASC,
                            [ТабельныйНомер] ASC
                    ) AS rn
                FROM [Lexema_Кадры_ЛичнаяКарточка]
                WHERE
                    [Фамилия] = @LastName
                    AND [Имя] = @FirstName
                    AND [Отчество] = @Patronymic
            )
            SELECT TOP 1 rn
            FROM X
            WHERE EmpId = @EmployeeId;";

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@LastName", lastName);
        cmd.Parameters.AddWithValue("@FirstName", firstName);
        cmd.Parameters.AddWithValue("@Patronymic", patronymic);
        cmd.Parameters.AddWithValue("@EmployeeId", employeeId);

        var o = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(o ?? 1);
    }

    private static async Task<string> GenerateUniqueLoginAsync(
        SqlConnection connection,
        string loginBase,
        bool useNumbering,
        int startSuffix = 1)
    {
        
        if (!useNumbering && !await IsLoginTakenAsync(connection, loginBase))
            return loginBase;

        var suffix = Math.Max(1, startSuffix);
        while (true)
        {
            var candidate = $"{loginBase}{suffix}";
            if (!await IsLoginTakenAsync(connection, candidate))
                return candidate;
            suffix++;
        }
    }
}

public record VerifyRequest(
    string LastName,
    string FirstName,
    string Patronymic,
    string EmployeeId,
    string Phone,
    string PhoneNormalized,
    string? Login,
    string? Password);

public record VerifyResponse(
    bool Success,
    bool Exists,
    bool RegisteredInApp,
    string Message,
    string? Login = null,
    string? Password = null);

public record EmployeeProfile(
    string LastName,
    string FirstName,
    string Phone,
    string EmployeeId,
    string Position,
    string Subdivision,
    string? AvatarUrl,
    int Level,
    int Experience,
    int XpToNext,
    int CoinBalance = 0,
    int NextPayoutDays = 7);

public record ProfileResponse(bool Success, string Message, EmployeeProfile? Profile);

public record AvatarUploadResponse(bool Success, string Message, string? AvatarUrl);

public record LoginRequest(string Login, string Password, string? DeviceId, string? DeviceName, bool? ReloginBypass = null);

public record ConfirmDeviceLoginRequest(string Login, string Password, string DeviceId, string? DeviceName, int AttemptId, string? Code);

public record LoginResult(
    string LastName,
    string FirstName,
    string Phone,
    string EmployeeId,
    bool CanCreatePosts,
    bool IsTechAdmin,
    bool CanUseDevConsole);

public record LoginResponse(
    bool Success,
    string Message,
    LoginResult? Result,
    bool RequiresDeviceCode = false,
    int? PendingAttemptId = null);
public record WorkSchedule(string WorkPattern, string ShiftStart, string ShiftEnd, string VacationStart, string VacationEnd);
public record WorkScheduleResponse(bool Success, string Message, WorkSchedule? Schedule);

