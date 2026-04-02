using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PushController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public PushController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("register")]
    public async Task<ActionResult<RegisterPushTokenResponse>> Register([FromBody] RegisterPushTokenRequest request)
    {
        if (request == null
            || string.IsNullOrWhiteSpace(request.Token)
            || string.IsNullOrWhiteSpace(request.DeviceId))
        {
            return BadRequest(new RegisterPushTokenResponse(false, "Укажите token и deviceId"));
        }

        Console.WriteLine($"PUSH REGISTER: login='{request.Login ?? ""}' deviceId='{request.DeviceId}' tokenLen={request.Token.Length}");

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return StatusCode(500, new RegisterPushTokenResponse(false, "Не настроено подключение к БД"));
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await EnsurePushTokensTableAsync(connection);

            const string sql = @"
                IF EXISTS (SELECT 1 FROM [App_PushTokens] WHERE [Token] = @Token)
                    UPDATE [App_PushTokens]
                    SET [Login] = COALESCE(NULLIF(@Login, ''), [Login]),
                        [DeviceId] = @DeviceId,
                        [DeviceName] = @DeviceName,
                        [Platform] = @Platform,
                        [UpdatedAt] = GETUTCDATE()
                    WHERE [Token] = @Token
                ELSE
                    INSERT INTO [App_PushTokens] ([Login], [Token], [DeviceId], [DeviceName], [Platform], [UpdatedAt])
                    VALUES (NULLIF(@Login, ''), @Token, @DeviceId, @DeviceName, @Platform, GETUTCDATE());";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Login", (object?)request.Login?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@Token", request.Token.Trim());
            cmd.Parameters.AddWithValue("@DeviceId", (object?)request.DeviceId?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DeviceName", (object?)request.DeviceName?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Platform", (object?)request.Platform?.Trim() ?? "android");
            await cmd.ExecuteNonQueryAsync();

            return Ok(new RegisterPushTokenResponse(true, "OK"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("PUSH REGISTER ERROR:");
            Console.WriteLine(ex.ToString());
            return StatusCode(500, new RegisterPushTokenResponse(false, $"Ошибка: {ex.Message}"));
        }
    }

    private static async Task EnsurePushTokensTableAsync(SqlConnection connection)
    {
        // 1) Create table if missing.
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

        // 2) Drop existing primary key (old schema used (Login, Token)).
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

        // 3) Ensure Login is nullable (old schema had NOT NULL).
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

        // 4) Remove duplicates by Token before adding PK(Token).
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

        // 5) Add PK(Token) (if not exists).
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
}

public record RegisterPushTokenRequest(string? Login, string Token, string? DeviceId, string? DeviceName, string? Platform);
public record RegisterPushTokenResponse(bool Success, string Message);

