using LaciSynchroni.Common.Data.Enum;

namespace LaciSynchroni.Shared.Models;

public class MessageWithSeverity(MessageSeverity severity, string message)
{
    public MessageSeverity Severity { get; set; } = severity;
    public string Message { get; set; } = message;
}