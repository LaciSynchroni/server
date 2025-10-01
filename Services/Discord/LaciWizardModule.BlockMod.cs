using Discord.Interactions;
using Discord;
using LaciSynchroni.Shared.Utils;
using LaciSynchroni.Shared.Utils.Configuration;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using LaciSynchroni.Shared.Models;
using LaciSynchroni.Shared.Utils.Configuration.Services;

namespace LaciSynchroni.Services.Discord;

public partial class LaciWizardModule
{
    [ComponentInteraction("wizard-blockmod")]
    public async Task ComponentBlockMod()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false)))
            return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentBlockMod), Context.Interaction.User.Id);

        using var db = await GetDbContext().ConfigureAwait(false);
        var user = await db.LodeStoneAuth.Include(u => u.User).SingleAsync(u => u.DiscordId == Context.User.Id).ConfigureAwait(false);
        var primaryUID = user.User.UID;

        EmbedBuilder eb = new();
        eb.WithColor(Color.Blue);
        eb.WithTitle("Block mod");
        eb.WithDescription("You can prevent specific mods from being synced using this dialog.");
        ComponentBuilder cb = new();
        AddHome(cb);
        cb.WithButton("Block mod", "wizard-blockmod-confirm:" + primaryUID, ButtonStyle.Primary, emote: new Emoji("🚫"));
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-blockmod-confirm:*")]
    public async Task ComponentBlockModConfirm(string uid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false)))
            return;

        _logger.LogInformation("{method}:{userId}:{uid}", nameof(ComponentBlockModConfirm), Context.Interaction.User.Id, uid);

        await RespondWithModalAsync<BlockModModal>("wizard-blockmod-modal:" + uid).ConfigureAwait(false);
    }

    [ModalInteraction("wizard-blockmod-modal:*")]
    public async Task ModalBlockModConfirm(string uid, BlockModModal modal)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false)))
            return;

        _logger.LogInformation("{method}:{userId}:{uid}", nameof(ModalBlockModConfirm), Context.Interaction.User.Id, uid);

        try
        {
            var modBlockInfo = new ForbiddenUploadEntry()
            {
                Hash = modal.ModHash,
                ForbiddenBy = modal.ForbiddenBy,
            };

            using var db = await GetDbContext().ConfigureAwait(false);
            await db.ForbiddenUploadEntries.AddAsync(modBlockInfo).ConfigureAwait(false);
            await db.SaveChangesAsync();

            EmbedBuilder eb = new();
            eb.WithTitle($"Mod {modal.ModHash} successfully banned");
            eb.WithColor(Color.Green);
            ComponentBuilder cb = new();
            AddHome(cb);

            await ModifyModalInteraction(eb, cb).ConfigureAwait(false);


            await _botServices.LogToChannel(LogType.ModeratorAction, $"{Context.User.Mention} MOD BLOCK SUCCESS: {modal.ModHash}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling modal delete account confirm");
        }
    }
}
