using Microsoft.AspNetCore.Mvc;
using PCClarity.API.Models;
using PCClarity.API.Services;

namespace PCClarity.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PcHealthController : ControllerBase
{
    private readonly PcMetricsService _metrics;
    private readonly SubscriptionService _subscriptions;
    private readonly ILogger<PcHealthController> _logger;

    // MVP rate limiter: userId → last scan time
    // Replace with Redis or DB-backed solution in production
    private static readonly Dictionary<string, DateTime> _lastScan = new();

    public PcHealthController(
        PcMetricsService metrics,
        SubscriptionService subscriptions,
        ILogger<PcHealthController> logger)
    {
        _metrics       = metrics;
        _subscriptions = subscriptions;
        _logger        = logger;
    }

    /// <summary>
    /// GET /api/pchealth/scan?userId=abc123
    /// Free plan: 1 scan per 24 hours
    /// Pro plan:  unlimited
    /// </summary>
    [HttpGet("scan")]
    public IActionResult Scan([FromQuery] string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId is required." });

        var status = _subscriptions.GetStatus(userId);
        bool isPro = status.IsActive && status.Plan == "pro";

        // Rate limit free users
        if (!isPro)
        {
            if (_lastScan.TryGetValue(userId, out var last) &&
                DateTime.UtcNow - last < TimeSpan.FromHours(24))
            {
                var waitMinutes = (int)(TimeSpan.FromHours(24) - (DateTime.UtcNow - last)).TotalMinutes;
                return StatusCode(429, new
                {
                    error      = "Free plan allows 1 scan per 24 hours.",
                    retryAfterMinutes = waitMinutes,
                    upgradeUrl = "/pricing"
                });
            }
        }

        _lastScan[userId] = DateTime.UtcNow;

        var report = _metrics.GetReport();
        _logger.LogInformation("Scan complete for {UserId} — score {Score}", userId, report.HealthScore);

        return Ok(report);
    }

    /// <summary>
    /// GET /api/pchealth/status?userId=abc123
    /// Returns subscription tier without consuming a scan.
    /// </summary>
    [HttpGet("status")]
    public IActionResult Status([FromQuery] string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId is required." });

        return Ok(_subscriptions.GetStatus(userId));
    }
}
