
namespace PortfolioContactApi.Models;

public class MyFolioVisionContactRequest : IFormRequest
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Status { get; set; } = "";
    public string ProjectType { get; set; } = "";
    public string Budget { get; set; } = "";
    public string Timeline { get; set; } = "";
    public string HasContent { get; set; } = "";
    public string Description { get; set; } = "";
    public string ClientId { get; set; } = "";

    public Dictionary<string, string> GetFormData()
    {
        return new Dictionary<string, string>
        {
            { "name", Name },
            { "email", Email },
            { "phone", Phone },
            { "status", Status },
            { "projectType", ProjectType },
            { "budget", Budget },
            { "timeline", Timeline },
            { "hasContent", HasContent },
            { "description", Description },
            { "clientId", ClientId }
        };
    }

    public string GetFormType()
    {
        return "vision";
    }
}