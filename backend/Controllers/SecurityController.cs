using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SecurityController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public SecurityController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("approve")]
    public async Task<ActionResult<SecurityDecisionResponse>> Approve([FromBody] SecurityDecisionRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login) || request.AttemptId <= 0)
            return BadRequest(new SecurityDecisionResponse(false, "Неверные данные запроса"));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(500, new SecurityDecisionResponse(false, "Не настроено подключение к БД"));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await EnsureSecurityTablesAsync(connection);

            // Load attempt
            const string selectSql = @"
                SELECT TOP 1 [RecipientLogin], [DeviceId], [DeviceName], [Ip], [UserAgent], [Status]
                FROM [App_SecurityLoginAttempts]
                WHERE [Id] = @Id;";
            await using var selCmd = new SqlCommand(selectSql, connection);
            selCmd.Parameters.AddWithValue("@Id", request.AttemptId);

            var recipientLogin = "";
            var deviceId = "";
            string? deviceName = null;
            string? ip = null;
            string? ua = null;
            string? status = null;

            await using (var reader = await selCmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return Ok(new SecurityDecisionResponse(false, "Запрос не найден"));

                recipientLogin = reader.GetString(0);
                deviceId = reader.GetString(1);
                deviceName = reader.IsDBNull(2) ? null : reader.GetString(2);
                ip = reader.IsDBNull(3) ? null : reader.GetString(3);
                ua = reader.IsDBNull(4) ? null : reader.GetString(4);
                status = reader.IsDBNull(5) ? null : reader.GetString(5);
            }

            if (!string.Equals(recipientLogin, request.Login.Trim(), StringComparison.OrdinalIgnoreCase))
                return Ok(new SecurityDecisionResponse(false, "Нет прав на управление этим запросом"));

            if (!string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
                return Ok(new SecurityDecisionResponse(false, "Запрос уже обработан"));

            // Approve
            const string updateAttemptSql = @"
                UPDATE [App_SecurityLoginAttempts]
                SET [Status] = 'approved',
                    [ApprovedAt] = GETUTCDATE()
                WHERE [Id] = @Id;";
            await using (var upd = new SqlCommand(updateAttemptSql, connection))
            {
                upd.Parameters.AddWithValue("@Id", request.AttemptId);
                await upd.ExecuteNonQueryAsync();
            }

            // Save device as trusted
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
                upsert.Parameters.AddWithValue("@Login", request.Login.Trim());
                upsert.Parameters.AddWithValue("@DeviceId", deviceId);
                upsert.Parameters.AddWithValue("@DeviceName", (object?)deviceName ?? DBNull.Value);
                upsert.Parameters.AddWithValue("@Ip", (object?)ip ?? DBNull.Value);
                upsert.Parameters.AddWithValue("@UA", (object?)ua ?? DBNull.Value);
                await upsert.ExecuteNonQueryAsync();
            }

           
            if (!string.IsNullOrWhiteSpace(ip))
            {
                const string deleteBlockSql = @"
                    DELETE FROM [App_BlockedIps]
                    WHERE [Login] = @Login AND [Ip] = @Ip;";
                await using var del = new SqlCommand(deleteBlockSql, connection);
                del.Parameters.AddWithValue("@Login", request.Login.Trim());
                del.Parameters.AddWithValue("@Ip", ip);
                await del.ExecuteNonQueryAsync();
            }

            return Ok(new SecurityDecisionResponse(true, "Одобрено"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new SecurityDecisionResponse(false, $"Ошибка: {ex.Message}"));
        }
    }

    [HttpPost("deny")]
    public async Task<ActionResult<SecurityDecisionResponse>> Deny([FromBody] SecurityDecisionRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login) || request.AttemptId <= 0)
            return BadRequest(new SecurityDecisionResponse(false, "Неверные данные запроса"));

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(500, new SecurityDecisionResponse(false, "Не настроено подключение к БД"));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await EnsureSecurityTablesAsync(connection);

            const string selectSql = @"
                SELECT TOP 1 [RecipientLogin], [DeviceId], [Ip], [Status]
                FROM [App_SecurityLoginAttempts]
                WHERE [Id] = @Id;";
            await using var selCmd = new SqlCommand(selectSql, connection);
            selCmd.Parameters.AddWithValue("@Id", request.AttemptId);

            var recipientLogin = "";
            var deviceId = "";
            string? ip = null;
            string? status = null;

            await using (var reader = await selCmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return Ok(new SecurityDecisionResponse(false, "Запрос не найден"));

                recipientLogin = reader.GetString(0);
                deviceId = reader.GetString(1);
                ip = reader.IsDBNull(2) ? null : reader.GetString(2);
                status = reader.IsDBNull(3) ? null : reader.GetString(3);
            }

            if (!string.Equals(recipientLogin, request.Login.Trim(), StringComparison.OrdinalIgnoreCase))
                return Ok(new SecurityDecisionResponse(false, "Нет прав на управление этим запросом"));

            if (!string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
                return Ok(new SecurityDecisionResponse(false, "Запрос уже обработан"));

            const string updateAttemptSql = @"
                UPDATE [App_SecurityLoginAttempts]
                SET [Status] = 'denied',
                    [DeniedAt] = GETUTCDATE()
                WHERE [Id] = @Id;";
            await using (var upd = new SqlCommand(updateAttemptSql, connection))
            {
                upd.Parameters.AddWithValue("@Id", request.AttemptId);
                await upd.ExecuteNonQueryAsync();
            }

            // Block the IP for this login
            if (!string.IsNullOrWhiteSpace(ip))
            {
                // block for 24h
                const string upsertBlockSql = @"
                    IF EXISTS (SELECT 1 FROM [App_BlockedIps] WHERE [Login] = @Login AND [Ip] = @Ip)
                        UPDATE [App_BlockedIps]
                        SET [BlockedUntil] = DATEADD(hour, 24, GETUTCDATE())
                        WHERE [Login] = @Login AND [Ip] = @Ip
                    ELSE
                        INSERT INTO [App_BlockedIps] ([Login], [Ip], [BlockedUntil])
                        VALUES (@Login, @Ip, DATEADD(hour, 24, GETUTCDATE()));";
                await using var upsert = new SqlCommand(upsertBlockSql, connection);
                upsert.Parameters.AddWithValue("@Login", request.Login.Trim());
                upsert.Parameters.AddWithValue("@Ip", ip);
                await upsert.ExecuteNonQueryAsync();
            }

            return Ok(new SecurityDecisionResponse(true, "Запрещено"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new SecurityDecisionResponse(false, $"Ошибка: {ex.Message}"));
        }
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
}

public record SecurityDecisionRequest(string Login, int AttemptId);

public record SecurityDecisionResponse(bool Success, string Message);

