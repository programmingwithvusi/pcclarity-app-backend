using PCClarity.API.Services;
using Microsoft.EntityFrameworkCore;
using Stripe;
using AppSubscriptionService = PCClarity.API.Services.SubscriptionService;

var builder = WebApplication.CreateBuilder(args);

//var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

// CORS — allow your frontend origin
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",           // local dev
                "http://localhost:5173",           // local Vite dev
                "https://pcclarity-app.vercel.app" // Vercel production
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});


// Load from appsettings.json + environment variables
builder.Configuration.AddEnvironmentVariables();

var anthropicApiKey = builder.Configuration["AnthropicApiKey"];

builder.Services.AddHttpClient("Anthropic", client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/v1/");
    client.DefaultRequestHeaders.Add("x-api-key", anthropicApiKey);
    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
});


// Stripe
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"]
    ?? throw new InvalidOperationException("Stripe:SecretKey is not configured.");

// App services
builder.Services.AddSingleton<PcMetricsService>();

// If a DB-backed subscription store is enabled in config, register it.
if (builder.Configuration.GetValue<bool>("SubscriptionDb:Enabled"))
{
    var provider = builder.Configuration["SubscriptionDb:Provider"]?.ToLowerInvariant() ?? "sqlite"; // sqlite|postgres|mssql
    var conn = builder.Configuration["SubscriptionDb:ConnectionString"] ?? (provider == "sqlite" ? "Data Source=subscriptions.db" : throw new InvalidOperationException("SubscriptionDb:ConnectionString is required for non-sqlite providers."));

    if (provider == "sqlite")
    {
        builder.Services.AddDbContext<PCClarity.API.Services.SubscriptionDbContext>(options => options.UseSqlite(conn));
    }
    else if (provider == "postgres")
    {
        builder.Services.AddDbContext<PCClarity.API.Services.SubscriptionDbContext>(options => options.UseNpgsql(conn));
    }
    else if (provider == "mssql" || provider == "sqlserver")
    {
        builder.Services.AddDbContext<PCClarity.API.Services.SubscriptionDbContext>(options => options.UseSqlServer(conn));
    }
    else
    {
        throw new InvalidOperationException($"Unsupported SubscriptionDb:Provider '{provider}'");
    }

    builder.Services.AddScoped<PCClarity.API.Services.EfDbSubscriptionService>();
    builder.Services.AddScoped<PCClarity.API.Services.SubscriptionService>(sp => sp.GetRequiredService<PCClarity.API.Services.EfDbSubscriptionService>());
}
else
{
    builder.Services.AddSingleton<AppSubscriptionService>();
}


var app = builder.Build();

// Optionally apply EF Core migrations at startup if explicitly enabled.
if (builder.Configuration.GetValue<bool>("SubscriptionDb:Enabled") && builder.Configuration.GetValue<bool>("SubscriptionDb:ApplyMigrations"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetService<PCClarity.API.Services.SubscriptionDbContext>();
    db?.Database.Migrate();
}


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    // Helpful during local development: redirect root to Swagger UI
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.UseCors("FrontendPolicy");
app.UseAuthorization();
app.MapControllers();

app.Run();
