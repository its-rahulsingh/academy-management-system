namespace Academy.Api.Models;

// A minimal, non-sensitive first entity for proving persistence.
// The real batch workflow and permissions come in a later phase.
public sealed class Batch
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
