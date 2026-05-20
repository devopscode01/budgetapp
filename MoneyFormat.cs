using System.Globalization;

namespace BudgetApp;

/// <summary>Consistent USD display (avoids invariant <c>¤</c> from <c>ToString(&quot;C&quot;, InvariantCulture)</c>).</summary>
public static class MoneyFormat
{
    public static readonly CultureInfo Usd = CultureInfo.GetCultureInfo("en-US");
}
