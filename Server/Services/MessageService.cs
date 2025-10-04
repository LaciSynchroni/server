using System.Globalization;
using LaciSynchroni.Shared.Services;
using LaciSynchroni.Shared.Utils.Configuration;

namespace LaciSynchroni.Server.Services;

public class MessageService(
    IConfigurationService<ServerConfiguration> serverConfig,
    SystemInfoService systemInfoService)
{
    public MessageConfiguration.MessageWithSeverity GetMessageOfTheDay()
    {
        var messageConfig = serverConfig.GetValue<MessageConfiguration>(nameof(MessageConfiguration));
        var messageOfTheDay = messageConfig.MessageOfTheDay;
        return new MessageConfiguration.MessageWithSeverity(messageOfTheDay.Severity, InterpolateString(messageOfTheDay.Message));
    }

    public MessageConfiguration.MessageWithSeverity? GetNextPeriodicMessage(int executionNumber)
    {
        var messageConfig = serverConfig.GetValue<MessageConfiguration>(nameof(MessageConfiguration));
        var messageCount = messageConfig.PeriodicMessages.Length;
        if (messageCount <= 0)
        {
            return null;
        }

        var message = messageConfig.PeriodicMessages[executionNumber % messageCount];
        return new MessageConfiguration.MessageWithSeverity(message.Severity, InterpolateString(message.Message));
    }

    public TimeSpan? GetPeriodicMessageInterval()
    {
        var messageConfig = serverConfig.GetValue<MessageConfiguration>(nameof(MessageConfiguration));
        return messageConfig.PeriodicMessageInterval;
    }


    private string InterpolateString(string? input)
    {
        return input?.Replace("%ServerName%", serverConfig.GetValue<string>(nameof(ServerConfiguration.ServerName)))
            .Replace("%DiscordInvite%", serverConfig.GetValue<string>(nameof(ServerConfiguration.DiscordInvite)))
            .Replace("%ShardName%", serverConfig.GetValue<string>(nameof(ServerConfiguration.ShardName)))
            .Replace("%OnlineUsers%", systemInfoService.SystemInfoDto.OnlineUsers.ToString(CultureInfo.InvariantCulture));
    } 
}