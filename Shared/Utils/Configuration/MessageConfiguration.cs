using LaciSynchroni.Common.Data.Enum;

namespace LaciSynchroni.Shared.Utils.Configuration;

public class MessageConfiguration
{

    public MessageWithSeverity[] PeriodicMessages { get; set; } = [];
    public TimeSpan? PeriodicMessageInterval { get; set; } = TimeSpan.Zero;

    public MessageWithSeverity MessageOfTheDay { get; set; } = new(MessageSeverity.Information,
        "Welcome to %ServerName% \"%ShardName%\", Current Online Users: %OnlineUsers%");
    
    public class MessageWithSeverity(MessageSeverity severity, string message)
    {
        public MessageSeverity Severity { get; set; } = severity;
        public string Message { get; set; } = message;
    }
}