using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using EmployeeApi.Services;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmployeeController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    public EmployeeController(IConfiguration configuration, IWebHostEnvironment env)
    {
        _configuration = configuration;
        _env = env;
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
                    )";

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
                    );
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
                    (@EmployeeId <> '' AND TRY_CONVERT(nvarchar(50), C.[ТабельныйНомер]) = @EmployeeId)
                    OR
                    (@Login <> '' AND C.[Логин] = @Login);";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@EmployeeId", employeeId ?? string.Empty);
            cmd.Parameters.AddWithValue("@Login", login ?? string.Empty);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return Ok(new ProfileResponse(true, "Сотрудник не найден", null));
            }

            var experience = reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetValue(7));
            var avatarFileName = reader.IsDBNull(8) ? null : reader.GetString(8);
            var level = Math.Max(1, 1 + experience / 100);
            var xpToNext = ComputeXpToNextWithinLevel(experience);

            var avatarUrl = BuildAvatarPublicUrl(avatarFileName);

            var profile = new EmployeeProfile(
                LastName: reader.GetString(0),
                FirstName: reader.GetString(1),
                Phone: reader.GetString(2),
                EmployeeId: reader.GetString(3),
                Position: reader.GetString(4),
                Subdivision: reader.GetString(5),
                AvatarUrl: avatarUrl,
                Level: level,
                Experience: experience,
                XpToNext: xpToNext
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

        var req = Request;
        var baseUrl = $"{req.Scheme}://{req.Host}{req.PathBase}";
        return $"{baseUrl}/uploads/avatars/{Uri.EscapeDataString(avatarFileName)}";
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
        const string sql = @"SELECT [AvatarFileName] FROM [App_UserProfile] WHERE [Login] = @Login;";
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

            string lastName;
            string firstName;
            string phone;
            string employeeId;
            bool canCreatePosts;
            bool canUseDevConsole;

            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                {
                    return Ok(new LoginResponse(false, "Неверный логин или пароль", null));
                }

                lastName = reader.GetString(0);
                firstName = reader.GetString(1);
                phone = reader.GetString(2);
                employeeId = reader.GetString(3);
                canCreatePosts = Convert.ToInt32(reader.GetValue(4)) == 1;
                canUseDevConsole = Convert.ToInt32(reader.GetValue(5)) == 1;
            }  

            var result = new LoginResult(
                LastName: lastName,
                FirstName: firstName,
                Phone: phone,
                EmployeeId: employeeId,
                CanCreatePosts: canCreatePosts,
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

           
            var isKnownDevice = await IsDeviceKnownAsync(connection, login, deviceId);
            if (isKnownDevice)
            {
                await UpdateDeviceSeenAsync(connection, login, deviceId, deviceName, ipNorm, userAgent);
            }
            else
            {
                
                if (!hasAnyDevices)
                {
                    await TrustFirstDeviceAsync(connection, login, deviceId, deviceName, ipNorm, userAgent);
                }
                else
                {
                var attemptId = await CreateSecurityAttemptAsync(connection, login, deviceId, deviceName, ipNorm, userAgent);
                await InsertNotificationAsync(
                    connection,
                    recipientLogin: login,
                    type: "security",
                    title: "Безопасность: требуется подтверждение входа",
                    body: $"Попытка входа с устройства: {deviceName}. IP: {ipNorm}",
                    action: "security_login",
                    actionData: attemptId.ToString());

               
                if (FcmPush.IsConfigured())
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await FcmPush.SendToLoginAsync(
                                connectionString,
                                login,
                                "Безопасность: требуется подтверждение входа",
                                $"Попытка входа с устройства: {deviceName}. IP: {ipNorm}",
                                new Dictionary<string, string>
                                {
                                    ["type"] = "security",
                                    ["action"] = "security_login",
                                    ["actionData"] = attemptId.ToString()
                                });
                        }
                        catch
                        {
                            
                        }
                    });
                }

                return Ok(new LoginResponse(false, "Вход подтверждается владельцем. Попробуйте войти снова после одобрения.", null));
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
                [CanUseDevConsole] BIT NOT NULL DEFAULT 0,
                [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );
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
            );";

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

    private static async Task<int> CreateSecurityAttemptAsync(SqlConnection connection, string login, string deviceId, string? deviceName, string ip, string userAgent)
    {
         
        const string sql = @"
            INSERT INTO [App_SecurityLoginAttempts] ([RecipientLogin], [DeviceId], [DeviceName], [Ip], [UserAgent], [Status])
            VALUES (@Login, @DeviceId, @DeviceName, @Ip, @UA, 'pending');
            SELECT SCOPE_IDENTITY();";

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        cmd.Parameters.AddWithValue("@DeviceId", deviceId);
        cmd.Parameters.AddWithValue("@DeviceName", (object?)deviceName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Ip", (object?)ip ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UA", (object?)userAgent ?? DBNull.Value);

        var o = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(o);
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
    int XpToNext);

public record ProfileResponse(bool Success, string Message, EmployeeProfile? Profile);

public record AvatarUploadResponse(bool Success, string Message, string? AvatarUrl);

public record LoginRequest(string Login, string Password, string? DeviceId, string? DeviceName);

public record LoginResult(
    string LastName,
    string FirstName,
    string Phone,
    string EmployeeId,
    bool CanCreatePosts,
    bool CanUseDevConsole);

public record LoginResponse(bool Success, string Message, LoginResult? Result);
public record WorkSchedule(string WorkPattern, string ShiftStart, string ShiftEnd, string VacationStart, string VacationEnd);
public record WorkScheduleResponse(bool Success, string Message, WorkSchedule? Schedule);

