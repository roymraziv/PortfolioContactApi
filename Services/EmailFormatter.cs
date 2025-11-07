using System.Text;
using System.Web;
using PortfolioContactApi.Models;

namespace PortfolioContactApi.Services;

public class EmailFormatter : IEmailFormatter
{
    public (string subject, string textBody, string htmlBody) FormatEmail(IFormRequest request)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
        var formType = request.GetFormType();
        var formData = request.GetFormData();

        var subject = formType switch
        {
            "contact" => "New Contact Form Submission",
            "vision" => "New Vision Project Inquiry",
            _ => "New Form Submission"
        };

        var textBody = BuildTextBody(formType, formData, timestamp, request.ClientId);
        var htmlBody = BuildHtmlBody(formType, formData, timestamp, request.ClientId);

        return (subject, textBody, htmlBody);
    }

    private string BuildTextBody(string formType, Dictionary<string, string> data, string timestamp, string clientId)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"New {formType} form submission from {clientId}");
        sb.AppendLine();

        foreach (var (key, value) in data)
        {
            sb.AppendLine($"{key}: {value}");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"Sent: {timestamp}");
        sb.AppendLine($"Client: {clientId}");

        return sb.ToString();
    }

    private string BuildHtmlBody(string formType, Dictionary<string, string> data, string timestamp, string clientId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<html><head></head>");
        sb.AppendLine("<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>");
        sb.AppendLine("  <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>");
        sb.AppendLine($"    <h2 style='color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 10px;'>New {formType.ToUpper()} Form Submission</h2>");
        sb.AppendLine("    <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>");

        foreach (var (key, value) in data)
        {
            var encodedValue = HttpUtility.HtmlEncode(value);
            if (key.Contains("Email", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"      <p style='margin: 5px 0;'><strong>{key}:</strong> <a href='mailto:{encodedValue}'>{encodedValue}</a></p>");
            }
            else if (key.Contains("Message", StringComparison.OrdinalIgnoreCase) || 
                     key.Contains("Description", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"      <p style='margin: 5px 0;'><strong>{key}:</strong></p>");
                sb.AppendLine($"      <p style='white-space: pre-wrap; margin: 0 0 10px 15px;'>{encodedValue}</p>");
            }
            else
            {
                sb.AppendLine($"      <p style='margin: 5px 0;'><strong>{key}:</strong> {encodedValue}</p>");
            }
        }

        sb.AppendLine("    </div>");
        sb.AppendLine("    <hr style='border: none; border-top: 1px solid #ddd; margin: 20px 0;'>");
        sb.AppendLine($"    <p style='color: #7f8c8d; font-size: 12px; margin: 5px 0;'>Sent: {timestamp}<br>Client: {clientId}</p>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }
}