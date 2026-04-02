using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Data.SqlClient;

namespace EmployeeApi.Services;

public static class FcmPush
{
    public static bool IsConfigured()
    {
        var path = GetServiceAccountPath();
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    public static string? GetServiceAccountPath()
    {
        return Environment.GetEnvironmentVariable("FCM_SERVICE_ACCOUNT_PATH")?.Trim().Trim('"');
    }

    public static void EnsureFirebaseInitialized()
    {
        if (FirebaseApp.DefaultInstance != null) return;

        var serviceAccountPath = GetServiceAccountPath();
        if (string.IsNullOrWhiteSpace(serviceAccountPath))
            throw new InvalidOperationException("Missing env var FCM_SERVICE_ACCOUNT_PATH (path to Firebase service account json).");

        if (!File.Exists(serviceAccountPath))
            throw new FileNotFoundException("FCM service account json not found.", serviceAccountPath);

        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromFile(serviceAccountPath)
        });
    }

    public static async Task<int> SendToLoginAsync(
        string connectionString,
        string login,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null)
    {
        if (string.IsNullOrWhiteSpace(login)) return 0;
        EnsureFirebaseInitialized();

        var tokens = await GetTokensForLoginAsync(connectionString, login.Trim());
        if (tokens.Count == 0) return 0;

        var msg = new MulticastMessage
        {
            Tokens = tokens,
            Notification = new Notification { Title = title, Body = body },
            Data = data != null ? new Dictionary<string, string>(data) : null,
            Android = new AndroidConfig { Priority = Priority.High }
        };

        var result = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(msg);
        return result.SuccessCount;
    }

    public static async Task<int> SendBroadcastAsync(
        string connectionString,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null)
    {
        EnsureFirebaseInitialized();

        var tokens = await GetAllTokensAsync(connectionString);
        if (tokens.Count == 0) return 0;

        var msg = new MulticastMessage
        {
            Tokens = tokens,
            Notification = new Notification { Title = title, Body = body },
            Data = data != null ? new Dictionary<string, string>(data) : null,
            Android = new AndroidConfig { Priority = Priority.High }
        };

        var result = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(msg);
        return result.SuccessCount;
    }

    private static async Task<List<string>> GetTokensForLoginAsync(string connectionString, string login)
    {
        var tokens = new List<string>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        const string sql = @"
            IF OBJECT_ID('App_PushTokens', 'U') IS NULL
                SELECT CAST(NULL AS NVARCHAR(300)) AS Token WHERE 1=0;
            ELSE
                SELECT DISTINCT [Token]
                FROM [App_PushTokens]
                WHERE [Login] = @Login AND [Token] IS NOT NULL AND LTRIM(RTRIM([Token])) <> '';";

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", login);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.IsDBNull(0)) continue;
            var t = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(t)) tokens.Add(t.Trim());
        }
        return tokens;
    }

    private static async Task<List<string>> GetAllTokensAsync(string connectionString)
    {
        var tokens = new List<string>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        const string sql = @"
            IF OBJECT_ID('App_PushTokens', 'U') IS NULL
                SELECT CAST(NULL AS NVARCHAR(300)) AS Token WHERE 1=0;
            ELSE
                SELECT DISTINCT [Token]
                FROM [App_PushTokens]
                WHERE [Token] IS NOT NULL AND LTRIM(RTRIM([Token])) <> '';";

        await using var cmd = new SqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.IsDBNull(0)) continue;
            var t = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(t)) tokens.Add(t.Trim());
        }
        return tokens;
    }
}

