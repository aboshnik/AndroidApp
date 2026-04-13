using System.Text;
using Microsoft.Extensions.Hosting;

namespace EmployeeApi.Services;

/// <summary>
/// Кольцевой буфер последних ошибок текущего процесса API (для /stats в чате).
/// Не заменяет файловые логи; после перезапуска буфер пустой.
/// </summary>
public static class ServerDiagnosticsBuffer
{
    private const int MaxEntries = 50;
    private static readonly object Gate = new();
    private static readonly List<DiagnosticEntry> Entries = new();

    public sealed record DiagnosticEntry(DateTime Utc, string Kind, string Summary);

    public static void RecordHttp500(HttpContext? ctx, int status, string? extra = null)
    {
        if (status < 500) return;
        var path = ctx?.Request?.Path.Value ?? "?";
        if (path.Length > 160) path = string.Concat(path.AsSpan(0, 157), "...");
        var method = ctx?.Request?.Method ?? "?";
        var sum = $"{status} {method} {path}";
        if (!string.IsNullOrWhiteSpace(extra))
            sum += " | " + Sanitize(extra, 180);
        Push(new DiagnosticEntry(DateTime.UtcNow, "HTTP", sum));
    }

    public static void RecordUnhandled(string where, Exception ex, HttpContext? ctx = null)
    {
        var path = ctx?.Request?.Path.Value ?? "";
        if (path.Length > 120) path = string.Concat(path.AsSpan(0, 117), "...");
        var msg = string.IsNullOrEmpty(path)
            ? $"{where} | {ex.GetType().Name}: {Sanitize(ex.Message, 220)}"
            : $"{where} {path} | {ex.GetType().Name}: {Sanitize(ex.Message, 200)}";
        Push(new DiagnosticEntry(DateTime.UtcNow, "EXC", msg));
    }

    public static void RecordEvent(string message) =>
        Push(new DiagnosticEntry(DateTime.UtcNow, "EVENT", Sanitize(message, 320)));

    private static void Push(DiagnosticEntry e)
    {
        lock (Gate)
        {
            Entries.Add(e);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        }
    }

    /// <summary>Текст для вставки в отчёт /stats (сначала новые записи).</summary>
    public static string FormatRecentForStats(int maxTotalChars = 2800)
    {
        List<DiagnosticEntry> copy;
        lock (Gate)
            copy = new List<DiagnosticEntry>(Entries);

        if (copy.Count == 0)
            return "Лог процесса: записей ещё нет (5xx и сбои появятся здесь после события).";

        var sb = new StringBuilder();
        sb.AppendLine("─── Лог сервера (текущий процесс) ───");
        for (var i = copy.Count - 1; i >= 0; i--)
        {
            var e = copy[i];
            var line = $"{e.Utc:yyyy-MM-dd HH:mm}Z | {e.Kind} | {e.Summary}";
            if (sb.Length + line.Length + Environment.NewLine.Length > maxTotalChars)
                break;
            sb.AppendLine(line);
        }

        return sb.ToString().TrimEnd();
    }

    private static string Sanitize(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (s.Length > max) return string.Concat(s.AsSpan(0, max - 1), "…");
        return s;
    }
}

/// <summary>Фиксирует старт и graceful-остановку процесса.</summary>
public sealed class ServerDiagnosticsLifetimeHostedService(IHostApplicationLifetime lifetime) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ServerDiagnosticsBuffer.RecordEvent("API стартовал");
        lifetime.ApplicationStopping.Register(static () =>
            ServerDiagnosticsBuffer.RecordEvent("API останавливается (graceful shutdown)"));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
