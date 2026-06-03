using Discord.Interactions;
using Discord;
using LaciSynchroni.Shared.Utils;
using LaciSynchroni.Shared.Utils.Configuration;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using LaciSynchroni.Shared.Utils.Configuration.Services;

namespace LaciSynchroni.Services.Discord;

public partial class LaciWizardModule
{
    [ComponentInteraction("wizard-delete")]
    public async Task ComponentDelete()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentDelete), Context.Interaction.User.Id);
        
        using var db = await GetDbContext().ConfigureAwait(false);
        
        var components = Wrap(
            CreateResponse(Color.Blue)
                .WithTextDisplay("## Delete Account")
                .WithTextDisplay("You can delete your primary or secondary UIDs here." +
                                 $"{Environment.NewLine}{Environment.NewLine}" +
                                 "__Note: deleting your primary UID will delete all associated secondary UIDs as well.__" +
                                 $"{Environment.NewLine}{Environment.NewLine}" +
                                 "- 1️⃣ is your primary account/UID" +
                                 $"{Environment.NewLine}" +
                                 "- 2️⃣ are all your secondary accounts/UIDs" +
                                 $"{Environment.NewLine}" +
                                 "If you are using Vanity UIDs the original UID is displayed in the second line of the account selection.")
                .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
                .WithActionRow([
                    await MakeUserSelectionV2(db, "wizard-delete-select").ConfigureAwait(false),
                ])
                .WithActionRow([
                    MakeHomeV2(),
                ])
        );

        await ModifyInteractionV2(components).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-delete-select")]
    public async Task SelectionDeleteAccount(string uid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{uid}", nameof(SelectionDeleteAccount), Context.Interaction.User.Id, uid);

        using var db = await GetDbContext().ConfigureAwait(false);
        bool isPrimary = db.Auth.Single(u => u.UserUID == uid).PrimaryUserUID == null;

        var components = Wrap(
            CreateResponse(Color.Purple)
                .WithTextDisplay("## Confirm Account Deletion")
                .WithTextDisplay($"You are about to delete **`{uid}`**." +
                                 $"{Environment.NewLine}" +
                                 $"This operation is irreversible. All your pairs, joined syncshells and information stored on the service for {uid} will be irrevocably deleted." +
                                 (isPrimary
                                     ? Environment.NewLine + Environment.NewLine +
                                       "⚠️ **You are about to delete a Primary UID, all attached Secondary UIDs and their information will be deleted as well.** ⚠️"
                                     : string.Empty))
                .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
                .WithActionRow([
                    new ButtonBuilder
                    {
                        Label = "Cancel",
                        CustomId = "wizard-delete",
                        Emote = new Emoji("❌"),
                        Style = ButtonStyle.Primary,
                    },
                    new ButtonBuilder
                    {
                        Label = "Delete",
                        CustomId = $"wizard-delete-confirm:{uid}",
                        Emote = new Emoji("🗑"),
                        Style = ButtonStyle.Danger,
                    },
                ])
        );

        await ModifyInteractionV2(components).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-delete-confirm:*")]
    public async Task ComponentDeleteAccountConfirm(string uid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{uid}", nameof(ComponentDeleteAccountConfirm), Context.Interaction.User.Id, uid);

        await RespondWithModalAsync<ConfirmDeletionModal>("wizard-delete-confirm-modal:" + uid).ConfigureAwait(false);
    }

    [ModalInteraction("wizard-delete-confirm-modal:*")]
    public async Task ModalDeleteAccountConfirm(string uid, ConfirmDeletionModal modal)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{uid}", nameof(ModalDeleteAccountConfirm), Context.Interaction.User.Id, uid);

        try
        {
            if (!string.Equals("DELETE", modal.Delete, StringComparison.Ordinal))
            {
                var components = Wrap(CreateResponse(Color.Red)
                    .WithTextDisplay("## Invalid Confirmation")
                    .WithTextDisplay(
                        $"You entered {modal.Delete} but requested was DELETE. Please try again and enter DELETE to confirm.")
                    .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
                    .WithActionRow([
                        new ButtonBuilder
                        {
                            Label = "Cancel",
                            CustomId = "wizard-delete",
                            Emote = new Emoji("❌"),
                            Style = ButtonStyle.Primary,
                        },
                        new ButtonBuilder
                        {
                            Label = "Retry",
                            CustomId = $"wizard-delete-confirm:{uid}",
                            Emote = new Emoji("🔁"),
                            Style = ButtonStyle.Danger,
                        },
                    ])
                );

                await ModifyModalInteractionV2(components).ConfigureAwait(false);
            }
            else
            {
                var maxGroupsByUser = _serverConfig.GetValueOrDefault(nameof(ServerConfiguration.MaxGroupUserCount), 3);

                using var db = await GetDbContext().ConfigureAwait(false);
                var user = await db.Users.SingleAsync(u => u.UID == uid).ConfigureAwait(false);
                var lodestone = await db.LodeStoneAuth.Include(u => u.User).SingleOrDefaultAsync(u => u.User.UID == uid).ConfigureAwait(false);
                await SharedDbFunctions.PurgeUser(_logger, user, db, maxGroupsByUser).ConfigureAwait(false);
                
                var components = Wrap(CreateResponse(Color.Green)
                    .WithTextDisplay("## Account Deleted")
                    .WithTextDisplay(
                        $"Your account **`{uid}`** has been deleted.")
                    .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
                    .WithActionRow([
                        MakeHomeV2(),
                    ])
                );

                await ModifyModalInteractionV2(components).ConfigureAwait(false);

                await _botServices.LogToChannel(LogType.Delete, $"{Context.User.Mention} DELETE SUCCESS: {uid}").ConfigureAwait(false);

                // only remove role if deleted uid has lodestone attached (== primary uid)
                if (lodestone != null)
                {
                    await _botServices.RemoveRegisteredRoleAsync(Context.Interaction.User).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling modal delete account confirm");
        }
    }
}
