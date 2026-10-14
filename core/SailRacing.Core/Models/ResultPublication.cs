using SQLite;

namespace SailRacing.Models;

public enum PublishAckStatus
{
    Pending,
    Success,
    Failed
}

/// <summary>Audit record of a results publish attempt, so a failed POST can be retried.</summary>
public class ResultPublication
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Indexed]
    public string RaceId { get; set; } = string.Empty;

    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Serialized ResultPublication payload (see shared/contracts/result.schema.json).</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public PublishAckStatus ServerAckStatus { get; set; } = PublishAckStatus.Pending;
}
