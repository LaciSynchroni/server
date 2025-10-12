using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using LaciSynchroni.Shared.Data;
using LaciSynchroni.Shared.Models;
using LaciSynchroni.Shared.Services;
using LaciSynchroni.Shared.Utils;
using LaciSynchroni.Shared.Utils.Configuration;
using LaciSynchroni.Shared.Utils.Configuration.Services;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Text.RegularExpressions;

namespace LaciSynchroni.Services.Discord;

public partial class LaciWizardModule : InteractionModuleBase
{
    private ILogger<LaciModule> _logger;
    private DiscordBotServices _botServices;
    private IConfigurationService<ServerConfiguration> _serverConfig;
    private IConfigurationService<ServicesConfiguration> _servicesConfig;
    private IConnectionMultiplexer _connectionMultiplexer;
    private readonly IDbContextFactory<LaciDbContext> _dbContextFactory;
    private Random random = new();

    public LaciWizardModule(ILogger<LaciModule> logger, DiscordBotServices botServices,
        IConfigurationService<ServerConfiguration> serverConfig,
        IConfigurationService<ServicesConfiguration> servicesConfig,
        IConnectionMultiplexer connectionMultiplexer, IDbContextFactory<LaciDbContext> dbContextFactory)
    {
        _logger = logger;
        _botServices = botServices;
        _serverConfig = serverConfig;
        _servicesConfig = servicesConfig;
        _connectionMultiplexer = connectionMultiplexer;
        _dbContextFactory = dbContextFactory;
    }

    [ComponentInteraction("wizard-captcha:*")]
    public async Task WizardCaptcha(bool init = false)
    {
        if (!init && !(await ValidateInteraction().ConfigureAwait(false))) return;

        if (_botServices.VerifiedCaptchaUsers.Contains(Context.Interaction.User.Id))
        {
            await StartWizard(true).ConfigureAwait(false);
            return;
        }

        var serverName = _servicesConfig.GetValueOrDefault(nameof(ServicesConfiguration.ServerName), "Laci Synchroni");

        var rnd = new Random();
        var correctButton = rnd.Next(4) + 1;
        string nthButtonText = correctButton switch
        {
            1 => "first",
            2 => "second",
            3 => "third",
            4 => "fourth",
            _ => "unknown",
        };

        Emoji nthButtonEmoji = correctButton switch
        {
            1 => Emoji.Parse("🌅"),
            2 => Emoji.Parse("🏞️"),
            3 => Emoji.Parse("🌌"),
            4 => Emoji.Parse("🌆"),
            _ => "unknown",
        };

        int incorrectButtonHighlight;
        do
        {
            incorrectButtonHighlight = rnd.Next(4) + 1;
        }
        while (incorrectButtonHighlight == correctButton);

        // There are 4 button styles we can use, Primary, Secondary, Danger and Success.
        // We want to give each button a different style, randomized. To do this, we use rnd from above. 
        // The enums start at 1, so we use the range of 1-4. Button styles should NOT be repeated, but we also do not want Link or Premium styles.
        var buttonStyles = Enum.GetValues(typeof(ButtonStyle)).Cast<ButtonStyle>().Where(bs => bs != ButtonStyle.Link && bs != ButtonStyle.Premium).OrderBy(x => rnd.Next()).Take(4).ToArray();

        var components = new ComponentBuilderV2()
        {
            Components = [
                new ContainerBuilder()
                    .WithAccentColor(Color.LightOrange)
                    .WithTextDisplay($"# {serverName} Self-Service")
                    .WithTextDisplay("## Captcha")
                    .WithTextDisplay("As this is your first time using the self-service since it has been restarted, you have to solve the captcha problem below to verify you are not a bot.")
                    .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
                    .WithTextDisplay($"-# To solve the captcha, __press on the **{nthButtonText} button** ({nthButtonEmoji}).__")
                    .WithSeparator(spacing: SeparatorSpacingSize.Small, isDivider: false)
                    .WithActionRow([
                        new ButtonBuilder()
                        {
                            Label = "Limsa Lominsa",
                            CustomId = correctButton == 1 ? "wizard-home:false" : "wizard-captcha-fail:1",
                            Emote = Emoji.Parse("🌅"),
                            Style = buttonStyles[0],
                        },
                        new ButtonBuilder()
                        {
                            Label = "New Gridania",
                            CustomId = correctButton == 2 ? "wizard-home:false" : "wizard-captcha-fail:2",
                            Emote = Emoji.Parse("🏞️"),
                            Style = buttonStyles[1],
                        },
                        new ButtonBuilder()
                        {
                            Label = "Old Gridania",
                            CustomId = correctButton == 3 ? "wizard-home:false" : "wizard-captcha-fail:3",
                            Emote = Emoji.Parse("🌌"),
                            Style = buttonStyles[2],
                        },
                        new ButtonBuilder()
                        {
                            Label = "Ul'dah",
                            CustomId = correctButton == 4 ? "wizard-home:false" : "wizard-captcha-fail:4",
                            Emote = Emoji.Parse("🌆"),
                            Style = buttonStyles[3],
                        },
                    ]),
            ],
        };

        await InitOrUpdateInteractionV2(init, components).ConfigureAwait(false);
    }

