using System.Text;
using System.Text.Json;
using BudgetApp.Models;

namespace BudgetApp.Services;

public sealed class LlmService(IHttpClientFactory httpFactory, ILogger<LlmService> logger)
{
    private const string OcrPrompt =
        "Extract all bank transactions from this statement screenshot. " +
        "Return ONLY a JSON array, no markdown, no explanation. " +
        "Each element must have: date (YYYY-MM-DD string), description (string), amount (number). " +
        "Use negative amounts for debits/expenses and positive for credits/income. " +
        "Copy each description EXACTLY as it appears on the statement — do not normalize, shorten, or rename merchants. " +
        "Skip headers, balance rows, and summary rows — only actual posted transactions. " +
        "Include every transaction visible in the image; do not stop early.";

    public async Task<(string? Content, string? Error)> AnalyzeAsync(LlmConfig config, string prompt, CancellationToken ct)
    {
        try
        {
            var content = (LlmProvider)config.Provider switch
            {
                LlmProvider.Gemini => await CallGeminiAsync(config, prompt, ct),
                _                  => await CallOpenAiCompatibleAsync(config, prompt, ct),
            };
            return (content, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LLM call failed ({Provider})", (LlmProvider)config.Provider);
            return (null, ex.Message);
        }
    }

    public async Task<(IReadOnlyList<RawStatementLine>? Lines, string? Error)> ExtractTransactionsFromImageAsync(
        LlmConfig config, byte[] imageBytes, string mimeType, CancellationToken ct)
    {
        try
        {
            var raw = (LlmProvider)config.Provider switch
            {
                LlmProvider.Gemini => await CallGeminiImageAsync(config, imageBytes, mimeType, ct),
                _                  => await CallOpenAiImageAsync(config, imageBytes, mimeType, ct),
            };
            return (ParseOcrJson(raw ?? ""), null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OCR LLM call failed ({Provider})", (LlmProvider)config.Provider);
            return (null, ex.Message);
        }
    }

    private static IReadOnlyList<RawStatementLine> ParseOcrJson(string raw)
    {
        var text = raw.Trim();
        // Strip markdown code fences the model might wrap the JSON in
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var start = text.IndexOf('\n') + 1;
            var end = text.LastIndexOf("```", StringComparison.Ordinal);
            if (end > start) text = text[start..end].Trim();
        }

        using var doc = JsonDocument.Parse(text);
        var result = new List<RawStatementLine>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var dateStr = el.GetProperty("date").GetString() ?? "";
            var desc = el.GetProperty("description").GetString() ?? "";
            var amount = el.GetProperty("amount").GetDecimal();
            if (!DateOnly.TryParse(dateStr, out var date) || string.IsNullOrWhiteSpace(desc)) continue;
            result.Add(new RawStatementLine(date, desc.Trim(), amount));
        }
        return result;
    }

    private async Task<string?> CallOpenAiImageAsync(LlmConfig config, byte[] imageBytes, string mimeType, CancellationToken ct)
    {
        var baseUrl = (LlmProvider)config.Provider == LlmProvider.Ollama
            ? config.Endpoint.TrimEnd('/') + "/v1"
            : "https://api.openai.com/v1";

        var base64 = Convert.ToBase64String(imageBytes);
        var body = JsonSerializer.Serialize(new
        {
            model      = config.Model,
            max_tokens = 4096,
            messages   = new[]
            {
                new
                {
                    role    = "user",
                    content = new object[]
                    {
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64}" } },
                        new { type = "text",      text      = OcrPrompt }
                    }
                }
            }
        });

        using var http = httpFactory.CreateClient();
        if (!string.IsNullOrEmpty(config.ApiKey))
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
        http.Timeout = TimeSpan.FromSeconds(120);

        var resp = await http.PostAsync($"{baseUrl}/chat/completions",
            new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Provider returned {(int)resp.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    private async Task<string?> CallGeminiImageAsync(LlmConfig config, byte[] imageBytes, string mimeType, CancellationToken ct)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{config.Model}:generateContent?key={config.ApiKey}";
        var base64 = Convert.ToBase64String(imageBytes);
        var body = JsonSerializer.Serialize(new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { inline_data = new { mime_type = mimeType, data = base64 } },
                        new { text = OcrPrompt }
                    }
                }
            },
            generationConfig = new { maxOutputTokens = 4096 }
        });

        using var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(120);
        var resp = await http.PostAsync(url,
            new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini returned {(int)resp.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
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
        if (!string.IsNullOrEmpty(config.ApiKey))
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");

        var resp = await http.PostAsync($"{baseUrl}/chat/completions",
            new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Provider returned {(int)resp.StatusCode}: {json}");

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

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini returned {(int)resp.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
    }
}
