using System;
using System.Text.Json;
using PortfolioContactApi.Models;

namespace PortfolioContactApi.Services;

public class FormDeserializer : IFormDeserializer
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true
    };
    public IFormRequest? Deserialize(string json, string formType)
    {
        return formType.ToLower() switch
        {
            "contact" => JsonSerializer.Deserialize<ContactRequest>(json, _options),
            "vision" => JsonSerializer.Deserialize<MyFolioVisionContactRequest>(json, _options),
            _ => null
        };
    }
}
