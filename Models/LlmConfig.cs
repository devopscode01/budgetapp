namespace BudgetApp.Models;

public enum LlmProvider { Ollama = 0, OpenAI = 1, Gemini = 2 }

public sealed class LlmConfig
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int Provider { get; set; }                                  // LlmProvider enum
    public string Endpoint { get; set; } = "http://localhost:11434";   // Ollama base URL
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "llama3.2";
    public bool IsEnabled { get; set; }
}
