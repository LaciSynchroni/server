using Discord;
using Discord.Interactions;
using LaciSynchroni.Shared.Utils.Configuration.Services;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;

namespace LaciSynchroni.Services.Discord;

public partial class LaciWizardModule
{
    [ComponentInteraction("wizard-vanity")]
    public async Task ComponentVanity()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;
        using var db = await GetDbContext().ConfigureAwait(false);

        _logger.LogInformation("{method}:{userId}", nameof(ComponentVanity), Context.Interaction.User.Id);

        StringBuilder sb = new();
        var user = await Context.Guild.GetUserAsync(Context.User.Id).ConfigureAwait(false);
        bool userIsInVanityRole = _botServices.VanityRoles.Keys.Any(u => user.RoleIds.Contains(u.Id)) || !_botServices.VanityRoles.Any();
        if (!userIsInVanityRole)
        {
            sb.AppendLine("To be able to set Vanity IDs you must have one of the following roles:");
            foreach (var role in _botServices.VanityRoles)
            {
                sb.Append("- ").Append(role.Key.Mention).Append(" (").Append(role.Value).AppendLine(")");
            }
        }
        else
        {
            sb.AppendLine("Your current roles on this server allow you to set Vanity IDs.");
        }

        var container =
            CreateResponse(Color.Blue)
                .WithTextDisplay("## Vanity IDs")
                .WithTextDisplay("You are able to set your Vanity IDs here." +
                                 $"{Environment.NewLine}" +
                                 $"Vanity IDs are a way to customize your displayed UID or Syncshell ID to others.")
                .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
                .WithTextDisplay(sb.ToString())
                .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true);

        if (userIsInVanityRole)
        {
            container.WithActionRow([await MakeUserSelectionV2(db, "wizard-vanity-uid").ConfigureAwait(false)]);
            container.WithActionRow([await MakeGroupSelectionV2(db, "wizard-vanity-gid").ConfigureAwait(false)]);
        }

        container.WithActionRow([
            MakeHomeV2(),
        ]);
        
        await ModifyInteractionV2(Wrap(container)).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-vanity-uid")]
    public async Task SelectionVanityUid(string uid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{uid}", nameof(SelectionVanityUid), Context.Interaction.User.Id, uid);

        using var db = await GetDbContext().ConfigureAwait(false);
        var user = db.Users.Single(u => u.UID == uid);

        var components = Wrap(CreateResponse()
            .WithTextDisplay("## Vanity IDs")
            .WithTextDisplay($"You are setting a Vanity UID for **`{uid}`**." +
                             $"{Environment.NewLine}" +
                             $"The current Vanity UID is set to: **`{(user.Alias == null ? "No Vanity UID set" : user.Alias)}`**")
            .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
            .WithActionRow([
                new ButtonBuilder
                {
                    Label = "Cancel",
                    CustomId = "wizard-vanity",
                    Emote = new Emoji("❌"),
                    Style = ButtonStyle.Secondary,
                },
                new ButtonBuilder
                {
                    Label = "Set Vanity ID",
                    CustomId = $"wizard-vanity-uid-set:{uid}",
                    Emote = new Emoji("💅"),
                    Style = ButtonStyle.Primary,
                },
            ])
        );

        await ModifyInteractionV2(components).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-vanity-uid-set:*")]
    public async Task SelectionVanityUidSet(string uid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{uid}", nameof(SelectionVanityUidSet), Context.Interaction.User.Id, uid);

        await RespondWithModalAsync<VanityUidModal>("wizard-vanity-uid-modal:" + uid).ConfigureAwait(false);
    }

    [ModalInteraction("wizard-vanity-uid-modal:*")]
    public async Task ConfirmVanityUidModal(string uid, VanityUidModal modal)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{uid}:{vanity}", nameof(ConfirmVanityUidModal), Context.Interaction.User.Id, uid, modal.DesiredVanityUID);

        EmbedBuilder eb = new();
        ComponentBuilder cb = new();
        var desiredVanityUid = modal.DesiredVanityUID;
        using var db = await GetDbContext().ConfigureAwait(false);
        bool canAddVanityId = !db.Users.Any(u => u.UID == modal.DesiredVanityUID || u.Alias == modal.DesiredVanityUID);

        var container = CreateResponse()
            .WithTextDisplay("## Vanity IDs");

        Regex rgx = new(@"^[_\-a-zA-Z0-9]{5,15}$", RegexOptions.ECMAScript);
        if (!rgx.Match(desiredVanityUid).Success)
        {
            container
                .WithTextDisplay("### Invalid Vanity UID" +
                                 $"{Environment.NewLine}" +
                                 $"A Vanity UID must be between 5 and 15 characters long and only contain the letters A-Z, numbers 0-9, dashes (-) and underscores (_).")
                .WithActionRow([
                    new ButtonBuilder
                    {
                        Label = "Cancel",
                        CustomId = "wizard-vanity",
                        Emote = new Emoji("❌"),
                        Style = ButtonStyle.Secondary,
                    },
                    new ButtonBuilder
                    {
                        Label = "Pick Different UID",
                        CustomId = $"wizard-vanity-uid-set:{uid}",
                        Emote = new Emoji("💅"),
                        Style = ButtonStyle.Primary,
                    },
                ]);
        }
        else if (!canAddVanityId)
        {
            container
                .WithTextDisplay("### Vanity UID already taken" +
                                 $"{Environment.NewLine}" +
                                 $"The Vanity UID {desiredVanityUid} has already been claimed. Please pick a different one.")
                .WithActionRow([
                    new ButtonBuilder
                    {
                        Label = "Cancel",
                        CustomId = "wizard-vanity",
                        Emote = new Emoji("❌"),
                        Style = ButtonStyle.Secondary,
                    },
                    new ButtonBuilder
                    {
                        Label = "Pick Different UID",
                        CustomId = $"wizard-vanity-uid-set:{uid}",
                        Emote = new Emoji("💅"),
                        Style = ButtonStyle.Primary,
                    },
                ]);
        }
        else
        {
            var user = await db.Users.SingleAsync(u => u.UID == uid).ConfigureAwait(false);
            user.Alias = desiredVanityUid;
            db.Update(user);
            await db.SaveChangesAsync().ConfigureAwait(false);
            container
                .WithTextDisplay("### Vanity UID successfully set" +
                                 $"{Environment.NewLine}" +
                                 $"Your Vanity UID for **`{uid}`** was successfully changed to **`{desiredVanityUid}`**." +
                                 $"{Environment.NewLine}{Environment.NewLine}" +
                                 $"For changes to take effect, you need to reconnect to the Laci service.")
                .WithActionRow([
                    MakeHomeV2(),
                ]);
            await _botServices.LogToChannel(LogType.VanitySet, $"{Context.User.Mention} VANITY UID SET: UID: {user.UID}, Vanity: {desiredVanityUid}").ConfigureAwait(false);
        }

        await ModifyModalInteractionV2(Wrap(container)).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-vanity-gid")]
    public async Task SelectionVanityGid(string gid)
    {
        _logger.LogInformation("{method}:{userId}:{uid}", nameof(SelectionVanityGid), Context.Interaction.User.Id, gid);

        using var db = await GetDbContext().ConfigureAwait(false);
        var group = db.Groups.Single(u => u.GID == gid);
        
        var components = Wrap(CreateResponse()
            .WithTextDisplay("## Vanity IDs")
            .WithTextDisplay($"You are setting a Vanity GID for **`{gid}`**." +
                             $"{Environment.NewLine}" +
                             $"The current Vanity GID is set to: **`{(group.Alias == null ? "No Vanity UID set" : group.Alias)}`**")
            .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
            .WithActionRow([
                new ButtonBuilder
                {
                    Label = "Cancel",
                    CustomId = "wizard-vanity",
                    Emote = new Emoji("❌"),
                    Style = ButtonStyle.Secondary,
                },
                new ButtonBuilder
                {
                    Label = "Set Vanity ID",
                    CustomId = $"wizard-vanity-gid-set:{gid}",
                    Emote = new Emoji("💅"),
                    Style = ButtonStyle.Primary,
                },
            ])
        );

        await ModifyInteractionV2(components).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-vanity-gid-set:*")]
    public async Task SelectionVanityGidSet(string gid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{gid}", nameof(SelectionVanityGidSet), Context.Interaction.User.Id, gid);

        await RespondWithModalAsync<VanityGidModal>("wizard-vanity-gid-modal:" + gid).ConfigureAwait(false);
    }

    [ModalInteraction("wizard-vanity-gid-modal:*")]
    public async Task ConfirmVanityGidModal(string gid, VanityGidModal modal)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{gid}:{vanity}", nameof(ConfirmVanityGidModal), Context.Interaction.User.Id, gid, modal.DesiredVanityGID);

        EmbedBuilder eb = new();
        ComponentBuilder cb = new();
        var desiredVanityGid = modal.DesiredVanityGID;
        using var db = await GetDbContext().ConfigureAwait(false);
        bool canAddVanityId = !db.Groups.Any(u => u.GID == modal.DesiredVanityGID || u.Alias == modal.DesiredVanityGID);
        
        var container = CreateResponse()
            .WithTextDisplay("## Vanity IDs");

        Regex rgx = new(@"^[_\-a-zA-Z0-9]{5,20}$", RegexOptions.ECMAScript);
        if (!rgx.Match(desiredVanityGid).Success)
        {
            container
                .WithTextDisplay("### Invalid Vanity Syncshell ID" +
                                 $"{Environment.NewLine}" +
                                 $"A Vanity Syncshell ID must be between 5 and 15 characters long and only contain the letters A-Z, numbers 0-9, dashes (-) and underscores (_).")
                .WithActionRow([
                    new ButtonBuilder
                    {
                        Label = "Cancel",
                        CustomId = "wizard-vanity",
                        Emote = new Emoji("❌"),
                        Style = ButtonStyle.Secondary,
                    },
                    new ButtonBuilder
                    {
                        Label = "Pick Different UID",
                        CustomId = $"wizard-vanity-gid-set:{gid}",
                        Emote = new Emoji("💅"),
                        Style = ButtonStyle.Primary,
                    },
                ]);
        }
        else if (!canAddVanityId)
        {
            container
                .WithTextDisplay("### Vanity Syncshell ID already taken" +
                                 $"{Environment.NewLine}" +
                                 $"The Vanity Syncshell ID {desiredVanityGid} has already been claimed. Please pick a different one.")
                .WithActionRow([
                    new ButtonBuilder
                    {
                        Label = "Cancel",
                        CustomId = "wizard-vanity",
                        Emote = new Emoji("❌"),
                        Style = ButtonStyle.Secondary,
                    },
                    new ButtonBuilder
                    {
                        Label = "Pick Different UID",
                        CustomId = $"wizard-vanity-gid-set:{gid}",
                        Emote = new Emoji("💅"),
                        Style = ButtonStyle.Primary,
                    },
                ]);
        }
        else
        {
            var group = await db.Groups.SingleAsync(u => u.GID == gid).ConfigureAwait(false);
            group.Alias = desiredVanityGid;
            db.Update(group);
            await db.SaveChangesAsync().ConfigureAwait(false);
            container
                .WithTextDisplay("### Vanity Syncshell ID successfully set" +
                                 $"{Environment.NewLine}" +
                                 $"Your Vanity Syncshell ID for **`{gid}`** was successfully changed to **`{desiredVanityGid}`**." +
                                 $"{Environment.NewLine}{Environment.NewLine}" +
                                 $"For changes to take effect, you need to reconnect to the Laci service.")
                .WithActionRow([
                    MakeHomeV2(),
                ]);
            await _botServices.LogToChannel(LogType.VanitySet, $"{Context.User.Mention} VANITY GID SET: GID: {group.GID}, Vanity: {desiredVanityGid}").ConfigureAwait(false);
        }

        await ModifyModalInteractionV2(Wrap(container)).ConfigureAwait(false);
    }
}
