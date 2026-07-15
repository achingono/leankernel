namespace LeanKernel.Channels.Teams;

public sealed class IncomingActivityDto
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Text { get; set; }
    public string? ServiceUrl { get; set; }
    public ActorDto? From { get; set; }
    public ConversationDto? Conversation { get; set; }
    public List<AttachmentDto>? Attachments { get; set; }
}

public sealed class ActorDto
{
    public string? Id { get; set; }
}

public sealed class ConversationDto
{
    public string? Id { get; set; }
}

public sealed class AttachmentDto
{
    public string? ContentUrl { get; set; }
}
