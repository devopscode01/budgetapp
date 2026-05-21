using System.Text;
using System.Text.Json;
using BudgetApp.Models;

namespace BudgetApp.Services;

public sealed class LlmService(IHttpClientFactory httpFactory, ILogger<LlmService> logger)
{
    public async Task<string?> AnalyzeAsync(LlmConfig config, string prompt, CancellationToken ct)
    {
        try
        {
            return (LlmProvider)config.Provider switch
            {
                LlmProvider.Gemini => await CallGeminiAsync(config, prompt, ct),
                _                  => await CallOpenAiCompatibleAsync(config, prompt, ct),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LLM call failed ({Provider})", (LlmProvider)config.Provider);
            return null;
        }
    }

    private async Task<string?> CallOpenAiCompatibleAsync(LlmConfig config, string prompt, CancellationToken ct)
    {
        // Ollama exposes OpenAI-compatible /v1 endpoint; OpenAI uses api.openai.com/v1
        var baseUrl = (LlmProvider)config.Provider == LlmProvider.Ollama
            ? config.Endpoint.TrimEnd('/') + "/v1"
            : "https://api.openai.com/v1";

        var body = JsonSerializer.Serialize(new
        {
            model    = config.Model,
            messages = new[] { new { role = "user", content = prompt } },
        });

        using var http = httpFactory.CreateClient();
        if ((LlmProvider)config.Provider == LlmProvider.OpenAI && !string.IsNullOrEmpty(config.ApiKey))
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");

        var resp = await http.PostAsync($"{baseUrl}/chat/completions",
            new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    private async Task<string?> CallGeminiAsync(LlmConfig config, string prompt, CancellationToken ct)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{config.Model}:generateContent?key={config.ApiKey}";
        var body = JsonSerializer.Serialize(new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
        });

        using var http = httpFactory.CreateClient();
        var resp = await http.PostAsync(url,
            new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
    }
}
