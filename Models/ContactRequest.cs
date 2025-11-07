
namespace PortfolioContactApi.Models;

public class ContactRequest : IFormRequest
{
   public string ClientId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
   public string Message { get; set; } = "";

    public Dictionary<string, string> GetFormData()
    {
        return new Dictionary<string, string>
        {
            { "clientId", ClientId },
            { "name", Name },
            { "email", Email },
            { "message", Message }
        };
    }

    public string GetFormType()
    {
        return "contact";
    }
}