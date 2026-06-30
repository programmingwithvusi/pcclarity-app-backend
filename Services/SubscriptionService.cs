using Stripe;
using Stripe.Checkout;
using Stripe.Infrastructure;
using PCClarity.API.Models;

namespace PCClarity.API.Services;

/// <summary>
/// Handles Stripe checkout sessions and subscription status.
/// In production, persist subscription state in a real database (Postgres, SQL Server, etc.).
/// For the MVP we use an in-memory dictionary keyed by userId.
/// </summary>
public class SubscriptionService
{
    private readonly ILogger<SubscriptionService> _logger;
    private readonly IConfiguration _config;

    // MVP: in-memory store  →  replace with DB in production
    private readonly Dictionary<string, SubscriptionStatusResponse> _store = new();

    public SubscriptionService(ILogger<SubscriptionService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    // ── Create Stripe Checkout Session ───────────────────────

    public virtual async Task<CreateCheckoutResponse> CreateCheckoutSessionAsync(
        CreateCheckoutRequest request)
    {
        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = ["card"],
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price    = request.PriceId,
                    Quantity = 1
                }
            ],
            Mode = "subscription",
            SuccessUrl = request.SuccessUrl + "?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = request.CancelUrl,
            Metadata = new Dictionary<string, string> { ["userId"] = request.UserId }
        };

        var service = new SessionService();
        Session session = await service.CreateAsync(options);

        _logger.LogInformation("Checkout session created: {SessionId} for user {UserId}",
            session.Id, request.UserId);

        return new CreateCheckoutResponse
        {
            SessionId = session.Id,
            CheckoutUrl = session.Url
        };
    }

    // ── Get Subscription Status ───────────────────────────────

    public virtual SubscriptionStatusResponse GetStatus(string userId)
    {
        if (_store.TryGetValue(userId, out var status))
            return status;

        return new SubscriptionStatusResponse
        {
            UserId = userId,
            IsActive = false,
            Plan = "free"
        };
    }

    // ── Handle Stripe Webhook ────────────────────────────────

    public virtual void HandleWebhook(string payload, string signature)
    {
        var webhookSecret = _config["Stripe:WebhookSecret"]
            ?? throw new InvalidOperationException("Stripe:WebhookSecret not configured.");

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signature, webhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Webhook signature validation failed.");
            throw;
        }

        switch (stripeEvent.Type)
        {
            case Events.CustomerSubscriptionCreated:
            case Events.CustomerSubscriptionUpdated:
                if (stripeEvent.Data.Object is Subscription sub)
                    ActivateSubscription(sub);
                break;

            case Events.CustomerSubscriptionDeleted:
                if (stripeEvent.Data.Object is Subscription cancelledSub)
                    CancelSubscription(cancelledSub);
                break;

                /*
            case EventTypes.CustomerSubscriptionCreated:
            case EventTypes.CustomerSubscriptionUpdated:
                if (stripeEvent.Data.Object is Subscription sub)
                    ActivateSubscription(sub);
                break;

            case EventTypes.CustomerSubscriptionDeleted:
                if (stripeEvent.Data.Object is Subscription cancelledSub)
                    CancelSubscription(cancelledSub);
                break;
                */
        }
    }

    protected virtual void ActivateSubscription(Subscription sub)
    {
        var userId = sub.Metadata.GetValueOrDefault("userId", sub.CustomerId);
        _store[userId] = new SubscriptionStatusResponse
        {
            UserId = userId,
            IsActive = sub.Status == "active",
            Plan = "pro",
            ExpiresAt = sub.CurrentPeriodEnd
        };
        _logger.LogInformation("Subscription activated for {UserId}", userId);
    }

    protected virtual void CancelSubscription(Subscription sub)
    {
        var userId = sub.Metadata.GetValueOrDefault("userId", sub.CustomerId);
        if (_store.ContainsKey(userId))
            _store[userId] = _store[userId] with { IsActive = false, Plan = "free" };

        _logger.LogInformation("Subscription cancelled for {UserId}", userId);
    }
}
