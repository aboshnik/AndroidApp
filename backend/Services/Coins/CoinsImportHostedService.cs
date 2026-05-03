namespace EmployeeApi.Services.Coins;

public sealed class CoinsImportHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CoinsImportHostedService> _logger;

    public CoinsImportHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<CoinsImportHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Google Sheets coins import removed.
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}

