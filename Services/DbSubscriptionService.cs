using System.Data;
using System.Data.SQLite;
using Dapper;
using PCClarity.API.Models;

namespace PCClarity.API.Services;

// Simple SQLite-backed SubscriptionService example.
// Not hardened for production — demonstrates how to replace the in-memory store.
public class DbSubscriptionService : SubscriptionService
{
    private readonly string _connectionString;

    public DbSubscriptionService(ILogger<SubscriptionService> logger, IConfiguration config)
        : base(logger, config)
    {
        // Example connection string — file-based DB in the app folder
        _connectionString = config["SubscriptionDb:ConnectionString"]
            ?? "Data Source=subscriptions.db;Version=3;";

        Initialize();
    }

    // Exposed for startup migration/initialization
    public void EnsureDatabase() => Initialize();

    private IDbConnection Connection() => new SQLiteConnection(_connectionString);

    private void Initialize()
    {
        using var db = Connection();
        db.Execute(@"CREATE TABLE IF NOT EXISTS subscriptions (
                        userId TEXT PRIMARY KEY,
                        isActive INTEGER,
                        plan TEXT,
                        expiresAt TEXT
                    );");
    }

    public override SubscriptionStatusResponse GetStatus(string userId)
    {
        using var db = Connection();
        var row = db.QuerySingleOrDefault<dynamic>(
            "SELECT userId, isActive, plan, expiresAt FROM subscriptions WHERE userId = @UserId",
            new { UserId = userId });

        if (row == null)
            return new SubscriptionStatusResponse { UserId = userId, IsActive = false, Plan = "free" };

        return new SubscriptionStatusResponse
        {
            UserId = row.userId,
            IsActive = row.isActive == 1,
            Plan = row.plan,
            ExpiresAt = row.expiresAt == null ? null : DateTime.Parse((string)row.expiresAt)
        };
    }

    protected override void ActivateSubscription(Stripe.Subscription sub)
    {
        var userId = sub.Metadata.GetValueOrDefault("userId", sub.CustomerId);
        using var db = Connection();
        db.Execute(@"INSERT OR REPLACE INTO subscriptions (userId, isActive, plan, expiresAt)
                     VALUES (@UserId, @IsActive, @Plan, @ExpiresAt)", new
        {
            UserId = userId,
            IsActive = sub.Status == "active" ? 1 : 0,
            Plan = "pro",
            ExpiresAt = sub.CurrentPeriodEnd
        });

        // call base logger
        base.ActivateSubscription(sub);
    }

    protected override void CancelSubscription(Stripe.Subscription sub)
    {
        var userId = sub.Metadata.GetValueOrDefault("userId", sub.CustomerId);
        using var db = Connection();
        db.Execute(@"INSERT OR REPLACE INTO subscriptions (userId, isActive, plan, expiresAt)
                     VALUES (@UserId, 0, 'free', NULL)", new { UserId = userId });

        base.CancelSubscription(sub);
    }
}
