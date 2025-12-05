using LaciSynchroni.AuthService.Authentication;
using LaciSynchroni.AuthService.Services;
using LaciSynchroni.Shared.Data;
using LaciSynchroni.Shared.Models;
using LaciSynchroni.Shared.Services;
using LaciSynchroni.Shared.Utils;
using LaciSynchroni.Shared.Utils.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace LaciSynchroni.AuthService.Controllers;

public abstract class AuthControllerBase : Controller
{
    protected readonly ILogger Logger;
    protected readonly IConfigurationService<AuthServiceConfiguration> Configuration;
    protected readonly IDbContextFactory<LaciDbContext> DbContextFactory;
    protected readonly SecretKeyAuthenticatorService SecretKeyAuthenticatorService;
    private readonly GeoIpService _geoIpService;
    private readonly IDatabase _redis;

    protected AuthControllerBase(ILogger logger,
    IDbContextFactory<LaciDbContext> dbContextFactory,
    SecretKeyAuthenticatorService secretKeyAuthenticatorService,
    IConfigurationService<AuthServiceConfiguration> configuration,
    IDatabase redisDb, GeoIpService geoIpService)
    {
        Logger = logger;
        _redis = redisDb;
        _geoIpService = geoIpService;
        DbContextFactory = dbContextFactory;
        SecretKeyAuthenticatorService = secretKeyAuthenticatorService;
        Configuration = configuration;
    }

    protected async Task<IActionResult> GenericAuthResponse(LaciDbContext dbContext, string charaIdent, SecretKeyAuthReply authResult)
    {
        if (await IsIdentBanned(dbContext, charaIdent))
        {
            Logger.LogWarning("Authenticate:IDENTBAN:{id}:{ident}", authResult.Uid, charaIdent);
            return Unauthorized("Your character is banned from using the service.");
        }

        if (!authResult.Success && !authResult.TempBan)
        {
            Logger.LogWarning("Authenticate:INVALID:{id}:{ident}", authResult?.Uid ?? "NOUID", charaIdent);
            return Unauthorized("The provided secret key is invalid. Verify your accounts existence and/or recover the secret key.");
        }
        if (!authResult.Success && authResult.TempBan)
        {
            Logger.LogWarning("Authenticate:TEMPBAN:{id}:{ident}", authResult.Uid ?? "NOUID", charaIdent);
            return Unauthorized("Due to an excessive amount of failed authentication attempts you are temporarily locked out. Check your Secret Key configuration and try connecting again in 5 minutes.");
        }

        if (authResult.Permaban || authResult.MarkedForBan)
        {
            if (authResult.MarkedForBan)
            {
                Logger.LogWarning("Authenticate:MARKBAN:{id}:{primaryid}:{ident}", authResult.Uid, authResult.PrimaryUid, charaIdent);
                await EnsureBan(authResult.Uid!, authResult.PrimaryUid, charaIdent);
            }

            Logger.LogWarning("Authenticate:UIDBAN:{id}:{ident}", authResult.Uid, charaIdent);
            return Unauthorized("Your account is banned from using the service.");
        }

        var existingIdent = await _redis.StringGetAsync("UID:" + authResult.Uid);
        if (!string.IsNullOrEmpty(existingIdent))
        {
            Logger.LogWarning("Authenticate:DUPLICATE:{id}:{ident}", authResult.Uid, charaIdent);
            return Unauthorized("Already logged in to this account. Reconnect in 60 seconds. If you keep seeing this issue, restart your game.");
        }

        Logger.LogInformation("Authenticate:SUCCESS:{id}:{ident}", authResult.Uid, charaIdent);
        return await CreateJwtFromId(authResult.Uid!, charaIdent, authResult.Alias ?? string.Empty);
    }

    protected JwtSecurityToken CreateJwt(IEnumerable<Claim> authClaims)
    {
        var authSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(Configuration.GetValue<string>(nameof(LaciConfigurationBase.Jwt))))
        {
            KeyId = Configuration.GetValue<string>(nameof(LaciConfigurationBase.JwtKeyId)),
        };

        var token = new SecurityTokenDescriptor()
        {
            Subject = new ClaimsIdentity(authClaims),
            SigningCredentials = new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256Signature),
            Expires = new(long.Parse(authClaims.First(f => string.Equals(f.Type, LaciClaimTypes.Expires, StringComparison.Ordinal)).Value!, CultureInfo.InvariantCulture), DateTimeKind.Utc),
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.CreateJwtSecurityToken(token);
    }

    protected async Task<IActionResult> CreateJwtFromId(string uid, string charaIdent, string alias)
    {
        var token = CreateJwt(new List<Claim>()
        {
            new Claim(LaciClaimTypes.Uid, uid),
            new Claim(LaciClaimTypes.CharaIdent, charaIdent),
            new Claim(LaciClaimTypes.Alias, alias),
            new Claim(LaciClaimTypes.Expires, DateTime.UtcNow.AddHours(6).Ticks.ToString(CultureInfo.InvariantCulture)),
            new Claim(LaciClaimTypes.Continent, await _geoIpService.GetCountryFromIp(HttpContext))
        });

        return Content(token.RawData);
    }

    protected async Task EnsureBan(string uid, string? primaryUid, string charaIdent)
    {
        using var dbContext = await DbContextFactory.CreateDbContextAsync();
        if (!dbContext.BannedUsers.Any(c => c.CharacterIdentification == charaIdent))
        {
            dbContext.BannedUsers.Add(new Banned()
            {
                CharacterIdentification = charaIdent,
                Reason = "Autobanned CharacterIdent (" + uid + ")",
            });
        }

        var uidToLookFor = primaryUid ?? uid;

        var primaryUserAuth = await dbContext.Auth.FirstAsync(f => f.UserUID == uidToLookFor);
        primaryUserAuth.MarkForBan = false;
        primaryUserAuth.IsBanned = true;

        var lodestone = await dbContext.LodeStoneAuth.Include(a => a.User).FirstOrDefaultAsync(c => c.User.UID == uidToLookFor);

        if (lodestone != null)
        {
            if (!dbContext.BannedRegistrations.Any(c => c.DiscordIdOrLodestoneAuth == lodestone.HashedLodestoneId))
            {
                dbContext.BannedRegistrations.Add(new BannedRegistrations()
                {
                    DiscordIdOrLodestoneAuth = lodestone.HashedLodestoneId,
                });
            }
            if (!dbContext.BannedRegistrations.Any(c => c.DiscordIdOrLodestoneAuth == lodestone.DiscordId.ToString()))
            {
                dbContext.BannedRegistrations.Add(new BannedRegistrations()
                {
                    DiscordIdOrLodestoneAuth = lodestone.DiscordId.ToString(),
                });
            }
        }

        await dbContext.SaveChangesAsync();
    }

    protected async Task<bool> IsIdentBanned(LaciDbContext dbContext, string charaIdent)
    {
        return await dbContext.BannedUsers.AsNoTracking().AnyAsync(u => u.CharacterIdentification == charaIdent).ConfigureAwait(false);
    }
}
