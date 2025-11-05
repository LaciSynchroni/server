using LaciSynchroni.Common.Dto.Server;
using LaciSynchroni.Common.SignalR;
using LaciSynchroni.Shared.Services;
using LaciSynchroni.Shared.Utils.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using System.Text.Json;

namespace LaciSynchroni.Server.Controllers;

[ApiController]
[Route("/clientconfiguration")]
[AllowAnonymous]
public class ClientConfigurationController(
    IConfigurationService<ServerConfiguration> serverConfig, IConfigurationService<AuthServiceConfiguration> authConfig) : ControllerBase
{
    [Route("get")]
    public IActionResult GetConfiguration()
    {
        var discordOAuthUri = authConfig.GetValueOrDefault<Uri?>(nameof(AuthServiceConfiguration.PublicOAuthBaseUri), null);
        var discordClientSecret = authConfig.GetValueOrDefault<string?>(nameof(AuthServiceConfiguration.DiscordOAuthClientSecret), null);
        var discordClientId = authConfig.GetValueOrDefault<string?>(nameof(AuthServiceConfiguration.DiscordOAuthClientId), null);

        var configuration = new ConfigurationDto()
        {
            ServerName = serverConfig.GetValueOrDefault(nameof(ServerConfiguration.ServerName), "Laci Synchroni"),
            ServerVersion = Assembly.GetExecutingAssembly().GetName().Version,
            HubUri = IServerHub.Path.Equals("/hub") ? null : new Uri(serverConfig.GetValueOrDefault(nameof(ServerConfiguration.ServerPublicUri), new Uri("wss://noemptyuri")), IServerHub.Path),
            DiscordInvite = serverConfig.GetValueOrDefault<string>(nameof(ServerConfiguration.DiscordInvite), defaultValue: null),
            ServerRules = serverConfig.GetValueOrDefault<string>(nameof(ServerConfiguration.ServerRules), defaultValue: null),
            IsOAuthEnabled = discordOAuthUri != null && discordClientSecret != null && discordClientId != null,
        };

        return Ok(JsonSerializer.Serialize(configuration));
    }
}