    private async Task InitOrUpdateInteraction(bool init, EmbedBuilder eb, ComponentBuilder cb)
    {
        _logger.LogInformation("Init: {init}", init);
        if (init)
        {
            await RespondAsync(embed: eb.Build(), components: cb.Build(), ephemeral: true).ConfigureAwait(false);
            var resp = await GetOriginalResponseAsync().ConfigureAwait(false);
            _botServices.ValidInteractions[Context.User.Id] = resp.Id;
            _logger.LogInformation("Init Msg: {id}", resp.Id);
        }
        else
        {
            await ModifyInteraction(eb, cb).ConfigureAwait(false);
        }
    }

    private async Task InitOrUpdateInteractionV2(bool init, ComponentBuilderV2 components)
    {
        if (init)
        {
            await RespondAsync(components: components.Build(), ephemeral: true).ConfigureAwait(false);
            var resp = await GetOriginalResponseAsync().ConfigureAwait(false);
            _botServices.ValidInteractions[Context.User.Id] = resp.Id;
        }
        else
        {
            await ModifyInteractionV2(components).ConfigureAwait(false);
        }
    }

    [ComponentInteraction("wizard-captcha-fail:*")]
    public async Task WizardCaptchaFail(int button)
    {
        var serverName = _servicesConfig.GetValueOrDefault(nameof(ServicesConfiguration.ServerName), "Laci Synchroni");

        var components = new ComponentBuilderV2()
            .WithContainer(
                new ContainerBuilder()
                    .WithAccentColor(Color.LightOrange)
                    .WithTextDisplay($"# {serverName} Self-Service")
                    .WithTextDisplay("## Captcha")
                    .WithTextDisplay("You failed the captcha. Please retry again.")
                    .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
                    .WithActionRow([
                        new ButtonBuilder()
                            .WithLabel("Retry")
                            .WithStyle(ButtonStyle.Primary)
                            .WithCustomId("wizard-captcha:false")
                            .WithEmote(Emoji.Parse("↩️")),
                    ])
            );



        await InitOrUpdateInteractionV2(init: false, components).ConfigureAwait(false);

        await _botServices.LogToChannel(LogType.CaptchaFailed, $"{Context.User.Mention} FAILED CAPTCHA").ConfigureAwait(false);
    }


