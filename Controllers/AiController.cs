using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

[ApiController]
[Route("api/ai")]
public class AiController : ControllerBase
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    public AiController(IHttpClientFactory http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    [HttpPost("message")]
    public async Task<IActionResult> SendMessage([FromBody] string userMessage)
    {
        var client = _http.CreateClient("Anthropic");

        var payload = new
        {
            model = "claude-3-5-sonnet-20240620",
            max_tokens = 256,
            messages = new[]
            {
                new { role = "user", content = userMessage }
            }
        };

        var response = await client.PostAsJsonAsync("messages", payload);
        var result = await response.Content.ReadAsStringAsync();

        return Ok(result);
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest req)
    {

        var client = _http.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", _config["Anthropic:apiKey"]);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var payload = new
        {
            model = "claude-sonnet-4-20250514",
            max_tokens = 1000,
            system = "You are a friendly PC health assistant inside a Windows app called PCClarity. Users have little or no tech knowledge. Answer in plain, warm English — 2 to 4 sentences max. No bullet points. If you use a tech term, explain it simply in brackets.",
            messages = new[] { new { role = "user", content = req.Question } }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("https://api.anthropic.com/v1/messages", content);
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        return Ok(new { text });
    }
}

public record AskRequest(string Question);