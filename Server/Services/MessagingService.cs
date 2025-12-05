using System.Globalization;
using LaciSynchroni.Common.SignalR;
using LaciSynchroni.Server.Hubs;
using LaciSynchroni.Shared.Models;
using LaciSynchroni.Shared.Services;
using LaciSynchroni.Shared.Utils.Configuration;
using Microsoft.AspNetCore.SignalR;

namespace LaciSynchroni.Server.Services;

/// <summary>
/// Allows sending of formatted message to the client. Use this instead of the plain <see cref="IServerHub.Client_ReceiveServerMessage"/> to
/// send formatted messages.
/// The following placeholders are supported:
/// <list type="bullet">
/// <item>%ServerName%</item>
/// <item>%DiscordInvite%</item>
/// <item>%ShardName%</item>
/// <item>%OnlineUsers%</item>
/// </list>
/// </summary>
public class MessagingService(IHubContext<ServerHub, IServerHub> hubContext, SystemInfoService systemInfoService, IConfigurationService<ServerConfiguration> serverConfig)
{
    
    private MessageWithSeverity MessageOfTheDay => serverConfig
        .GetValueOrDefault(nameof(MessageConfiguration), MessageConfiguration.DefaultMessageConfig).MessageOfTheDay;
    
    public Task SendMessageToAllClients(MessageWithSeverity message)
    {
        return SendMessageToClient(hubContext.Clients.All, message);
    }
    
    public Task SendMessageToUser(string uid, MessageWithSeverity message)
    {
        return SendMessageToClient(hubContext.Clients.User(uid), message);
    }

    public Task SendMessageOfTheDay(IServerHub caller)
    {
        return SendMessageToClient(caller, MessageOfTheDay);
    }

    private async Task SendMessageToClient(IServerHub hub, MessageWithSeverity messageWithSeverity)
    {
        var interpolatedMessage = InterpolateString(messageWithSeverity.Message);
        await hub.Client_ReceiveServerMessage(messageWithSeverity.Severity, interpolatedMessage).ConfigureAwait(false);
    }
    
    private string InterpolateString(string? input)
    {
        if (input == null)
        {
            return string.Empty;
        }
        return input
            .Replace("%ServerName%", serverConfig.GetValueOrDefault<string>(nameof(ServerConfiguration.ServerName), ""), StringComparison.Ordinal)
            .Replace("%DiscordInvite%", serverConfig.GetValueOrDefault<string>(nameof(ServerConfiguration.DiscordInvite), ""), StringComparison.Ordinal)
            .Replace("%ShardName%", serverConfig.GetValueOrDefault<string>(nameof(ServerConfiguration.ShardName), ""), StringComparison.Ordinal)
            .Replace("%OnlineUsers%", systemInfoService.SystemInfoDto.OnlineUsers.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}