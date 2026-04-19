using Microsoft.AspNetCore.SignalR;

namespace EmployeeApi.Hubs;

public class ChatRealtimeHub : Hub
{
    public Task Join(string login)
    {
        var group = NormalizeGroup(login);
        if (group == null) return Task.CompletedTask;
        return Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public Task Leave(string login)
    {
        var group = NormalizeGroup(login);
        if (group == null) return Task.CompletedTask;
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    public static string? NormalizeGroup(string? login)
    {
        var v = login?.Trim();
        if (string.IsNullOrWhiteSpace(v)) return null;
        return $"login:{v.ToLowerInvariant()}";
    }
}
