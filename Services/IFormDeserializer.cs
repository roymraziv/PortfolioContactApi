using System;
using PortfolioContactApi.Models;

namespace PortfolioContactApi.Services;

public interface IFormDeserializer
{
    IFormRequest? Deserialize(string json, string formType);
}
