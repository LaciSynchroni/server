using LaciSynchroni.Common.Routes;
using LaciSynchroni.AuthService.Services;
using LaciSynchroni.Shared;
using LaciSynchroni.Shared.Data;
using LaciSynchroni.Shared.Services;
using LaciSynchroni.Shared.Utils;
using LaciSynchroni.Shared.Utils.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace LaciSynchroni.AuthService.Controllers;

[Route(AuthRoutes.Auth)]
public class JwtController : AuthControllerBase
{
    public JwtController(ILogger<JwtController> logger,
        IHttpContextAccessor accessor, IDbContextFactory<LaciDbContext> dbContextFactory,
        SecretKeyAuthenticatorService secretKeyAuthenticatorService,
        IConfigurationService<AuthServiceConfiguration> configuration,
        IDatabase redisDb)
            : base(logger, accessor, dbContextFactory, secretKeyAuthenticatorService,
                configuration, redisDb)
    {
    }

    [AllowAnonymous]
    [HttpPost(AuthRoutes.Auth_CreateIdent)]
    public async Task<IActionResult> CreateToken(string auth, string charaIdent)
    {
        using var dbContext = await DbContextFactory.CreateDbContextAsync();
        return await AuthenticateInternal(dbContext, auth, charaIdent).ConfigureAwait(false);
    }

    [Authorize(Policy = "Authenticated")]
    [HttpGet(AuthRoutes.Auth_RenewToken)]
    public async Task<IActionResult> RenewToken()
    {
        using var dbContext = await DbContextFactory.CreateDbContextAsync();
        try
        {
            var uid = HttpContext.User.Claims.Single(p => string.Equals(p.Type, LaciClaimTypes.Uid, StringComparison.Ordinal))!.Value;
            var ident = HttpContext.User.Claims.Single(p => string.Equals(p.Type, LaciClaimTypes.CharaIdent, StringComparison.Ordinal))!.Value;
            var alias = HttpContext.User.Claims.SingleOrDefault(p => string.Equals(p.Type, LaciClaimTypes.Alias))?.Value ?? string.Empty;

            if (await dbContext.Auth.Where(u => u.UserUID == uid || u.PrimaryUserUID == uid).AnyAsync(a => a.MarkForBan))
            {
                var userAuth = await dbContext.Auth.SingleAsync(u => u.UserUID == uid);
                await EnsureBan(uid, userAuth.PrimaryUserUID, ident);

                return Unauthorized("Your account is banned.");
            }

            if (await IsIdentBanned(dbContext, ident))
            {
                return Unauthorized("Your XIV service account is banned from using the service.");
            }

            Logger.LogInformation("RenewToken:SUCCESS:{id}:{ident}", uid, ident);
            return await CreateJwtFromId(uid, ident, alias);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "RenewToken:FAILURE");
            return Unauthorized("Unknown error while renewing authentication token");
        }
    }

    protected async Task<IActionResult> AuthenticateInternal(LaciDbContext dbContext, string auth, string charaIdent)
    {
        try
        {
            if (string.IsNullOrEmpty(auth)) return BadRequest("No Authkey");
            if (string.IsNullOrEmpty(charaIdent)) return BadRequest("No CharaIdent");

            var remoteIp = HttpAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

            var authResult = await SecretKeyAuthenticatorService.AuthorizeAsync(remoteIp, auth);

            return await GenericAuthResponse(dbContext, charaIdent, authResult);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Authenticate:UNKNOWN");
            return Unauthorized("Unknown internal server error during authentication");
        }
    }
}