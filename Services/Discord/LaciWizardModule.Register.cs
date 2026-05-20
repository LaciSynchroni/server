using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using LaciSynchroni.Shared.Data;
using LaciSynchroni.Shared.Models;
using LaciSynchroni.Shared.Utils;
using LaciSynchroni.Shared.Utils.Configuration;
using LaciSynchroni.Common.Dto.Server;
using LaciSynchroni.Common.SignalR;
using MessagePack;
using System.Text.Json;
using LaciSynchroni.Common.Data;
using LaciSynchroni.Shared.Utils.Configuration.Services;

namespace LaciSynchroni.Services.Discord;

public partial class LaciWizardModule
{
    [ComponentInteraction("wizard-register")]
    public async Task ComponentRegister()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentRegister), Context.Interaction.User.Id);

        var serverName = _servicesConfig.GetValueOrDefault(nameof(ServicesConfiguration.ServerName), "Laci Synchroni");
        
        var components = Wrap(
            CreateResponse(Color.Blue)
                .WithTextDisplay("## Registration")
                .WithTextDisplay($"You are about to register a service account with the {serverName} server." +
                                 $"{Environment.NewLine}" +
                                 $"Please follow the bot instructions precisely to ensure that the registration goes smoothly.")
                .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
                .WithActionRow([
                    new ButtonBuilder
                    {
                        Label = "Start Registration",
                        // We swap this back to wizard-register-start once we re-add authentication
                        CustomId = "wizard-register-verify-check:OK",
                        Emote = new Emoji("🌒"),
                        Style = ButtonStyle.Primary,
                    },
                    MakeHomeV2(),
                ])
        );
        

        await ModifyInteractionV2(components).ConfigureAwait(false);
    }
    
    // TODO Redo this for next
    [ComponentInteraction("wizard-register-start")]
    public async Task ComponentRegisterStart()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentRegisterStart), Context.Interaction.User.Id);

        using var db = await GetDbContext().ConfigureAwait(false);
        var entry = await db.LodeStoneAuth.SingleOrDefaultAsync(u => u.DiscordId == Context.User.Id && u.StartedAt != null).ConfigureAwait(false);
        if (entry != null)
        {
            db.LodeStoneAuth.Remove(entry);
        }
        _botServices.DiscordLodestoneMapping.TryRemove(Context.User.Id, out _);
        _botServices.DiscordVerifiedUsers.TryRemove(Context.User.Id, out _);

        await db.SaveChangesAsync().ConfigureAwait(false);

        await RespondWithModalAsync<LodestoneModal>("wizard-register-lodestone-modal").ConfigureAwait(false);
    }

    // TODO Redo this for next
    [ModalInteraction("wizard-register-lodestone-modal")]
    public async Task ModalRegister(LodestoneModal lodestoneModal)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{lodestone}", nameof(ModalRegister), Context.Interaction.User.Id, lodestoneModal.LodestoneUrl);

        EmbedBuilder eb = new();
        eb.WithColor(Color.Purple);
        var success = await HandleRegisterModalAsync(eb, lodestoneModal).ConfigureAwait(false);
        ComponentBuilder cb = new();
        cb.WithButton("Cancel", "wizard-register", ButtonStyle.Secondary, emote: new Emoji("❌"));
        if (success.Item1) cb.WithButton("Verify", "wizard-register-verify:" + success.Item2, ButtonStyle.Primary, emote: new Emoji("✅"));
        else cb.WithButton("Try again", "wizard-register-start", ButtonStyle.Primary, emote: new Emoji("🔁"));
        await ModifyModalInteraction(eb, cb).ConfigureAwait(false);
    }

    // TODO Redo this for next
    [ComponentInteraction("wizard-register-verify:*")]
    public async Task ComponentRegisterVerify(string verificationCode)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{verificationcode}", nameof(ComponentRegisterVerify), Context.Interaction.User.Id, verificationCode);

        _botServices.VerificationQueue.Enqueue(new KeyValuePair<ulong, Func<DiscordBotServices, Task>>(Context.User.Id,
            (service) => HandleVerifyAsync(Context.User.Id, verificationCode, service)));
        EmbedBuilder eb = new();
        ComponentBuilder cb = new();
        eb.WithColor(Color.Purple);
        cb.WithButton("Cancel", "wizard-register", ButtonStyle.Secondary, emote: new Emoji("❌"));
        cb.WithButton("Check", "wizard-register-verify-check:" + verificationCode, ButtonStyle.Primary, emote: new Emoji("❓"));
        eb.WithTitle("Verification Pending");
        eb.WithDescription("Please wait until the bot verifies your registration." + Environment.NewLine
            + "Press \"Check\" to check if the verification has been already processed" + Environment.NewLine + Environment.NewLine
            + "__This will not advance automatically, you need to press \"Check\".__");
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
    }
    
    [ComponentInteraction("wizard-register-verify-check:*")]
    public async Task ComponentRegisterVerifyCheck(string verificationCode)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{uid}", nameof(ComponentRegisterVerifyCheck), Context.Interaction.User.Id, verificationCode);

        EmbedBuilder eb = new();
        ComponentBuilder cb = new();
        bool registerSuccess = false;

        var isRegistrationLocked = _servicesConfig.GetValueOrDefault(nameof(ServicesConfiguration.LockRegistrationToRole), false);
        var serverName = _servicesConfig.GetValueOrDefault(nameof(ServicesConfiguration.ServerName), "Laci Synchroni");

        ComponentBuilderV2 components;

        if (isRegistrationLocked)
        {
            var hasAccess = false;
            var registrationRole = _servicesConfig.GetValueOrDefault<ulong?>(nameof(ServicesConfiguration.DiscordRegistrationRole), null!);
            if (registrationRole == null)
            {
                components = Wrap(
                    CreateResponse(Color.Red)
                        .WithTextDisplay("## Invalid Service Configuration")
                        .WithTextDisplay("The service was set up with an invalid configuration. Role registration lock has been enabled, but no role has been specified.")
                        .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
                        .WithActionRow([
                            MakeHomeV2(),
                        ])
                    );

                await ModifyInteractionV2(components).ConfigureAwait(false);
                return;
            }

            var restUser = await Context.Guild.GetUserAsync(Context.Interaction.User.Id).ConfigureAwait(false);
            if (restUser != null)
            {
                hasAccess = restUser.RoleIds.Contains((ulong)registrationRole);
            }

            if (!hasAccess)
            {
                
                components = Wrap(
                    CreateResponse(Color.Red)
                        .WithTextDisplay("## Not AUthorized")
                        .WithTextDisplay($"You can not register without the <@&{registrationRole}> role.")
                        .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
                        .WithActionRow([
                            MakeHomeV2(),
                        ])
                );
                
                await ModifyInteractionV2(components).ConfigureAwait(false);
                return;
            }
        }

        eb.WithColor(Color.Green);
        using var db = await GetDbContext().ConfigureAwait(false);
        var (uid, key) = await HandleAddUser(db).ConfigureAwait(false);

        var publicServerUri = _serverConfig.GetValue<Uri>(nameof(ServerConfiguration.ServerPublicUri));
        
        components = Wrap(
            CreateResponse(Color.Green)
                .WithTextDisplay($"## Registration successful, your UID: {uid}")
                .WithTextDisplay($"Click this link to to quickly open up the Laci onboarding UI and connect to this service." +
                                 $"{Environment.NewLine}{Environment.NewLine}" +
                                 $"{PluginHttpServerData.Hostname}:{PluginHttpServerData.Port}/laci/join?uri={Uri.EscapeDataString(publicServerUri.ToString())}&secretKey={key}" +
                                 $"{Environment.NewLine}{Environment.NewLine}" +
                                 $"Already connected to the server? Use the secret key below. **If you lose it, you will have to recover your account through this bot.**" +
                                 $"{Environment.NewLine}{Environment.NewLine}" +
                                 $"||**`{key}`**||" +
                                 $"{Environment.NewLine}{Environment.NewLine}" +
                                 $"**__Using the suggested OAuth2 authentication in Laci, you do not need to use this Secret Key.__**" +
                                 $"{Environment.NewLine}{Environment.NewLine}" +
                                 $"If you want to continue using secret key authentication, enter this key in Laci Synchroni or click on the link above and hit save to connect to the service." +
                                 $"{Environment.NewLine}{Environment.NewLine}" +
                                 $"__NOTE: The Secret Key only contains letters ABCDEF and numbers 0 - 9.__" +
                                 $"{Environment.NewLine}{Environment.NewLine}" +
                                 $"**DO NOT SHARE ANY OF THIS INFO WITH ANYONE OR YOUR ACCOUNT MAY BE COMPROMISED.**" +
                                 $"{Environment.NewLine}" +
                                 $"Have fun.")
                .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
                .WithActionRow([
                    MakeHomeV2(),
                ])
        );
        
        registerSuccess = true;

        await ModifyInteractionV2(components).ConfigureAwait(false);
        await _botServices.AddRegisteredRoleAsync(Context.Interaction.User).ConfigureAwait(false);
    }

    // TODO Redo this for next
    private async Task<(bool, string)> HandleRegisterModalAsync(EmbedBuilder embed, LodestoneModal arg)
    {
        var lodestoneId = ParseCharacterIdFromLodestoneUrl(arg.LodestoneUrl);
        if (lodestoneId == null)
        {
            embed.WithTitle("Invalid Lodestone URL");
            embed.WithDescription("The lodestone URL was not valid. It should have following format:" + Environment.NewLine
                + "https://eu.finalfantasyxiv.com/lodestone/character/YOUR_LODESTONE_ID/");
            return (false, string.Empty);
        }

        // check if userid is already in db
        var hashedLodestoneId = StringUtils.Sha256String(lodestoneId.ToString());

        using var db = await GetDbContext().ConfigureAwait(false);

        // check if discord id or lodestone id is banned
        if (db.BannedRegistrations.Any(a => a.DiscordIdOrLodestoneAuth == hashedLodestoneId))
        {
            embed.WithDescription("This account is banned");
            return (false, string.Empty);
        }

        if (db.LodeStoneAuth.Any(a => a.HashedLodestoneId == hashedLodestoneId))
        {
            // character already in db
            embed.WithDescription("This lodestone character already exists in the Database. If you want to attach this character to your current Discord account use relink.");
            return (false, string.Empty);
        }

        string lodestoneAuth = await GenerateLodestoneAuth(Context.User.Id, hashedLodestoneId, db).ConfigureAwait(false);

        // check if lodestone id is already in db
        embed.WithTitle("Authorize your character");
        embed.WithDescription("Add following key to your character profile at https://na.finalfantasyxiv.com/lodestone/my/setting/profile/"
                              + Environment.NewLine
                              + "__NOTE: If the link does not lead you to your character edit profile page, you need to log in and set up your privacy settings!__"
                              + Environment.NewLine + Environment.NewLine
                              + $"**`{lodestoneAuth}`**"
                              + Environment.NewLine + Environment.NewLine
                              + $"**! THIS IS NOT THE KEY YOU HAVE TO ENTER IN LACI !**"
                              + Environment.NewLine + Environment.NewLine
                              + "Once added and saved, use the button below to Verify and finish registration and receive a secret key to use for Laci Synchroni."
                              + Environment.NewLine
                              + "__You can delete the entry from your profile after verification.__"
                              + Environment.NewLine + Environment.NewLine
                              + "The verification will expire in approximately 15 minutes. If you fail to verify the registration will be invalidated and you have to register again.");
        _botServices.DiscordLodestoneMapping[Context.User.Id] = lodestoneId.ToString();

        return (true, lodestoneAuth);
    }

    // TODO Redo this for next
    private async Task HandleVerifyAsync(ulong userid, string authString, DiscordBotServices services)
    {
        using var req = new HttpClient();

        services.DiscordVerifiedUsers.Remove(userid, out _);
        if (services.DiscordLodestoneMapping.ContainsKey(userid))
        {
            var randomServer = services.LodestoneServers[random.Next(services.LodestoneServers.Length)];
            var url = $"https://{randomServer}.finalfantasyxiv.com/lodestone/character/{services.DiscordLodestoneMapping[userid]}";
            using var response = await req.GetAsync(url).ConfigureAwait(false);
            _logger.LogInformation("Verifying {userid} with URL {url}", userid, url);
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (content.Contains(authString))
                {
                    services.DiscordVerifiedUsers[userid] = true;
                    _logger.LogInformation("Verified {userid} from lodestone {lodestone}", userid, services.DiscordLodestoneMapping[userid]);
                    await _botServices.LogToChannel(LogType.Register, $"<@{userid}> REGISTER VERIFY: Success.").ConfigureAwait(false);
                    services.DiscordLodestoneMapping.TryRemove(userid, out _);
                }
                else
                {
                    services.DiscordVerifiedUsers[userid] = false;
                    _logger.LogInformation("Could not verify {userid} from lodestone {lodestone}, did not find authString: {authString}, status code was: {code}",
                        userid, services.DiscordLodestoneMapping[userid], authString, response.StatusCode);
                    await _botServices.LogToChannel(LogType.Register, $"<@{userid}> REGISTER VERIFY: Failed: No Authstring ({authString}). (<{url}>)").ConfigureAwait(false);
                }
            }
            else
            {
                _logger.LogWarning("Could not verify {userid}, HttpStatusCode: {code}", userid, response.StatusCode);
                await _botServices.LogToChannel(LogType.Register, $"<@{userid}> REGISTER VERIFY: Failed: HttpStatusCode {response.StatusCode}. (<{url}>)").ConfigureAwait(false);
            }
        }
    }

    // TODO Redo this for next
    private async Task<(string, string)> HandleAddUser(LaciDbContext db)
    {
        var lodestoneAuth = db.LodeStoneAuth.SingleOrDefault(u => u.DiscordId == Context.User.Id);

        if (lodestoneAuth == null)
        {
            lodestoneAuth = new LodeStoneAuth()
            {
                DiscordId = Context.User.Id,
                HashedLodestoneId = StringUtils.Sha256String(Context.User.Id.ToString()),
                LodestoneAuthString = string.Empty
            };
            await db.LodeStoneAuth.AddAsync(lodestoneAuth).ConfigureAwait(false);
        }

        var user = new User();

        var uidLength = _servicesConfig.GetValueOrDefault(nameof(ServicesConfiguration.UidLength), 10);

        var hasValidUid = false;
        while (!hasValidUid)
        {
            var uid = StringUtils.GenerateRandomString(uidLength);
            if (db.Users.Any(u => u.UID == uid || u.Alias == uid)) continue;
            user.UID = uid;
            hasValidUid = true;
        }

        // make the first registered user on the service to admin
        if (!await db.Users.AnyAsync().ConfigureAwait(false))
        {
            user.IsAdmin = true;
        }

        user.LastLoggedIn = DateTime.UtcNow;

        var computedHash = StringUtils.Sha256String(StringUtils.GenerateRandomString(64) + DateTime.UtcNow.ToString());
        string hashedKey = StringUtils.Sha256String(computedHash);
        var auth = new Auth()
        {
            HashedKey = hashedKey,
            User = user,
        };

        await db.Users.AddAsync(user).ConfigureAwait(false);
        await db.Auth.AddAsync(auth).ConfigureAwait(false);

        lodestoneAuth.StartedAt = null;
        lodestoneAuth.User = user;
        lodestoneAuth.LodestoneAuthString = null;

        await db.SaveChangesAsync().ConfigureAwait(false);

        _botServices.Logger.LogInformation("User registered: {userUID}:{hashedKey}", user.UID, hashedKey);

        await _botServices.LogToChannel(LogType.Register, $"{Context.User.Mention} REGISTER COMPLETE: => {user.UID}").ConfigureAwait(false);

        _botServices.DiscordVerifiedUsers.Remove(Context.User.Id, out _);

        return (user.UID, computedHash);
    }
}