    [ComponentInteraction("wizard-home:*")]
    public async Task StartWizard(bool init = false)
    {
        if (!init && !(await ValidateInteraction().ConfigureAwait(false))) return;

        if (!_botServices.VerifiedCaptchaUsers.Contains(Context.Interaction.User.Id))
            _botServices.VerifiedCaptchaUsers.Add(Context.Interaction.User.Id);

        _logger.LogInformation("{method}:{userId}", nameof(StartWizard), Context.Interaction.User.Id);

        using var db = await GetDbContext().ConfigureAwait(false);
        var account = await db.LodeStoneAuth.Include(u => u.User).FirstOrDefaultAsync(u => u.DiscordId == Context.User.Id && u.StartedAt == null).ConfigureAwait(false);
        bool hasAccount = account != null;

        var serverName = _servicesConfig.GetValueOrDefault(nameof(ServicesConfiguration.ServerName), "Laci Synchroni");

        if (init)
        {
            bool isBanned = await db.BannedRegistrations.AnyAsync(u => u.DiscordIdOrLodestoneAuth == Context.User.Id.ToString()).ConfigureAwait(false);

            if (isBanned)
            {
                var bannedComponents = new ComponentBuilderV2()
                    .WithContainer(
                        new ContainerBuilder()
                            .WithAccentColor(Color.Red)
                            .WithTextDisplay($"# {serverName} Self-Service")
                            .WithTextDisplay("## Account Banned")
                            .WithTextDisplay("Your access to this service has been revoked.")
                    );

                await RespondAsync(components: bannedComponents.Build(), ephemeral: true).ConfigureAwait(false);
                return;
            }
        }

        var components = new ComponentBuilderV2();
        var container = new ContainerBuilder()
            .WithTextDisplay($"# {serverName} Self-Service")
            .WithTextDisplay("You are permitted to perform these actions:")
            .WithSeparator(spacing: SeparatorSpacingSize.Small, isDivider: false);

        var USE_SELECT_MENUS = true;

        var permittedActions = "";

        List<ActionRowBuilder> actionRows = new();

        var actionRow = new ActionRowBuilder();
        var selectMenu = new SelectMenuBuilder("wizard:menu-picker", placeholder: "Select an action");
        actionRow.AddComponent(selectMenu);

        void AddAction(string title, string wizardId, string description, IEmote emote, ButtonStyle buttonStyle = ButtonStyle.Secondary)
        {
            if (USE_SELECT_MENUS)
            {
                permittedActions = "-# Not Empty";
                selectMenu.AddOption(title, wizardId, description, emote);
            }
            else
            {
                permittedActions += Environment.NewLine
                + $"### {emote} {title}"
                + Environment.NewLine
                + description;

                if (actionRows.Count > 0 && actionRows[^1] != null && actionRows[^1].ComponentCount() < 5)
                {
                    _logger.LogInformation("Using existing action row");
                    actionRows[^1].AddComponent(new ButtonBuilder(title, wizardId, buttonStyle, emote: emote));
                }
                else
                {
                    _logger.LogInformation("Created new action row");
                    var newActionRow = new ActionRowBuilder();
                    newActionRow.AddComponent(new ButtonBuilder(title, wizardId, buttonStyle, emote: emote));
                    actionRows.Add(newActionRow);
                }
            }
        }

        if (!hasAccount)
        {

            AddAction("Register", "wizard-register-verify-check:OK", "Register a new service account", Emoji.Parse("🌒"));
        }
        else
        {
            AddAction("User Info", "wizard-userinfo", "Check your service account status", Emoji.Parse("ℹ️"));
            AddAction("Recover", "wizard-recover", "Recover your secret key", Emoji.Parse("🏥"));
            AddAction("Secondary UID", "wizard-secondary", "Create a secondary UID", Emoji.Parse("2️⃣"));
            AddAction("Vanity ID", "wizard-vanity", "Set a Vanity UID", Emoji.Parse("💅"));
            if (account.User.IsAdmin)
            {
                AddAction("Block Mod", "wizard-blockmod", "Add a mod to be blocked from being shared", Emoji.Parse("🚫"));
            }
            AddAction("Delete", "wizard-delete", "Delete your secondary UIDs or service account", Emoji.Parse("⚠️"), ButtonStyle.Danger);
        }


        if (USE_SELECT_MENUS == false)
        {
            container.WithTextDisplay(permittedActions);

            container.WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true);

            for (var actionRowIndex = 0; actionRowIndex < actionRows.Count; actionRowIndex++)
            {
                container.WithActionRow(actionRows[actionRowIndex]);
            }
        }
        else
        {
            container.WithActionRow(actionRow);
        }

        components.WithContainer(container);

        await InitOrUpdateInteractionV2(init, components).ConfigureAwait(false);
        return;

