namespace BudgetApp.Models;

public sealed class UserCategory
{
    public int Id { get; set; }

    /// <summary>Keycloak sub / user identifier.</summary>
    public string UserId { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Hex colour string, e.g. "#6366F1".</summary>
    public string Color { get; set; } = "#6366F1";

    /// <summary>Comma-separated keywords used for auto-classification during ETL.</summary>
    public string Keywords { get; set; } = "";

    public int SortOrder { get; set; }
}
