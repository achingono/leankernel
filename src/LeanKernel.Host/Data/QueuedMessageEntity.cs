namespace LeanKernel.Host.Data;

/// <summary>
/// Entity for persisting queued messages to database.
/// </summary>
public class QueuedMessageEntity
{
    public string Id { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime EnqueuedAt { get; set; }
    public DateTime? ScheduledFor { get; set; }
    public bool IsUrgent { get; set; }
    public int Priority { get; set; } = 5;
    public bool IsDelivered { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public int RetryCount { get; set; } = 0;
    public DateTime? NextRetryAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
