using UglyToad.PdfPig;

namespace BudgetApp.Services;

public sealed class PdfTextExtractor(ILogger<PdfTextExtractor> logger)
{
    public string ExtractAllText(string pdfPath)
    {
        try
        {
            using var document = PdfDocument.Open(pdfPath);
            var parts = new List<string>(document.NumberOfPages);
            for (var i = 1; i <= document.NumberOfPages; i++)
            {
                var page = document.GetPage(i);
                parts.Add(page.Text);
            }

            return string.Join('\n', parts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read PDF: {Path}", pdfPath);
            return string.Empty;
        }
    }
}