        EmbedBuilder eb = new();
        eb.WithTitle($"Welcome to the {serverName} Service Bot for this server");
        eb.WithDescription("Here is what you can do:" + Environment.NewLine + Environment.NewLine
            + (!hasAccount ? string.Empty : ("- Check your account status press \"ℹ️ User Info\"" + Environment.NewLine))
            + (hasAccount ? string.Empty : ($"- Register a new {serverName} Account press \"🌒 Register\"" + Environment.NewLine))
            + (!hasAccount ? string.Empty : ("- You lost your secret key press \"🏥 Recover\"" + Environment.NewLine))
            + (!hasAccount ? string.Empty : ("- Create a secondary UIDs press \"2️⃣ Secondary UID\"" + Environment.NewLine))
            + (!hasAccount ? string.Empty : ("- Set a Vanity UID press \"💅 Vanity IDs\"" + Environment.NewLine))
            + (!hasAccount || (!account?.User?.IsAdmin ?? false) ? string.Empty : ("- Add a mod to the forbidden transfers list press \"🚫 Block mod\"" + Environment.NewLine))
            + (!hasAccount ? string.Empty : ("- Delete your primary or secondary accounts with \"⚠️ Delete\""))
            );
        eb.WithColor(Color.Blue);
        ComponentBuilder cb = new();
        if (!hasAccount)
        {
            cb.WithButton("Register", "wizard-register-verify-check:OK", ButtonStyle.Primary, new Emoji("🌒"));
            // cb.WithButton("Relink", "wizard-relink", ButtonStyle.Secondary, new Emoji("🔗"));
        }
        else
        {
            cb.WithButton("User Info", "wizard-userinfo", ButtonStyle.Secondary, new Emoji("ℹ️"));
            cb.WithButton("Recover", "wizard-recover", ButtonStyle.Secondary, new Emoji("🏥"));
            cb.WithButton("Secondary UID", "wizard-secondary", ButtonStyle.Secondary, new Emoji("2️⃣"));
            cb.WithButton("Vanity IDs", "wizard-vanity", ButtonStyle.Secondary, new Emoji("💅"));
            if (account?.User?.IsAdmin ?? false)
            {
                cb.WithButton("Block mod", "wizard-blockmod", ButtonStyle.Secondary, new Emoji("🚫"));
            }
            cb.WithButton("Delete", "wizard-delete", ButtonStyle.Danger, new Emoji("⚠️"), row: 1);
        }

        await InitOrUpdateInteraction(init, eb, cb).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard:menu-picker")]
    public async Task MenuPicker(string wizardId)
    {
        switch (wizardId)
        {
            case "wizard-register-verify-check:OK":
                await ComponentRegister().ConfigureAwait(false);
                break;
            case "wizard-userinfo":
                await ComponentUserInfo().ConfigureAwait(false);
                break;
            case "wizard-recover":
                await ComponentRecover().ConfigureAwait(false);
                break;
            case "wizard-secondary":
                await ComponentSecondary().ConfigureAwait(false);
                break;
            case "wizard-vanity":
                await ComponentVanity().ConfigureAwait(false);
                break;
            case "wizard-blockmod":
                await ComponentBlockMod().ConfigureAwait(false);
                break;
            case "wizard-delete":
                await ComponentDelete().ConfigureAwait(false);
                break;
            default:
                // TODO Show unexpected wizard module
                break;
        }
    }
    public class VanityUidModal : IModal
    {
        public string Title => "Set Vanity UID";

        [InputLabel("Set your Vanity UID")]
        [ModalTextInput("vanity_uid", TextInputStyle.Short, "5-15 characters, underscore, dash", 5, 15)]
        public string DesiredVanityUID { get; set; }
    }

    public class VanityGidModal : IModal
    {
        public string Title => "Set Vanity Syncshell ID";

        [InputLabel("Set your Vanity Syncshell ID")]
        [ModalTextInput("vanity_gid", TextInputStyle.Short, "5-20 characters, underscore, dash", 5, 20)]
        public string DesiredVanityGID { get; set; }
    }

    public class ConfirmDeletionModal : IModal
    {
        public string Title => "Confirm Account Deletion";

        [InputLabel("Enter \"DELETE\" in all Caps")]
        [ModalTextInput("confirmation", TextInputStyle.Short, "Enter DELETE")]
        public string Delete { get; set; }
    }

