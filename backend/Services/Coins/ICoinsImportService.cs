namespace EmployeeApi.Services.Coins;

public interface ICoinsImportService
{
    Task<CoinsImportResult> ImportFromGoogleSheetAsync(bool overwriteExisting, CancellationToken cancellationToken = default);
}

public sealed record CoinsImportResult(
    bool Success,
    string Message,
    int Imported,
    int Skipped,
    int NotMatched
);

