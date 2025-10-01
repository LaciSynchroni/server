using System.Text;
using LaciSynchroni.Shared.Utils.Configuration.Services;

namespace LaciSynchroni.Shared.Utils.Configuration;

public class ServicesConfiguration : LaciConfigurationBase
{
    public string DiscordBotToken { get; set; } = string.Empty;
    public ulong? DiscordChannelForMessages { get; set; } = null;
    public ulong? DiscordChannelForCommands { get; set; } = null;
    public ulong? DiscordChannelForBotLog { get; set; } = null!;
    public ulong? DiscordRoleRegistered { get; set; } = null!;
    public bool KickNonRegisteredUsers { get; set; } = false;
    public Dictionary<ulong, string> VanityRoles { get; set; } = new Dictionary<ulong, string>();
    public int UidLength { get; set; } = 10;
    public bool LockRegistrationToRole { get; set; } = false;
    public ulong? DiscordRegistrationRole { get; set; } = null!;
    public int SecondaryUIDLimit { get; set; } = 5;
    [RemoteConfiguration]
    public string ServerName { get; set; } = "Laci Synchroni";
    public LogType[] AllowedDiscordLogs { get; set; } = [
        LogType.Startup,
        LogType.VanityCleanup,
        LogType.UserProcessing,
        LogType.RegistrationRole,
        LogType.ModeratorAction,
        LogType.Register,
        LogType.Delete,
        LogType.SecondaryAdd,
        LogType.VanitySet,
        LogType.Recover,
        LogType.Relink,
        LogType.CaptchaFailed,
    ];
    public bool RunVanityCleanup { get; set; } = true;

    public override string ToString()
    {
        StringBuilder sb = new();
        sb.AppendLine(base.ToString());
        sb.AppendLine($"{nameof(DiscordBotToken)} => {DiscordBotToken}");
        sb.AppendLine($"{nameof(DiscordChannelForMessages)} => {DiscordChannelForMessages}");
        sb.AppendLine($"{nameof(DiscordChannelForCommands)} => {DiscordChannelForCommands}");
        sb.AppendLine($"{nameof(DiscordRoleRegistered)} => {DiscordRoleRegistered}");
        sb.AppendLine($"{nameof(KickNonRegisteredUsers)} => {KickNonRegisteredUsers}");
        sb.AppendLine($"{nameof(UidLength)} => {UidLength}");
        sb.AppendLine($"{nameof(LockRegistrationToRole)} => {LockRegistrationToRole}");
        sb.AppendLine($"{nameof(DiscordRegistrationRole)} => {DiscordRegistrationRole}");
        foreach (var role in VanityRoles)
        {
            sb.AppendLine($"{nameof(VanityRoles)} => {role.Key} = {role.Value}");
        }
        sb.AppendLine($"{nameof(SecondaryUIDLimit)} => {SecondaryUIDLimit}");
        sb.AppendLine($"{nameof(ServerName)} => {ServerName}");
        sb.AppendLine($"{nameof(AllowedDiscordLogs)} => {AllowedDiscordLogs}");
        sb.AppendLine($"{nameof(RunVanityCleanup)} => {RunVanityCleanup}");
        return sb.ToString();
    }
}