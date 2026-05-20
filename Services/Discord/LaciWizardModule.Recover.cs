using Discord.Interactions;
using Discord;
using LaciSynchroni.Shared.Data;
using LaciSynchroni.Shared.Models;
using LaciSynchroni.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using LaciSynchroni.Shared.Utils.Configuration.Services;

namespace LaciSynchroni.Services.Discord;

public partial class LaciWizardModule
{
    [ComponentInteraction("wizard-recover")]
    public async Task ComponentRecover()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentRecover), Context.Interaction.User.Id);

        using var db = await GetDbContext().ConfigureAwait(false);
        
        var components = Wrap(CreateResponse(Color.Blue)
            .WithTextDisplay("## Recover")
            .WithTextDisplay(
                $"In case you have lost your secret key, you can recover it here." +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"### ⚠️ **Once you recover your key, the previously used key will be invalidated. If you use Laci on multiple devices, you will have to update the key everywhere you use it.** ⚠️" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"Use the selection below to select the user account you want to recover." +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"- 1️⃣ is your primary account/UID" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"- 2️⃣ are all your secondary accounts/UIDs" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"If you are using Vanity UIDs the original UID is displayed in the second line of the account selection." +
                $"{Environment.NewLine}" +
                $"### Note: instead of recovery and handling secret keys the switch to OAuth2 authentication is strongly suggested.")
            .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
            .WithActionRow([
                await MakeUserSelectionV2(db, "wizard-recover-select").ConfigureAwait(false),
            ])
            .WithActionRow([
                MakeHomeV2(),
            ])
        );

        await ModifyInteractionV2(components).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-recover-select")]
    public async Task SelectionRecovery(string uid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{uid}", nameof(SelectionRecovery), Context.Interaction.User.Id, uid);

        using var db = await GetDbContext().ConfigureAwait(false);
        
        string computedHash = string.Empty;
        Auth auth;
        var previousAuth = await db.Auth.Include(u => u.User).FirstOrDefaultAsync(u => u.UserUID == uid).ConfigureAwait(false);
        if (previousAuth != null)
        {
            db.Auth.Remove(previousAuth);
        }

        computedHash = StringUtils.Sha256String(StringUtils.GenerateRandomString(64) + DateTime.UtcNow);
        string hashedKey = StringUtils.Sha256String(computedHash);
        auth = new Auth()
        {
            HashedKey = hashedKey,
            User = previousAuth.User,
            PrimaryUserUID = previousAuth.PrimaryUserUID
        };

        await db.Auth.AddAsync(auth).ConfigureAwait(false);

        var components = Wrap(CreateResponse(Color.Blue)
            .WithTextDisplay("## Account Recovery")
            .WithTextDisplay(
                $"Recovery for **`{uid}`** complete." +
                $"{Environment.NewLine}" +
                $"This is your new private secret key. Do not share this private secret key with anyone. **If you lose it, it is irrevocably lost.**" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"**__NOTE: Secret keys are considered legacy authentication. If you are using the suggested OAuth2 authentication, you do not need to use the secret key or recover ever again.__**" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"||**`{computedHash}`**||" +
                $"{Environment.NewLine}" +
                $"__NOTE: The secret key only contains the letters ABCDEF and numbers 0 - 9.__" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"Enter this key in the Laci Synchroni Service Settings and reconnect to the service.")
            .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
            .WithActionRow([
                MakeHomeV2(),
            ])
        );
        
        await db.Auth.AddAsync(auth).ConfigureAwait(false);
        await db.SaveChangesAsync().ConfigureAwait(false);

        _botServices.Logger.LogInformation("User recovered: {userUID}:{hashedKey}", previousAuth.UserUID, hashedKey);
        await _botServices.LogToChannel(LogType.Recover, $"{Context.User.Mention} RECOVER SUCCESS: {previousAuth.UserUID}").ConfigureAwait(false);
        await ModifyInteractionV2(components).ConfigureAwait(false);
    }
}
