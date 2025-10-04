using LaciSynchroni.Common.SignalR;
using LaciSynchroni.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace LaciSynchroni.Server.Services;

public class PeriodicMessageService(
    MessageService messageService,
    IHubContext<ServerHub, IServerHub> hubContext,
    ILogger<PeriodicMessageService> logger)
    : BackgroundService
{
    private int _currentExecutionNumber;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = messageService.GetPeriodicMessageInterval();
        if (delay == null || delay.Value <= TimeSpan.Zero)
        {
            logger.LogInformation("Periodic messages disabled: No LaciSynchroni.MessageConfiguration.PeriodicMessageInterval configured.");
            return;
        }

        if (delay.Value <= TimeSpan.FromMinutes(15))
        {
            logger.LogInformation("Periodic messages disabled: LaciSynchroni.MessageConfiguration.PeriodicMessageInterval below 15 minutes. Don't spam your users!");
        }
        
        while (!stoppingToken.IsCancellationRequested)
        {
            await SendNextMessage().ConfigureAwait(false);
            await Task.Delay(delay.Value, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task SendNextMessage()
    {
        var message = messageService.GetNextPeriodicMessage(_currentExecutionNumber);
        if (message == null)
        {
            return;
        }
        logger.LogInformation("Sending message with severity {Severity} to all clients: {Message}", message.Severity, message.Message);
        await hubContext.Clients.All.Client_ReceiveServerMessage(message.Severity, message.Message).ConfigureAwait(false);
        _currentExecutionNumber++;
    }
}