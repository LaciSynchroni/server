using Discord;
using Discord.Interactions;
using LaciSynchroni.Shared.Data;
using LaciSynchroni.Shared.Utils.Configuration;
using Microsoft.EntityFrameworkCore;

namespace LaciSynchroni.Services.Discord;

public partial class LaciWizardModule
{
    [ComponentInteraction("wizard-userinfo")]
    public async Task ComponentUserInfo()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentUserInfo), Context.Interaction.User.Id);

        var serverName = _servicesConfig.GetValueOrDefault(nameof(ServicesConfiguration.ServerName), "Laci Synchroni");

        var components = new ComponentBuilderV2();
        var container = new ContainerBuilder()
                    .WithTextDisplay($"# {serverName} Self-Service")
                    .WithTextDisplay("## User Info")
                    .WithTextDisplay("You can see information about your service account(s) here." + Environment.NewLine
                        + "Use the selection below to select a service account to see info for." + Environment.NewLine + Environment.NewLine
                        + "- 1️⃣ is your primary account/UID" + Environment.NewLine
                        + "- 2️⃣ are all your secondary accounts/UIDs" + Environment.NewLine
                        + "If you are using Vanity UIDs the original UID is displayed in the second line of the account selection.")
                    .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true);

        using var db = await GetDbContext().ConfigureAwait(false);

        var selectionActionRow = new ActionRowBuilder();
        await AddUserSelectionV2(db, selectionActionRow, "wizard-userinfo-select").ConfigureAwait(false);

        var actionRow = new ActionRowBuilder();
        AddHomeV2(actionRow);

        container.WithActionRow(selectionActionRow)
            .WithActionRow(actionRow);
        components.WithContainer(container);

        await ModifyInteractionV2(components).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-userinfo-select")]
    public async Task SelectionUserInfo(string uid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{uid}", nameof(SelectionUserInfo), Context.Interaction.User.Id, uid);

        var serverName = _servicesConfig.GetValueOrDefault(nameof(ServicesConfiguration.ServerName), "Laci Synchroni");
        using var db = await GetDbContext().ConfigureAwait(false);

        var components = new ComponentBuilderV2();
        var container = new ContainerBuilder()
                    .WithTextDisplay($"# {serverName} Self-Service")
                    .WithTextDisplay("## User Info")
                    .WithTextDisplay($"User Information for `{uid}`")
                    .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true);

        await HandleUserInfo(db, container, uid).ConfigureAwait(false);

        var selectionActionRow = new ActionRowBuilder();
        await AddUserSelectionV2(db, selectionActionRow, "wizard-userinfo-select").ConfigureAwait(false);

        container.WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true);

        var actionRow = new ActionRowBuilder();
        AddHomeV2(actionRow);

        container.WithActionRow(selectionActionRow)
            .WithActionRow(actionRow);
        components.WithContainer(container);

        await ModifyInteractionV2(components).ConfigureAwait(false);
    }

    private async Task HandleUserInfo(LaciDbContext db, ContainerBuilder container, string uid)
    {
        ulong userToCheckForDiscordId = Context.User.Id;

        var dbUser = await db.Users.SingleOrDefaultAsync(u => u.UID == uid).ConfigureAwait(false);

        var groups = await db.Groups.Where(g => g.OwnerUID == dbUser.UID).ToListAsync().ConfigureAwait(false);
        var groupsJoined = await db.GroupPairs.Where(g => g.GroupUserUID == dbUser.UID).ToListAsync().ConfigureAwait(false);
        var identity = await _connectionMultiplexer.GetDatabase().StringGetAsync("UID:" + dbUser.UID).ConfigureAwait(false);

        var infoText = "-# Last Online (UTC)" + Environment.NewLine
            + dbUser.LastLoggedIn.ToString("U", System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine
            + "-# Currently Online" + Environment.NewLine
            + !string.IsNullOrEmpty(identity) + Environment.NewLine
            + "-# Joined Syncshells" + Environment.NewLine
            + groupsJoined.Count + Environment.NewLine
            + "-# Owned Syncshells" + Environment.NewLine
            + groups.Count;

        if (!string.IsNullOrEmpty(dbUser.Alias))
        {
            infoText = "-# Vanity UID" + Environment.NewLine
                + $"`{dbUser.Alias}`" + Environment.NewLine
                + infoText;
        }



        foreach (var group in groups)
        {
            var syncShellUserCount = await db.GroupPairs.CountAsync(g => g.GroupGID == group.GID).ConfigureAwait(false);

            infoText += Environment.NewLine
                + $"-# Syncshell `{group.GID}`" + (!string.IsNullOrEmpty(group.Alias) ? $" (`{group.Alias}`)" : "") + Environment.NewLine
                + "**User Count:** " + syncShellUserCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        container.WithTextDisplay(infoText);
    }
}
