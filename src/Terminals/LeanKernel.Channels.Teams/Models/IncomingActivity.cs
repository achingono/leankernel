namespace LeanKernel.Channels.Teams.Models;

public sealed class IncomingActivity
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Text { get; set; }
    public string? ServiceUrl { get; set; }
    public Actor? From { get; set; }
    public Conversation? Conversation { get; set; }
    public List<Attachment>? Attachments { get; set; }
}
