using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Data.SqlClient;
using System.Text;

namespace EmployeeApi.Services.Coins;

public sealed class GoogleSheetsCoinsService : ICoinsImportService
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<GoogleSheetsCoinsService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private const int DefaultNextPayoutDays = 7;

    public GoogleSheetsCoinsService(
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<GoogleSheetsCoinsService> logger,
        ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _env = env;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<CoinsImportResult> ImportFromGoogleSheetAsync(bool overwriteExisting, CancellationToken cancellationToken = default)
    {
        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return new CoinsImportResult(false, "Не настроено подключение к БД", 0, 0, 0);

        var settings = ReadSettings();
        if (!string.IsNullOrWhiteSpace(settings.PublicCsvUrl))
        {
            return await ImportFromPublicCsvAsync(settings.PublicCsvUrl, settings, overwriteExisting, cancellationToken);
        }
        if (string.IsNullOrWhiteSpace(settings.SpreadsheetId) && string.IsNullOrWhiteSpace(settings.SheetUrl))
            return new CoinsImportResult(false, "Coins:GoogleSheets:SpreadsheetId/SheetUrl не задан", 0, 0, 0);
        if (string.IsNullOrWhiteSpace(settings.CredentialsPath))
            return new CoinsImportResult(false, "Coins:GoogleSheets:CredentialsPath не задан", 0, 0, 0);

        var spreadsheetId = ExtractSpreadsheetId(settings.SpreadsheetId, settings.SheetUrl);
        if (string.IsNullOrWhiteSpace(spreadsheetId))
            return new CoinsImportResult(false, "Не удалось определить SpreadsheetId", 0, 0, 0);

        var credentialsPath = ResolveCredentialsPath(settings.CredentialsPath);
        if (!File.Exists(credentialsPath))
            return new CoinsImportResult(false, $"Файл credentials не найден: {credentialsPath}", 0, 0, 0);

        try
        {
            var credential = GoogleCredential
                .FromFile(credentialsPath)
                .CreateScoped(SheetsService.Scope.Spreadsheets);
            using var service = new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "EmployeeApi Coins"
            });

            var range = $"{settings.SheetName}!A:Z";
            var getReq = service.Spreadsheets.Values.Get(spreadsheetId, range);
            var response = await getReq.ExecuteAsync(cancellationToken);
            var rows = response.Values ?? new List<IList<object>>();
            var keyCol = Math.Max(0, settings.LoginColumn - 1);
            var balanceCol = Math.Max(0, settings.BalanceColumn - 1);
            var imported = 0;
            var skipped = 0;
            var notMatched = 0;

            var sqlCoins = new SqlCoinsService(_configuration, _loggerFactory.CreateLogger<SqlCoinsService>());

            foreach (var row in rows)
            {
                var fioKey = GetCell(row, keyCol);
                if (string.IsNullOrWhiteSpace(fioKey))
                {
                    skipped++;
                    continue;
                }
                var cellBalance = GetCell(row, balanceCol);
                if (!int.TryParse(cellBalance, out var balance)) balance = 0;

                var login = await ResolveLoginByFioAsync(fioKey, cancellationToken);
                if (string.IsNullOrWhiteSpace(login))
                {
                    notMatched++;
                    continue;
                }

                if (!overwriteExisting)
                {
                    var current = await sqlCoins.GetBalanceAsync(login, cancellationToken);
                    if (current.Success && current.Balance > 0)
                    {
                        skipped++;
                        continue;
                    }
                }

                var upsert = await sqlCoins.UpsertBalanceFromImportAsync(login, Math.Max(0, balance), "google_sheet", cancellationToken);
                if (upsert.Success) imported++;
                else skipped++;
            }

            return new CoinsImportResult(true, "OK", imported, skipped, notMatched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Coins import from Google Sheets failed");
            return new CoinsImportResult(false, $"Ошибка Google Sheets: {ex.Message}", 0, 0, 0);
        }
    }

    private async Task<CoinsImportResult> ImportFromPublicCsvAsync(
        string csvUrl,
        CoinsGoogleSheetsSettings settings,
        bool overwriteExisting,
        CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient();
            var csv = await http.GetStringAsync(csvUrl, cancellationToken);
            var rows = ParseCsv(csv);
            if (rows.Count == 0)
                return new CoinsImportResult(false, "CSV пустой", 0, 0, 0);

            var keyCol = Math.Max(0, settings.LoginColumn - 1);
            var balanceCol = Math.Max(0, settings.BalanceColumn - 1);
            var imported = 0;
            var skipped = 0;
            var notMatched = 0;

            var sqlCoins = new SqlCoinsService(_configuration, _loggerFactory.CreateLogger<SqlCoinsService>());
            foreach (var row in rows)
            {
                var fioKey = GetCell(row, keyCol);
                if (string.IsNullOrWhiteSpace(fioKey) || fioKey.Equals("ФИО", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                var cellBalance = GetCell(row, balanceCol);
                if (!int.TryParse(cellBalance, out var balance)) balance = 0;

                var login = await ResolveLoginByFioAsync(fioKey, cancellationToken);
                if (string.IsNullOrWhiteSpace(login))
                {
                    notMatched++;
                    continue;
                }

                if (!overwriteExisting)
                {
                    var current = await sqlCoins.GetBalanceAsync(login, cancellationToken);
                    if (current.Success && current.Balance > 0)
                    {
                        skipped++;
                        continue;
                    }
                }

                var upsert = await sqlCoins.UpsertBalanceFromImportAsync(login, Math.Max(0, balance), "google_sheet_csv", cancellationToken);
                if (upsert.Success) imported++;
                else skipped++;
            }

            return new CoinsImportResult(true, "OK", imported, skipped, notMatched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Coins import from public CSV failed");
            return new CoinsImportResult(false, $"Ошибка CSV: {ex.Message}", 0, 0, 0);
        }
    }

    private CoinsGoogleSheetsSettings ReadSettings()
    {
        return new CoinsGoogleSheetsSettings
        {
            SpreadsheetId = _configuration["Coins:GoogleSheets:SpreadsheetId"] ?? "",
            SheetUrl = _configuration["Coins:GoogleSheets:SheetUrl"] ?? "",
            PublicCsvUrl = _configuration["Coins:GoogleSheets:PublicCsvUrl"] ?? "",
            SheetName = _configuration["Coins:GoogleSheets:SheetName"] ?? "Coins",
            CredentialsPath = _configuration["Coins:GoogleSheets:CredentialsPath"] ?? "",
            LoginColumn = ParseIntOrDefault(_configuration["Coins:GoogleSheets:LoginColumn"], 1),
            BalanceColumn = ParseIntOrDefault(_configuration["Coins:GoogleSheets:BalanceColumn"], 2)
        };
    }

    private string ResolveCredentialsPath(string rawPath)
    {
        if (Path.IsPathRooted(rawPath)) return rawPath;
        return Path.Combine(_env.ContentRootPath, rawPath);
    }

    private static int ParseIntOrDefault(string? raw, int fallback)
        => int.TryParse(raw, out var value) ? value : fallback;

    private async Task<string> ResolveLoginByFioAsync(string fio, CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString)) return "";
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            const string sql = @"
                SELECT TOP 1 COALESCE(TRY_CONVERT(nvarchar(100), [Логин]), '')
                FROM [Lexema_Кадры_ЛичнаяКарточка]
                WHERE LTRIM(RTRIM(CONCAT(
                    COALESCE(TRY_CONVERT(nvarchar(200), [Фамилия]), ''),
                    CASE WHEN COALESCE(TRY_CONVERT(nvarchar(200), [Имя]), '') = '' THEN '' ELSE ' ' + COALESCE(TRY_CONVERT(nvarchar(200), [Имя]), '') END,
                    CASE WHEN COALESCE(TRY_CONVERT(nvarchar(200), [Отчество]), '') = '' THEN '' ELSE ' ' + COALESCE(TRY_CONVERT(nvarchar(200), [Отчество]), '') END
                ))) = @Fio;";
            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Fio", fio);
            var raw = await cmd.ExecuteScalarAsync(cancellationToken);
            var login = (raw == null || raw == DBNull.Value) ? "" : Convert.ToString(raw)?.Trim();
            return string.IsNullOrWhiteSpace(login) ? "" : login!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve login by fio={Fio}", fio);
            return "";
        }
    }

    private static string GetCell(IList<object> row, int index)
    {
        if (index < 0 || index >= row.Count) return "";
        return Convert.ToString(row[index])?.Trim() ?? "";
    }

    private static List<IList<object>> ParseCsv(string csv)
    {
        var rows = new List<IList<object>>();
        using var reader = new StringReader(csv);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            rows.Add(ParseCsvLine(line).Select(x => (object)x).ToList());
        }
        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                result.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        result.Add(sb.ToString().Trim());
        return result;
    }

    private static string ExtractSpreadsheetId(string? spreadsheetIdRaw, string? sheetUrlRaw)
    {
        var id = spreadsheetIdRaw?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(id)) return id;
        var url = sheetUrlRaw?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url)) return "";
        try
        {
            var marker = "/d/";
            var idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            var from = idx + marker.Length;
            var to = url.IndexOf('/', from);
            if (to < 0) to = url.Length;
            return url[from..to].Trim();
        }
        catch
        {
            return "";
        }
    }

    private sealed class CoinsGoogleSheetsSettings
    {
        public string SpreadsheetId { get; init; } = "";
        public string SheetUrl { get; init; } = "";
        public string PublicCsvUrl { get; init; } = "";
        public string SheetName { get; init; } = "Coins";
        public string CredentialsPath { get; init; } = "";
        public int LoginColumn { get; init; } = 1;
        public int BalanceColumn { get; init; } = 2;
    }
}

