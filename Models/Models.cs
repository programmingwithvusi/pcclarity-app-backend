namespace PCClarity.API.Models;

// ── PC Health ────────────────────────────────────────────────

public record PcHealthReport
{
    public int HealthScore { get; init; }          // 0-100
    public string ScoreLabel { get; init; } = "";  // "Good" | "Fair" | "Poor"
    public DiskMetrics Disk { get; init; } = new();
    public RamMetrics Ram { get; init; } = new();
    public StartupMetrics Startup { get; init; } = new();
    public List<HealthIssue> Issues { get; init; } = [];
    public DateTime ScannedAt { get; init; } = DateTime.UtcNow;
}

public record DiskMetrics
{
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public long JunkBytes { get; init; }           // estimated temp/cache
    public int UsedPercent => TotalBytes > 0
        ? (int)(100 - (FreeBytes * 100.0 / TotalBytes))
        : 0;
}

public record RamMetrics
{
    public long TotalBytes { get; init; }
    public long AvailableBytes { get; init; }
    public int UsedPercent => TotalBytes > 0
        ? (int)(100 - (AvailableBytes * 100.0 / TotalBytes))
        : 0;
}

public record StartupMetrics
{
    public int Count { get; init; }
    public List<string> Programs { get; init; } = [];
}

public record HealthIssue
{
    public string Id { get; init; } = "";
    public string Severity { get; init; } = "info"; // "critical" | "warning" | "info"
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
}

// ── Stripe / Subscriptions ───────────────────────────────────

public record CreateCheckoutRequest
{
    public string UserId { get; init; } = "";
    public string PriceId { get; init; } = "";      // Stripe price ID
    public string SuccessUrl { get; init; } = "";
    public string CancelUrl { get; init; } = "";
}

public record CreateCheckoutResponse
{
    public string SessionId { get; init; } = "";
    public string CheckoutUrl { get; init; } = "";
}

public record SubscriptionStatusResponse
{
    public string UserId { get; init; } = "";
    public bool IsActive { get; init; }
    public string Plan { get; init; } = "free";     // "free" | "pro"
    public DateTime? ExpiresAt { get; init; }
}

public record WebhookRequest
{
    public string Payload { get; init; } = "";
    public string Signature { get; init; } = "";
}
