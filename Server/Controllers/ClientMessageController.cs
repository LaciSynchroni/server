using LaciSynchroni.Server.Hubs;
using LaciSynchroni.Server.Services;
using LaciSynchroni.Shared.Models;
using LaciSynchroni.Shared.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LaciSynchroni.Server.Controllers;

[Route("/msgc")]
[Authorize(Policy = "Internal")]
public class ClientMessageController(ILogger<ClientMessageController> logger, MessagingService messagingService) : Controller
{
    [Route("sendMessage")]
    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] ClientMessage msg)
    {
        bool hasUid = !string.IsNullOrEmpty(msg.UID);
        var messageWithSeverity = new MessageWithSeverity(msg.Severity, msg.Message);
        if (!hasUid)
        {
            logger.LogInformation("Sending Message of severity {severity} to all online users: {message}", msg.Severity,
                msg.Message);
            await messagingService.SendMessageToAllClients(messageWithSeverity).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation("Sending Message of severity {severity} to user {uid}: {message}", msg.Severity,
                msg.UID, msg.Message);
            await messagingService.SendMessageToUser(msg.UID, messageWithSeverity).ConfigureAwait(false);
        }

        return Empty;
    }
}