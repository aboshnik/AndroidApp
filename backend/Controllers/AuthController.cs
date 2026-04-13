using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using EmployeeApi.Services;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ChatMessageCipher _chatCipher;

    public AuthController(IConfiguration configuration, ChatMessageCipher chatCipher)
    {
        _configuration = configuration;
        _chatCipher = chatCipher;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthRegisterResponse>> Register([FromBody] AuthRegisterRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.EmployeeId) || string.IsNullOrWhiteSpace(request.Phone))
            return BadRequest(new AuthRegisterResponse(false, "Укажите табельный номер и телефон", null));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            return StatusCode(500, new AuthRegisterResponse(false, "Не настроено подключение к БД", null));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await ChatController.EnsureChatTablesAsync(connection);
            await EnsureUserPermissionsTableExistsAsync(connection);

            var employeeId = request.EmployeeId.Trim();
            var phoneLast10 = GetPhoneLast10(NormalizePhone(request.Phone));

            const string findSql = @"
                SELECT TOP 1
                    COALESCE(TRY_CONVERT(nvarchar(200), [Фамилия]), '') AS LastName,
                    COALESCE(TRY_CONVERT(nvarchar(200), [Имя]), '') AS FirstName,
                    COALESCE(TRY_CONVERT(nvarchar(200), [Сотовый]), '') AS Phone,
                    COALESCE(TRY_CONVERT(nvarchar(50),  [ТабельныйНомер]), '') AS EmployeeId,
                    COALESCE(TRY_CONVERT(nvarchar(200), [Должность]), '') AS Position,
                    COALESCE(TRY_CONVERT(nvarchar(200), [Подразделение]), '') AS Subdivision,
                    COALESCE(TRY_CONVERT(nvarchar(100), [Логин]), '') AS CardLogin,
                    CAST(ISNULL([ЗарегВПриложении], 0) AS bit) AS RegisteredInApp
                FROM [Lexema_Кадры_ЛичнаяКарточка]
                WHERE TRY_CONVERT(nvarchar(50), [ТабельныйНомер]) = @EmployeeId
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
                  AND TRY_CONVERT(datetime2, [ДатаУвольнения]) IS NULL;";

            string lastName;
            string firstName;
            string phone;
            string position;
            string subdivision;
            string cardLogin;
            bool registeredInApp;

            await using (var cmd = new SqlCommand(findSql, connection))
            {
                cmd.Parameters.AddWithValue("@EmployeeId", employeeId);
                cmd.Parameters.AddWithValue("@PhoneLast10", phoneLast10);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Ok(new AuthRegisterResponse(false, "Сотрудник не найден", null));

                lastName = reader.GetString(0);
                firstName = reader.GetString(1);
                phone = reader.GetString(2);
                position = reader.GetString(4);
                subdivision = reader.GetString(5);
                cardLogin = reader.GetString(6);
                registeredInApp = reader.GetBoolean(7);
            }

            // If already registered: allow login by existing credentials
            if (registeredInApp && !string.IsNullOrWhiteSpace(cardLogin))
            {
                var isTechAdmin = await IsTechAdminAsync(connection, cardLogin);
                var canCreatePosts = await CanCreatePostsAsync(connection, cardLogin) || isTechAdmin;
                var canUseDevConsole = await CanUseDevConsoleAsync(connection, cardLogin);
                var result = new AuthRegisterResult(
                    Login: cardLogin,
                    EmployeeId: employeeId,
                    LastName: lastName,
                    FirstName: firstName,
                    Phone: phone,
                    Position: position,
                    Subdivision: subdivision,
                    CanCreatePosts: canCreatePosts,
                    IsTechAdmin: isTechAdmin,
                    CanUseDevConsole: canUseDevConsole
                );
                return Ok(new AuthRegisterResponse(true, "Сотрудник уже зарегистрирован. Выполните вход.", result));
            }

            // Generate login (based on last name) + random password
            var loginBase = ToLoginBase(lastName);
            var login = await GenerateUniqueLoginAsync(connection, loginBase);
            var password = GeneratePassword(12);
            var passwordHash = HashPassword(password);

            const string updateSql = @"
                UPDATE TOP (1) [Lexema_Кадры_ЛичнаяКарточка]
                SET [ЗарегВПриложении] = 1,
                    [Логин] = @Login,
                    [Пароль] = @PasswordHash
                WHERE TRY_CONVERT(nvarchar(50), [ТабельныйНомер]) = @EmployeeId;
                SELECT @@ROWCOUNT;";
            await using (var upd = new SqlCommand(updateSql, connection))
            {
                upd.Parameters.AddWithValue("@EmployeeId", employeeId);
                upd.Parameters.AddWithValue("@Login", login);
                upd.Parameters.AddWithValue("@PasswordHash", passwordHash);
                var o = await upd.ExecuteScalarAsync();
                var rows = Convert.ToInt32(o ?? 0);
                if (rows <= 0)
                    return Ok(new AuthRegisterResponse(false, "Не удалось зарегистрировать сотрудника", null));
            }

            // Notify in StekloSecurity chat
            var text = $"""
Добро пожаловать в наш корпоративный портал.

Ваш логин:
{login}

Ваш пароль:
{password}

Как скопировать данные:
1) Нажмите на это сообщение.
2) Выберите "Копировать текст" или "Выделить текст".
3) Вставьте данные в форму входа.

Не передавайте эти данные другим людям.
Обязательно запишите данный пароль себе в заметки!

""".Trim();
            await ChatController.InsertSecurityMessageAsync(connection, login, text, metaJson: null, _chatCipher);
            var reloginText = """
В связи с тем что на Ваш аккаунт установлен сложный пароль - просим вас перезайти в систему
Дабы перезайти в систему можете воспользоваться двумя способами:
Способ А:
1. Скопируйте данные(логин + пароль)
2. Перейдите в "Профиль"
3. Нажмите "Выйти из аккаунта"
4. Введите данные и нажмите "запомнить меня"
5.Войдите в аккаунт
Способ Б:
Под данным сообщением имеется кнопка "перезайти в аккаунт"
1.Нажмите на кнопку
2.Система сама вставит нужные данные и выберет "запомнить меня"
3.Нажмите "войти" либо же подождите 5 секунд,система сделает все автоматически
""".Trim();
            var reloginMeta = JsonSerializer.Serialize(new
            {
                action = "relogin_account",
                actionLabel = "Перезайти в аккаунт",
                actionLogin = login,
                actionPassword = password,
                actionAutoSeconds = 5
            });
            await ChatController.InsertSecurityMessageAsync(connection, login, reloginText, metaJson: reloginMeta, _chatCipher);

            var isTechAdminOut = await IsTechAdminAsync(connection, login);
            var canCreate = await CanCreatePostsAsync(connection, login) || isTechAdminOut;
            var canDev = await CanUseDevConsoleAsync(connection, login);
            var outResult = new AuthRegisterResult(
                Login: login,
                EmployeeId: employeeId,
                LastName: lastName,
                FirstName: firstName,
                Phone: phone,
                Position: position,
                Subdivision: subdivision,
                CanCreatePosts: canCreate,
                IsTechAdmin: isTechAdminOut,
                CanUseDevConsole: canDev
            );

            return Ok(new AuthRegisterResponse(true, "OK", outResult));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new AuthRegisterResponse(false, $"Ошибка: {ex.Message}", null));
        }
    }

    private static string NormalizePhone(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return new string(raw.Where(char.IsDigit).ToArray());
    }

    private static string GetPhoneLast10(string digits)
    {
        if (string.IsNullOrWhiteSpace(digits)) return string.Empty;
        return digits.Length >= 10 ? digits[^10..] : digits;
    }

    private static string GeneratePassword(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(alphabet[bytes[i] % alphabet.Length]);
        }
        return sb.ToString();
    }

    private static string HashPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password)) return string.Empty;
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static string ToLoginBase(string lastName)
    {
        if (string.IsNullOrWhiteSpace(lastName)) return "User";
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
            if (map.TryGetValue(ch, out var s)) sb.Append(s);
            else if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }

        var res = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(res)) return "User";
        return char.ToUpperInvariant(res[0]) + res.Substring(1);
    }

    private static async Task<string> GenerateUniqueLoginAsync(SqlConnection connection, string loginBase)
    {
        // If free, use base, otherwise add numeric suffix
        if (!await IsLoginTakenAsync(connection, loginBase)) return loginBase;
        var suffix = 2;
        while (true)
        {
            var candidate = $"{loginBase}{suffix}";
            if (!await IsLoginTakenAsync(connection, candidate)) return candidate;
            suffix++;
        }
    }

    private static async Task<bool> IsLoginTakenAsync(SqlConnection connection, string login)
    {
        const string sql = @"SELECT TOP 1 1 FROM [Lexema_Кадры_ЛичнаяКарточка] WHERE [Логин] = @Login;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        var o = await cmd.ExecuteScalarAsync();
        return o != null && o != DBNull.Value;
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

    private static async Task<bool> CanCreatePostsAsync(SqlConnection connection, string login)
    {
        const string sql = @"SELECT TOP 1 ISNULL([CanCreatePosts], 0) FROM [App_UserPermissions] WHERE [Login] = @Login;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        var o = await cmd.ExecuteScalarAsync();
        return o != null && o != DBNull.Value && Convert.ToInt32(o) == 1;
    }

    private static async Task<bool> CanUseDevConsoleAsync(SqlConnection connection, string login)
    {
        const string sql = @"SELECT TOP 1 ISNULL([CanUseDevConsole], 0) FROM [App_UserPermissions] WHERE [Login] = @Login;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        var o = await cmd.ExecuteScalarAsync();
        return o != null && o != DBNull.Value && Convert.ToInt32(o) == 1;
    }

    private static async Task<bool> IsTechAdminAsync(SqlConnection connection, string login)
    {
        const string sql = @"SELECT TOP 1 ISNULL([CanTechAdmin], 0) FROM [App_UserPermissions] WHERE [Login] = @Login;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);
        var o = await cmd.ExecuteScalarAsync();
        return o != null && o != DBNull.Value && Convert.ToInt32(o) == 1;
    }
}

public record AuthRegisterRequest(string EmployeeId, string Phone);

public record AuthRegisterResult(
    string Login,
    string EmployeeId,
    string LastName,
    string FirstName,
    string Phone,
    string Position,
    string Subdivision,
    bool CanCreatePosts,
    bool IsTechAdmin,
    bool CanUseDevConsole
);

public record AuthRegisterResponse(bool Success, string Message, AuthRegisterResult? Result);