    public class BlockModModal : IModal
    {
        public string Title => "Block Mod";

        [InputLabel("Mod Hash")]
        [ModalTextInput("mod_hash", TextInputStyle.Short, "40 characters, hex", 40, 40)]
        public string ModHash
        {
            get; set;
        }

        [InputLabel("Forbidden because")]
        [ModalTextInput("forbidden_by", TextInputStyle.Short, "1 to 100 characters", 1, 100)]
        public string ForbiddenBy
        {
            get; set;
        }
    }

    private async Task<LaciDbContext> GetDbContext()
    {
        return await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
    }

    private async Task<bool> ValidateInteraction()
    {
        if (Context.Interaction is not IComponentInteraction componentInteraction) return true;

        if (_botServices.ValidInteractions.TryGetValue(Context.User.Id, out ulong interactionId) && interactionId == componentInteraction.Message.Id)
        {
            return true;
        }

        var serverName = _servicesConfig.GetValueOrDefault(nameof(ServicesConfiguration.ServerName), "Laci Synchroni");

        var components = new ComponentBuilderV2()
            .WithContainer(
                new ContainerBuilder()
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay($"# {serverName} Self-Service")
                    .WithTextDisplay("## Session expired")
                    .WithTextDisplay("This session has expired since you have either again pressed **➡️ Start** on the initial message or the self-service has been restarted."
                        + Environment.NewLine
                        + Environment.NewLine
                        + "Please use the newly started self-service or start a new one.")
            );

        await InitOrUpdateInteractionV2(init: false, components).ConfigureAwait(false);

        return false;
    }

    private void AddHome(ComponentBuilder cb)
    {
        cb.WithButton("Return to Home", "wizard-home:false", ButtonStyle.Secondary, new Emoji("🏠"));
    }

    private void AddHomeV2(ActionRowBuilder actionRow)
    {
        actionRow.AddComponent(new ButtonBuilder("Return to Home", "wizard-home:false", ButtonStyle.Secondary, emote: Emoji.Parse("🏠")));
    }

    private async Task ModifyModalInteraction(EmbedBuilder eb, ComponentBuilder cb)
    {
        await (Context.Interaction as SocketModal).UpdateAsync(m =>
        {
            m.Embed = eb.Build();
            m.Components = cb.Build();
        }).ConfigureAwait(false);
    }

    private async Task ModifyInteraction(EmbedBuilder eb, ComponentBuilder cb)
    {
        await ((Context.Interaction) as IComponentInteraction).UpdateAsync(m =>
        {
            m.Content = null;
            m.Embed = eb.Build();
            m.Components = cb.Build();
        }).ConfigureAwait(false);
    }

    private async Task ModifyInteractionV2(ComponentBuilderV2 components)
    {
        await ((Context.Interaction) as IComponentInteraction).UpdateAsync(m =>
        {
            m.Content = null;
            m.Embed = null;
            m.Components = components.Build();
        }).ConfigureAwait(false);
    }

    private async Task AddUserSelection(LaciDbContext db, ComponentBuilder cb, string customId)
    {
        var discordId = Context.User.Id;
        var existingAuth = await db.LodeStoneAuth.Include(u => u.User).SingleOrDefaultAsync(e => e.DiscordId == discordId).ConfigureAwait(false);
        if (existingAuth != null)
        {
            SelectMenuBuilder sb = new();
            sb.WithPlaceholder("Select a UID");
            sb.WithCustomId(customId);
            var existingUids = await db.Auth.Include(u => u.User).Where(u => u.UserUID == existingAuth.User.UID || u.PrimaryUserUID == existingAuth.User.UID)
                .OrderByDescending(u => u.PrimaryUser == null).ToListAsync().ConfigureAwait(false);
            foreach (var entry in existingUids)
            {
                sb.AddOption(string.IsNullOrEmpty(entry.User.Alias) ? entry.UserUID : entry.User.Alias,
                    entry.UserUID,
                    !string.IsNullOrEmpty(entry.User.Alias) ? entry.User.UID : null,
                    entry.PrimaryUserUID == null ? new Emoji("1️⃣") : new Emoji("2️⃣"));
            }
            cb.WithSelectMenu(sb);
        }
    }

