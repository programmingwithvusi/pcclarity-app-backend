#if WINDOWS
using System.Management;
#endif
using System.Runtime.Versioning;
using PCClarity.API.Models;

namespace PCClarity.API.Services;

/// <summary>
/// Reads real PC metrics on Windows via WMI.
/// Falls back to safe mock values on non-Windows (dev/Linux/macOS).
/// </summary>
public class PcMetricsService
{
    private readonly ILogger<PcMetricsService> _logger;

    public PcMetricsService(ILogger<PcMetricsService> logger)
    {
        _logger = logger;
    }

    public PcHealthReport GetReport()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return BuildWindowsReport();

            _logger.LogWarning("Non-Windows OS detected — returning mock data.");
            return BuildMockReport();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read PC metrics.");
            return BuildMockReport();
        }
    }

    // ── Windows (real data via WMI) ──────────────────────────

#if WINDOWS
    [SupportedOSPlatform("windows")]
    private PcHealthReport BuildWindowsReport()
    {
        var disk    = GetDiskMetrics();
        var ram     = GetRamMetrics();
        var startup = GetStartupMetrics();
        var junkGb  = disk.JunkBytes / (1024.0 * 1024 * 1024);

        var issues = new List<HealthIssue>();

        if (disk.UsedPercent > 85)
            issues.Add(new() { Id = "disk_full", Severity = "critical",
                Title = "Disk almost full",
                Detail = $"{disk.UsedPercent}% of your drive is used — this slows Windows down significantly." });

        if (junkGb > 1)
            issues.Add(new() { Id = "junk_files", Severity = "critical",
                Title = "Temp files taking up space",
                Detail = $"{junkGb:F1} GB of junk files found in Windows temp folders and user cache." });

        if (startup.Count > 10)
            issues.Add(new() { Id = "startup_heavy", Severity = "warning",
                Title = "Too many startup programs",
                Detail = $"{startup.Count} programs launch when Windows starts — this adds boot time." });

        if (ram.UsedPercent > 75)
            issues.Add(new() { Id = "high_ram", Severity = "warning",
                Title = "High memory usage",
                Detail = $"Your PC is using {ram.UsedPercent}% of RAM at idle." });

        if (issues.Count == 0)
            issues.Add(new() { Id = "all_good", Severity = "info",
                Title = "Your PC looks healthy",
                Detail = "No major issues found. Keep running regular scans." });

        int score = CalculateScore(disk, ram, startup, junkGb);

        return new PcHealthReport
        {
            HealthScore = score,
            ScoreLabel  = score >= 80 ? "Good" : score >= 55 ? "Fair" : "Poor",
            Disk        = disk,
            Ram         = ram,
            Startup     = startup,
            Issues      = issues
        };
    }

    [SupportedOSPlatform("windows")]
    private static DiskMetrics GetDiskMetrics()
    {
        long total = 0, free = 0, junk = 0;

        using var searcher = new ManagementObjectSearcher(
            "SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3");

        foreach (ManagementObject d in searcher.Get())
        {
            total += Convert.ToInt64(d["Size"]);
            free  += Convert.ToInt64(d["FreeSpace"]);
        }

        junk += GetFolderSize(Path.GetTempPath());
        junk += GetFolderSize(@"C:\Windows\Temp");

        return new DiskMetrics { TotalBytes = total, FreeBytes = free, JunkBytes = junk };
    }

    [SupportedOSPlatform("windows")]
    private static RamMetrics GetRamMetrics()
    {
        long total = 0, available = 0;

        using var totalSearcher = new ManagementObjectSearcher(
            "SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
        foreach (ManagementObject o in totalSearcher.Get())
            total = Convert.ToInt64(o["TotalVisibleMemorySize"]) * 1024;

        using var freeSearcher = new ManagementObjectSearcher(
            "SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
        foreach (ManagementObject o in freeSearcher.Get())
            available = Convert.ToInt64(o["FreePhysicalMemory"]) * 1024;

        return new RamMetrics { TotalBytes = total, AvailableBytes = available };
    }

    [SupportedOSPlatform("windows")]
    private static StartupMetrics GetStartupMetrics()
    {
        var programs = new List<string>();

        var runKeys = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"
        };

        foreach (var key in runKeys)
        {
            using var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key);
            if (reg == null) continue;
            programs.AddRange(reg.GetValueNames());
        }

        foreach (var key in runKeys)
        {
            using var reg = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
            if (reg == null) continue;
            programs.AddRange(reg.GetValueNames());
        }

        return new StartupMetrics { Count = programs.Count, Programs = programs };
    }
#else
    // On Linux/macOS the Windows branch is compiled out entirely.
    // GetReport() already routes non-Windows to BuildMockReport() at runtime,
    // but we need a stub so the compiler is satisfied.
    private PcHealthReport BuildWindowsReport() => BuildMockReport();
#endif

    private static long GetFolderSize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        }
        catch { return 0; }
    }

    // ── Score calculation ────────────────────────────────────

    private static int CalculateScore(DiskMetrics disk, RamMetrics ram,
        StartupMetrics startup, double junkGb)
    {
        int score = 100;
        score -= Math.Min(25, disk.UsedPercent - 50);
        score -= Math.Min(20, (int)(junkGb * 4));
        score -= Math.Min(20, (startup.Count - 5) * 2);
        score -= Math.Min(15, ram.UsedPercent - 60);
        return Math.Clamp(score, 0, 100);
    }

    // ── Mock (non-Windows fallback) ──────────────────────────

    private static PcHealthReport BuildMockReport() => new()
    {
        HealthScore = 68,
        ScoreLabel  = "Fair",
        Disk        = new() { TotalBytes = 500L * 1024 * 1024 * 1024,
                              FreeBytes  = 120L * 1024 * 1024 * 1024,
                              JunkBytes  = 4L   * 1024 * 1024 * 1024 },
        Ram         = new() { TotalBytes      = 16L * 1024 * 1024 * 1024,
                              AvailableBytes   =  3L * 1024 * 1024 * 1024 },
        Startup     = new() { Count = 14, Programs = ["Spotify", "Teams", "Discord",
                              "OneDrive", "Skype", "Steam", "Zoom"] },
        Issues =
        [
            new() { Id = "junk_files",    Severity = "critical", Title = "Temp files taking up space",
                    Detail = "4.2 GB of junk files found in Windows temp folders." },
            new() { Id = "startup_heavy", Severity = "warning",  Title = "Too many startup programs",
                    Detail = "14 programs launch on startup — slowing your boot by ~40 seconds." },
            new() { Id = "high_ram",      Severity = "warning",  Title = "High memory usage",
                    Detail = "78% RAM used at idle — possible memory leak." },
            new() { Id = "defender_ok",   Severity = "info",     Title = "Windows Defender is up to date",
                    Detail = "Last updated 2 hours ago." }
        ]
    };
}
