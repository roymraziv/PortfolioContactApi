using System;

namespace PortfolioContactApi.Models;

public interface IFormRequest
{
    string ClientId { get; }
    string GetFormType();
    Dictionary<string, string> GetFormData();
}
