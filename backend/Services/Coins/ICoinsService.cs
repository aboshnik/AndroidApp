namespace EmployeeApi.Services.Coins;

public interface ICoinsService
{
    Task<CoinBalanceResult> GetBalanceAsync(string login, CancellationToken cancellationToken = default);
    Task<CoinBalanceResult> AddCoinsAsync(string login, int amount, string? reason = null, CancellationToken cancellationToken = default);
    Task<CoinBalanceResult> SpendCoinsAsync(string login, int amount, string? reason = null, CancellationToken cancellationToken = default);
}

public sealed record CoinBalanceResult(
    bool Success,
    string Message,
    string Login,
    int Balance,
    int NextPayoutDays
);

