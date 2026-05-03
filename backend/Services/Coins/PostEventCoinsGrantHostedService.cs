using Microsoft.Data.SqlClient;

namespace EmployeeApi.Services.Coins;

public sealed class PostEventCoinsGrantHostedService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ICoinsService _coinsService;
    private readonly ILogger<PostEventCoinsGrantHostedService> _logger;

    public PostEventCoinsGrantHostedService(
        IConfiguration configuration,
        ICoinsService coinsService,
        ILogger<PostEventCoinsGrantHostedService> logger)
    {
        _configuration = configuration;
        _coinsService = coinsService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Post event coins grant background loop failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs)) return;
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync(cancellationToken);

        const string ensureSql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_PostEventPendingCoins')
            CREATE TABLE [App_PostEventPendingCoins] (
                [Id] INT IDENTITY(1,1) PRIMARY KEY,
                [PostId] INT NOT NULL,
                [Login] NVARCHAR(100) NOT NULL,
                [Coins] INT NOT NULL,
                [DueAt] DATETIME2 NOT NULL,
                [GrantedAt] DATETIME2 NULL,
                [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_App_PostEventPendingCoins_DueAt')
                CREATE INDEX [IX_App_PostEventPendingCoins_DueAt] ON [App_PostEventPendingCoins]([DueAt], [GrantedAt]);";
        await using (var ensure = new SqlCommand(ensureSql, connection))
        {
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }

        const string loadSql = @"
            SELECT TOP 200 [Id], [Login], [Coins]
            FROM [App_PostEventPendingCoins]
            WHERE [GrantedAt] IS NULL AND [DueAt] <= GETUTCDATE()
            ORDER BY [DueAt], [Id];";
        var rows = new List<(int Id, string Login, int Coins)>();
        await using (var cmd = new SqlCommand(loadSql, connection))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
            }
        }

        foreach (var row in rows)
        {
            var result = await _coinsService.AddCoinsAsync(row.Login, row.Coins, "Регистрация на мероприятие", cancellationToken);
            if (!result.Success) continue;

            const string markSql = @"UPDATE [App_PostEventPendingCoins] SET [GrantedAt] = GETUTCDATE() WHERE [Id] = @Id;";
            await using var mark = new SqlCommand(markSql, connection);
            mark.Parameters.AddWithValue("@Id", row.Id);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

