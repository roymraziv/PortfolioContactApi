using PortfolioContactApi.Models;

namespace PortfolioContactApi.Services;

public interface IEmailFormatter
{
    (string subject, string textBody, string htmlBody) FormatEmail(IFormRequest request);
}