    private async Task AddUserSelectionV2(LaciDbContext db, ActionRowBuilder actionRow, string customId)
    {
        var discordId = Context.User.Id;
        var existingAuth = await db.LodeStoneAuth.Include(u => u.User).SingleOrDefaultAsync(e => e.DiscordId == discordId).ConfigureAwait(false);
        if (existingAuth != null)
        {
            SelectMenuBuilder sb = new();
            sb.WithPlaceholder("Select a UID");
            sb.WithCustomId(customId);
            var existingUids = await db.Auth.Include(u => u.User).Where(u => u.UserUID == existingAuth.User.UID || u.PrimaryUserUID == existingAuth.User.UID)
                .OrderByDescending(u => u.PrimaryUser == null).ToListAsync().ConfigureAwait(false);
            foreach (var entry in existingUids)
            {
                sb.AddOption(string.IsNullOrEmpty(entry.User.Alias) ? entry.UserUID : entry.User.Alias,
                    entry.UserUID,
                    !string.IsNullOrEmpty(entry.User.Alias) ? entry.User.UID : null,
                    entry.PrimaryUserUID == null ? new Emoji("1️⃣") : new Emoji("2️⃣"));
            }
            actionRow.WithSelectMenu(sb);
        }
    }

    private async Task AddGroupSelection(LaciDbContext db, ComponentBuilder cb, string customId)
    {
        var primary = (await db.LodeStoneAuth.Include(u => u.User).SingleAsync(u => u.DiscordId == Context.User.Id).ConfigureAwait(false)).User;
        var secondary = await db.Auth.Include(u => u.User).Where(u => u.PrimaryUserUID == primary.UID).Select(u => u.User).ToListAsync().ConfigureAwait(false);
        var primaryGids = (await db.Groups.Include(u => u.Owner).Where(u => u.OwnerUID == primary.UID).ToListAsync().ConfigureAwait(false));
        var secondaryGids = (await db.Groups.Include(u => u.Owner).Where(u => secondary.Select(u => u.UID).Contains(u.OwnerUID)).ToListAsync().ConfigureAwait(false));
        SelectMenuBuilder gids = new();
        if (primaryGids.Any() || secondaryGids.Any())
        {
            foreach (var item in primaryGids)
            {
                gids.AddOption(item.Alias ?? item.GID, item.GID, (item.Alias == null ? string.Empty : item.GID) + $" ({item.Owner.Alias ?? item.Owner.UID})", new Emoji("1️⃣"));
            }
            foreach (var item in secondaryGids)
            {
                gids.AddOption(item.Alias ?? item.GID, item.GID, (item.Alias == null ? string.Empty : item.GID) + $" ({item.Owner.Alias ?? item.Owner.UID})", new Emoji("2️⃣"));
            }
            gids.WithCustomId(customId);
            gids.WithPlaceholder("Select a Syncshell");
            cb.WithSelectMenu(gids);
        }
    }

    private async Task<string> GenerateLodestoneAuth(ulong discordid, string hashedLodestoneId, LaciDbContext dbContext)
    {
        var auth = StringUtils.GenerateRandomString(12, "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz");
        LodeStoneAuth lsAuth = new LodeStoneAuth()
        {
            DiscordId = discordid,
            HashedLodestoneId = hashedLodestoneId,
            LodestoneAuthString = auth,
            StartedAt = DateTime.UtcNow
        };

        dbContext.Add(lsAuth);
        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        return (auth);
    }

    private int? ParseCharacterIdFromLodestoneUrl(string lodestoneUrl)
    {
        var regex = new Regex(@"https:\/\/(na|eu|de|fr|jp)\.finalfantasyxiv\.com\/lodestone\/character\/\d+");
        var matches = regex.Match(lodestoneUrl);
        var isLodestoneUrl = matches.Success;
        if (!isLodestoneUrl || matches.Groups.Count < 1) return null;

        lodestoneUrl = matches.Groups[0].ToString();
        var stringId = lodestoneUrl.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        if (!int.TryParse(stringId, out int lodestoneId))
        {
            return null;
        }

        return lodestoneId;
    }
}
