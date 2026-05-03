using Microsoft.Data.SqlClient;

namespace EmployeeApi.Services.Coins;

public sealed class SqlCoinsService : ICoinsService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SqlCoinsService> _logger;
    private const int DefaultNextPayoutDays = 7;

    public SqlCoinsService(IConfiguration configuration, ILogger<SqlCoinsService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<CoinBalanceResult> GetBalanceAsync(string login, CancellationToken cancellationToken = default)
    {
        var normalized = login?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return new CoinBalanceResult(false, "Укажите login", normalized, 0, DefaultNextPayoutDays);

        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return new CoinBalanceResult(false, "Не настроено подключение к БД", normalized, 0, DefaultNextPayoutDays);

        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            await EnsureCoinsTablesAsync(connection, cancellationToken);
            var ownerLogin = await ResolveOwnerLoginAsync(connection, normalized, cancellationToken);
            var balance = await GetBalanceInternalAsync(connection, ownerLogin, cancellationToken);
            return new CoinBalanceResult(true, "OK", ownerLogin, balance, DefaultNextPayoutDays);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Coins GetBalance failed for {Login}", normalized);
            return new CoinBalanceResult(false, $"Ошибка БД: {ex.Message}", normalized, 0, DefaultNextPayoutDays);
        }
    }

    public async Task<CoinBalanceResult> AddCoinsAsync(string login, int amount, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (amount <= 0) return new CoinBalanceResult(false, "Сумма должна быть больше 0", login?.Trim() ?? "", 0, DefaultNextPayoutDays);
        return await MutateAsync(login, amount, type: "grant", reason, actor: "system", cancellationToken);
    }

    public async Task<CoinBalanceResult> SpendCoinsAsync(string login, int amount, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (amount <= 0) return new CoinBalanceResult(false, "Сумма должна быть больше 0", login?.Trim() ?? "", 0, DefaultNextPayoutDays);
        return await MutateAsync(login, -amount, type: "spend", reason, actor: "user", cancellationToken);
    }

    public async Task<(bool Success, string Message, string Login, int Balance)> UpsertBalanceFromImportAsync(
        string login,
        int balance,
        string source,
        CancellationToken cancellationToken = default)
    {
        var normalized = login?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return (false, "Укажите login", normalized, 0);
        if (balance < 0) balance = 0;

        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return (false, "Не настроено подключение к БД", normalized, 0);

        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            await EnsureCoinsTablesAsync(connection, cancellationToken);
            var ownerLogin = await ResolveOwnerLoginAsync(connection, normalized, cancellationToken);

            await using var txDb = await connection.BeginTransactionAsync(cancellationToken);
            var tx = (SqlTransaction)txDb;
            var current = await GetBalanceInternalAsync(connection, ownerLogin, cancellationToken, tx);
            await SetBalanceInternalAsync(connection, ownerLogin, balance, cancellationToken, tx);
            var delta = balance - current;
            if (delta != 0)
            {
                await InsertTransferInternalAsync(connection, ownerLogin, delta, balance, "import", source, "importer", cancellationToken, tx);
            }
            await tx.CommitAsync(cancellationToken);
            return (true, "OK", ownerLogin, balance);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Coins import upsert failed for {Login}", normalized);
            return (false, $"Ошибка БД: {ex.Message}", normalized, 0);
        }
    }

    private async Task<CoinBalanceResult> MutateAsync(
        string login,
        int delta,
        string type,
        string? reason,
        string actor,
        CancellationToken cancellationToken)
    {
        var normalized = login?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return new CoinBalanceResult(false, "Укажите login", normalized, 0, DefaultNextPayoutDays);

        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return new CoinBalanceResult(false, "Не настроено подключение к БД", normalized, 0, DefaultNextPayoutDays);

        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            await EnsureCoinsTablesAsync(connection, cancellationToken);
            var ownerLogin = await ResolveOwnerLoginAsync(connection, normalized, cancellationToken);

            await using var txDb = await connection.BeginTransactionAsync(cancellationToken);
            var tx = (SqlTransaction)txDb;
            var current = await GetBalanceInternalAsync(connection, ownerLogin, cancellationToken, tx);
            var next = current + delta;
            if (next < 0)
            {
                await tx.RollbackAsync(cancellationToken);
                return new CoinBalanceResult(false, "Недостаточно коинов", ownerLogin, current, DefaultNextPayoutDays);
            }

            await SetBalanceInternalAsync(connection, ownerLogin, next, cancellationToken, tx);
            await InsertTransferInternalAsync(connection, ownerLogin, delta, next, type, reason, actor, cancellationToken, tx);
            await tx.CommitAsync(cancellationToken);

            return new CoinBalanceResult(true, "OK", ownerLogin, next, DefaultNextPayoutDays);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Coins mutation failed for {Login}", normalized);
            return new CoinBalanceResult(false, $"Ошибка БД: {ex.Message}", normalized, 0, DefaultNextPayoutDays);
        }
    }

    private static async Task EnsureCoinsTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_Balance')
            CREATE TABLE [App_Balance] (
                [Login] NVARCHAR(100) NOT NULL PRIMARY KEY,
                [Balance] INT NOT NULL CONSTRAINT [DF_App_Balance_Balance] DEFAULT 0,
                [UpdatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_App_Balance_UpdatedAt] DEFAULT GETUTCDATE()
            );

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_CoinsTransfers')
            CREATE TABLE [App_CoinsTransfers] (
                [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [Login] NVARCHAR(100) NOT NULL,
                [Delta] INT NOT NULL,
                [BalanceAfter] INT NOT NULL,
                [Type] NVARCHAR(50) NOT NULL,
                [Reason] NVARCHAR(500) NULL,
                [Actor] NVARCHAR(100) NULL,
                [Source] NVARCHAR(100) NULL,
                [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_App_CoinsTransfers_CreatedAt] DEFAULT GETUTCDATE()
            );

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_App_CoinsTransfers_Login_CreatedAt')
            CREATE INDEX [IX_App_CoinsTransfers_Login_CreatedAt] ON [App_CoinsTransfers]([Login], [CreatedAt] DESC);";
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> GetBalanceInternalAsync(SqlConnection connection, string login, CancellationToken cancellationToken, SqlTransaction? tx = null)
    {
        const string sql = @"SELECT TOP 1 ISNULL([Balance], 0) FROM [App_Balance] WHERE [Login] = @Login;";
        await using var cmd = new SqlCommand(sql, connection, tx);
        cmd.Parameters.AddWithValue("@Login", login);
        var o = await cmd.ExecuteScalarAsync(cancellationToken);
        return (o == null || o == DBNull.Value) ? 0 : Convert.ToInt32(o);
    }

    private static async Task SetBalanceInternalAsync(SqlConnection connection, string login, int balance, CancellationToken cancellationToken, SqlTransaction? tx = null)
    {
        const string sql = @"
            IF EXISTS (SELECT 1 FROM [App_Balance] WHERE [Login] = @Login)
                UPDATE [App_Balance]
                SET [Balance] = @Balance,
                    [UpdatedAt] = GETUTCDATE()
                WHERE [Login] = @Login
            ELSE
                INSERT INTO [App_Balance]([Login], [Balance], [UpdatedAt])
                VALUES (@Login, @Balance, GETUTCDATE());";
        await using var cmd = new SqlCommand(sql, connection, tx);
        cmd.Parameters.AddWithValue("@Login", login);
        cmd.Parameters.AddWithValue("@Balance", balance);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertTransferInternalAsync(
        SqlConnection connection,
        string login,
        int delta,
        int balanceAfter,
        string type,
        string? reason,
        string? actor,
        CancellationToken cancellationToken,
        SqlTransaction? tx = null)
    {
        const string sql = @"
            INSERT INTO [App_CoinsTransfers]([Login], [Delta], [BalanceAfter], [Type], [Reason], [Actor], [Source], [CreatedAt])
            VALUES (@Login, @Delta, @BalanceAfter, @Type, @Reason, @Actor, @Source, GETUTCDATE());";
        await using var cmd = new SqlCommand(sql, connection, tx);
        cmd.Parameters.AddWithValue("@Login", login);
        cmd.Parameters.AddWithValue("@Delta", delta);
        cmd.Parameters.AddWithValue("@BalanceAfter", balanceAfter);
        cmd.Parameters.AddWithValue("@Type", type);
        cmd.Parameters.AddWithValue("@Reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Actor", (object?)actor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Source", DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> ResolveOwnerLoginAsync(SqlConnection connection, string loginOrEmployeeId, CancellationToken cancellationToken)
    {
        var key = loginOrEmployeeId.Trim();
        if (!key.All(char.IsDigit)) return key;
        const string sql = @"
            SELECT TOP 1 COALESCE(TRY_CONVERT(nvarchar(200), [Логин]), '')
            FROM [Lexema_Кадры_ЛичнаяКарточка]
            WHERE TRY_CONVERT(nvarchar(50), [ТабельныйНомер]) = @EmployeeId;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@EmployeeId", key);
        var o = await cmd.ExecuteScalarAsync(cancellationToken);
        var resolved = (o == null || o == DBNull.Value) ? "" : Convert.ToString(o)?.Trim();
        return string.IsNullOrWhiteSpace(resolved) ? key : resolved!;
    }
}

