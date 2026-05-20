using Discord;
using Discord.Interactions;
using LaciSynchroni.Shared.Data;
using LaciSynchroni.Shared.Models;
using LaciSynchroni.Shared.Utils;
using LaciSynchroni.Shared.Utils.Configuration;
using LaciSynchroni.Shared.Utils.Configuration.Services;
using Microsoft.EntityFrameworkCore;

namespace LaciSynchroni.Services.Discord;

public partial class LaciWizardModule
{
    [ComponentInteraction("wizard-secondary")]
    public async Task ComponentSecondary()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentSecondary), Context.Interaction.User.Id);

        using var db = await GetDbContext().ConfigureAwait(false);
        var user = await db.LodeStoneAuth.Include(u => u.User).SingleAsync(u => u.DiscordId == Context.User.Id).ConfigureAwait(false);
        var primaryUID = user.User.UID;
        var secondaryUids = await db.Auth.CountAsync(p => p.PrimaryUserUID == primaryUID).ConfigureAwait(false);

        var allowedUIDs = _servicesConfig.GetValueOrDefault(nameof(ServicesConfiguration.SecondaryUIDLimit), 5);
        
        var components = Wrap(CreateResponse(Color.Blue)
            .WithTextDisplay("## Secondary UID")
            .WithTextDisplay(
                $"You can create secondary UIDs here." +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"Secondary UIDs act as completely separate accounts with their own pair list, joined syncshells, UID and so on." +
                $"{Environment.NewLine}" +
                $"Use this to create UIDs if you want to use Laci on two separate game instances at once or keep your alts private." +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"__Note:__ Creating a Secondary UID is _not_ necessary to use Laci for alts." +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"You currently have {secondaryUids} Secondary UIDs out of a maximum of {allowedUIDs}.")
            .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
            .WithActionRow([
                new ButtonBuilder
                {
                    Label = "Create Secondary UID",
                    CustomId = $"wizard-secondary-create:{primaryUID}",
                    Emote = new Emoji("2️⃣"),
                    Style = ButtonStyle.Primary,
                    IsDisabled = secondaryUids >= allowedUIDs,
                },
                MakeHomeV2(),
            ])
        );

        await ModifyInteractionV2(components).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-secondary-create:*")]
    public async Task ComponentSecondaryCreate(string primaryUid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{primary}", nameof(ComponentSecondaryCreate), Context.Interaction.User.Id, primaryUid);

        using var db = await GetDbContext().ConfigureAwait(false);
        
        User newUser = new()
        {
            IsAdmin = false,
            IsModerator = false,
            LastLoggedIn = DateTime.UtcNow,
        };

        var hasValidUid = false;
        while (!hasValidUid)
        {
            var uid = StringUtils.GenerateRandomString(10);
            if (await db.Users.AnyAsync(u => u.UID == uid || u.Alias == uid).ConfigureAwait(false)) continue;
            newUser.UID = uid;
            hasValidUid = true;
        }

        var computedHash = StringUtils.Sha256String(StringUtils.GenerateRandomString(64) + DateTime.UtcNow.ToString());
        var auth = new Auth()
        {
            HashedKey = StringUtils.Sha256String(computedHash),
            User = newUser,
            PrimaryUserUID = primaryUid
        };

        await db.Users.AddAsync(newUser).ConfigureAwait(false);
        await db.Auth.AddAsync(auth).ConfigureAwait(false);

        await db.SaveChangesAsync().ConfigureAwait(false);
        
        var components = Wrap(CreateResponse(Color.Blue)
            .WithTextDisplay("## Secondary UID")
            .WithTextDisplay(
                $"A Secondary UID for you was created. Use the information below and add the secret key to the Laci settings in the Service Settings tab.")
            .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
            .WithTextDisplay($"-# UID" +
                             $"{Environment.NewLine}" +
                             $"**`{newUser.UID}`**" +
                             $"{Environment.NewLine}" +
                             $"-# Secret Key" +
                             $"{Environment.NewLine}" +
                             $"||**`{computedHash}`**||")
            .WithSeparator(spacing: SeparatorSpacingSize.Large, isDivider: true)
            .WithActionRow([
                MakeHomeV2(),
            ])
        );

        await ModifyInteractionV2(components).ConfigureAwait(false);
    }
}
