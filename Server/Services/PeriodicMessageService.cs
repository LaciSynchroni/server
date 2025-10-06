using LaciSynchroni.Shared.Models;
using LaciSynchroni.Shared.Services;
using LaciSynchroni.Shared.Utils.Configuration;

namespace LaciSynchroni.Server.Services;

public class PeriodicMessageService(
    MessagingService messagingService,
    IConfigurationService<ServerConfiguration> serverConfig,
    ILogger<PeriodicMessageService> logger)
    : BackgroundService
{
    private int _currentExecutionNumber;

    private MessageConfiguration MessageConfiguration => serverConfig.GetValueOrDefault(
        nameof(Shared.Utils.Configuration.MessageConfiguration),
        MessageConfiguration.DefaultMessageConfig);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = MessageConfiguration.PeriodicMessageInterval;
        if (delay == null || delay.Value <= TimeSpan.Zero)
        {
            logger.LogInformation(
                "Periodic messages disabled: No LaciSynchroni.MessageConfiguration.PeriodicMessageInterval configured.");
            return;
        }

        if (delay.Value <= TimeSpan.FromMinutes(15))
        {
            logger.LogInformation(
                "Periodic messages disabled: LaciSynchroni.MessageConfiguration.PeriodicMessageInterval below 15 minutes. Don't spam your users!");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await SendNextMessage().ConfigureAwait(false);
            await Task.Delay(delay.Value, stoppingToken).ConfigureAwait(false);
        }
    }

    private Task SendNextMessage()
    {
        var message = GetNextPeriodicMessage(_currentExecutionNumber);
        if (message == null)
        {
            return Task.CompletedTask;
        }

        logger.LogInformation("Sending periodic message with severity {Severity} to all clients: {Message}",
            message.Severity, message.Message);
        _currentExecutionNumber++;
        return messagingService.SendMessageToAllClients(message);
    }

    private MessageWithSeverity? GetNextPeriodicMessage(int executionNumber)
    {
        var messageConfig = MessageConfiguration;
        var messageCount = messageConfig.PeriodicMessages.Length;
        if (messageCount <= 0)
        {
            return null;
        }

        return messageConfig.PeriodicMessages[executionNumber % messageCount];
    }
}