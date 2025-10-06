using LaciSynchroni.Common.Data.Enum;
using LaciSynchroni.Shared.Models;

namespace LaciSynchroni.Shared.Utils.Configuration;

public class MessageConfiguration
{

    public MessageWithSeverity[] PeriodicMessages { get; set; } = [];
    public TimeSpan? PeriodicMessageInterval { get; set; } = TimeSpan.Zero;
    public MessageWithSeverity MessageOfTheDay { get; set; } = new(MessageSeverity.Information,
        "Welcome to %ServerName% \"%ShardName%\", Current Online Users: %OnlineUsers%");

    public static readonly MessageConfiguration DefaultMessageConfig = new ();
}