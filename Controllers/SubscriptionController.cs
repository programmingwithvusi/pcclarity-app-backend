using Microsoft.AspNetCore.Mvc;
using PCClarity.API.Models;
using PCClarity.API.Services;

namespace PCClarity.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SubscriptionController : ControllerBase
{
    private readonly SubscriptionService _subscriptions;
    private readonly ILogger<SubscriptionController> _logger;

    public SubscriptionController(
        SubscriptionService subscriptions,
        ILogger<SubscriptionController> logger)
    {
        _subscriptions = subscriptions;
        _logger        = logger;
    }

    /// <summary>
    /// POST /api/subscription/checkout
    /// Creates a Stripe Checkout session and returns the redirect URL.
    /// </summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CreateCheckoutRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "userId is required." });

        if (string.IsNullOrWhiteSpace(request.PriceId))
            return BadRequest(new { error = "priceId is required." });

        try
        {
            var response = await _subscriptions.CreateCheckoutSessionAsync(request);
            return Ok(response);
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogError(ex, "Stripe error creating checkout session.");
            return StatusCode(502, new { error = "Payment provider error. Please try again." });
        }
    }

    /// <summary>
    /// GET /api/subscription/status?userId=abc123
    /// Returns the user's current plan.
    /// </summary>
    [HttpGet("status")]
    public IActionResult Status([FromQuery] string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId is required." });

        return Ok(_subscriptions.GetStatus(userId));
    }

    /// <summary>
    /// POST /api/subscription/webhook
    /// Stripe sends events here. Must be excluded from CSRF and auth middleware.
    /// Register this URL in your Stripe Dashboard → Webhooks.
    /// </summary>
    [HttpPost("webhook")]
    public IActionResult Webhook()
    {
        var payload   = new StreamReader(HttpContext.Request.Body).ReadToEnd();
        var signature = Request.Headers["Stripe-Signature"].ToString();

        if (string.IsNullOrEmpty(signature))
            return BadRequest(new { error = "Missing Stripe-Signature header." });

        try
        {
            _subscriptions.HandleWebhook(payload, signature);
            return Ok(new { received = true });
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogWarning(ex, "Webhook processing failed.");
            return BadRequest(new { error = "Webhook error." });
        }
    }
}
