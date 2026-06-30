using Microsoft.EntityFrameworkCore;
using PCClarity.API.Models;

namespace PCClarity.API.Services;

public class SubscriptionEntity
{
    public string UserId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Plan { get; set; } = "free";
    public DateTime? ExpiresAt { get; set; }
}

public class SubscriptionDbContext : DbContext
{
    public SubscriptionDbContext(DbContextOptions<SubscriptionDbContext> options) : base(options) { }
    public DbSet<SubscriptionEntity> Subscriptions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubscriptionEntity>().HasKey(s => s.UserId);
        base.OnModelCreating(modelBuilder);
    }
}

// EF Core-based sample implementation.
public class EfDbSubscriptionService : SubscriptionService
{
    private readonly SubscriptionDbContext _db;

    public EfDbSubscriptionService(SubscriptionDbContext db, ILogger<SubscriptionService> logger, IConfiguration config)
        : base(logger, config)
    {
        _db = db;
    }

    public override SubscriptionStatusResponse GetStatus(string userId)
    {
        var entity = _db.Subscriptions.Find(userId);
        if (entity == null)
            return new SubscriptionStatusResponse { UserId = userId, IsActive = false, Plan = "free" };

        return new SubscriptionStatusResponse
        {
            UserId = entity.UserId,
            IsActive = entity.IsActive,
            Plan = entity.Plan,
            ExpiresAt = entity.ExpiresAt
        };
    }

    protected override void ActivateSubscription(Stripe.Subscription sub)
    {
        var userId = sub.Metadata.GetValueOrDefault("userId", sub.CustomerId);
        var entity = _db.Subscriptions.Find(userId) ?? new SubscriptionEntity { UserId = userId };
        entity.IsActive = sub.Status == "active";
        entity.Plan = "pro";
        entity.ExpiresAt = sub.CurrentPeriodEnd;
        if (_db.Entry(entity).State == EntityState.Detached)
            _db.Subscriptions.Add(entity);
        _db.SaveChanges();

        base.ActivateSubscription(sub);
    }

    protected override void CancelSubscription(Stripe.Subscription sub)
    {
        var userId = sub.Metadata.GetValueOrDefault("userId", sub.CustomerId);
        var entity = _db.Subscriptions.Find(userId);
        if (entity != null)
        {
            entity.IsActive = false;
            entity.Plan = "free";
            entity.ExpiresAt = null;
            _db.SaveChanges();
        }

        base.CancelSubscription(sub);
    }
}
