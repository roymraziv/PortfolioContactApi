using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using PortfolioContactApi.Models;
using PortfolioContactApi.Services;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace PortfolioContactApi;

public class Function
{
    private readonly IAmazonSimpleEmailService _sesClient;
    private readonly IFormDeserializer _formDeserializer;
    private readonly IEmailFormatter _emailFormatter;
    private readonly Dictionary<string, string> _clientEmails;
    private readonly HashSet<string> _allowedOrigins;
    private readonly string _apiKey;
    private readonly string _verifiedSender;

    public Function()
        : this(new AmazonSimpleEmailServiceClient(), new FormDeserializer(), new EmailFormatter())
    {
    }

    public Function(
        IAmazonSimpleEmailService sesClient,
        IFormDeserializer formDeserializer,
        IEmailFormatter emailFormatter)
    {
        _sesClient = sesClient;
        _formDeserializer = formDeserializer;
        _emailFormatter = emailFormatter;
        _apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "";
        _verifiedSender = Environment.GetEnvironmentVariable("VERIFIED_SENDER_EMAIL") ?? "";
        var clientMappings = Environment.GetEnvironmentVariable("CLIENT_EMAIL_MAPPINGS");
        _clientEmails = ParseClientEmails(clientMappings);
        var allowedOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? "";
        _allowedOrigins = allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(o => o.Trim()).ToHashSet();
        
        Console.WriteLine($"Function initialized - API Key set: {!string.IsNullOrEmpty(_apiKey)}, Verified Sender: {_verifiedSender}, Client Emails count: {_clientEmails.Count}, Allowed Origins count: {_allowedOrigins.Count}");
    }

    private Dictionary<string, string> ParseClientEmails(string? mappings)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(mappings)) return result;
        
        var pairs = mappings.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split(':');
            if (parts.Length != 2) continue;
            result.Add(parts[0].Trim(), parts[1].Trim());
        }
        return result;
    }
    
    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        context.Logger.LogInformation("=== Function invoked ===");
        
        // Add null checks with logging
        if (request == null)
        {
            context.Logger.LogError("Request is null");
            return CreateResponse(400, new { error = "Invalid request" }, new Dictionary<string, string>
            {
                { "Content-Type", "application/json" }
            });
        }
        
        context.Logger.LogInformation($"HTTP Method: {request.HttpMethod ?? "null"}");
        
        if (request.Headers == null)
        {
            context.Logger.LogError("Request.Headers is null");
            return CreateResponse(400, new { error = "Invalid request headers" }, new Dictionary<string, string>
            {
                { "Content-Type", "application/json" }
            });
        }
        
        context.Logger.LogInformation($"Headers count: {request.Headers.Count}");
        context.Logger.LogInformation($"Headers: {string.Join(", ", request.Headers.Keys)}");
        
        // Get origin for CORS
        var origin = request.Headers.ContainsKey("origin") 
            ? request.Headers["origin"] 
            : request.Headers.ContainsKey("Origin") 
                ? request.Headers["Origin"] 
                : "";
        
        context.Logger.LogInformation($"Origin: {origin}");

        // Base CORS headers
        var headers = new Dictionary<string, string>
        {
            { "Content-Type", "application/json" },
            { "Access-Control-Allow-Methods", "POST, OPTIONS" },
            { "Access-Control-Allow-Headers", "Content-Type, x-api-key" }
        };

        // Validate and set CORS origin
        if (!string.IsNullOrEmpty(origin) && IsOriginAllowed(origin))
        {
            headers["Access-Control-Allow-Origin"] = origin;
        }
        else
        {
            headers["Access-Control-Allow-Origin"] = _allowedOrigins.FirstOrDefault() ?? "*";
        }

        // Handle OPTIONS preflight request
        if (request.HttpMethod?.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase) == true)
        {
            context.Logger.LogInformation("Handling OPTIONS preflight request");
            return CreateResponse(200, new { message = "OK" }, headers);
        }

        try
        {
            context.Logger.LogInformation("Starting request validation");
            
            // Validate API key
            context.Logger.LogInformation("Validating API key");
            if (!ValidateApiKey(request.Headers))
            {
                context.Logger.LogWarning("Invalid API key attempt");
                return CreateResponse(403, new { error = "Forbidden: Invalid API key" }, headers);
            }
            context.Logger.LogInformation("API key validated successfully");

            // Validate origin
            context.Logger.LogInformation($"Validating origin: {origin}");
            if (!IsOriginAllowed(origin))
            {
                context.Logger.LogWarning($"Request from unauthorized origin: {origin}");
                return CreateResponse(403, new { error = "Forbidden: Unauthorized origin" }, headers);
            }
            context.Logger.LogInformation("Origin validated successfully");

            // Parse request body
            context.Logger.LogInformation($"Request body length: {request.Body?.Length ?? 0}");
            context.Logger.LogInformation($"Request body: {request.Body ?? "null"}");
            
            if (string.IsNullOrEmpty(request.Body))
            {
                context.Logger.LogError("Request body is null or empty");
                return CreateResponse(400, new { error = "Request body is required" }, headers);
            }
            
            // Get form type from header or default to "contact"
            var formType = request.Headers.ContainsKey("x-form-type") 
                ? request.Headers["x-form-type"] 
                : "contact";
            
            context.Logger.LogInformation($"Form type: {formType}");

            // Deserialize using the appropriate type
            var body = _formDeserializer.Deserialize(request.Body, formType);

            if (body == null)
            {
                context.Logger.LogError("Failed to deserialize request body");
                return CreateResponse(400, new { error = "Invalid request body or form type" }, headers);
            }
            
            context.Logger.LogInformation($"Request parsed - ClientId: {body.ClientId}, FormType: {body.GetFormType()}");

            // Validate (you can create IFormValidator for this too)
            var validationError = ValidateFormRequest(body);
            if (validationError != null)
            {
                context.Logger.LogWarning($"Validation failed: {validationError}");
                return CreateResponse(400, new { error = validationError }, headers);
            }

            // Get recipient email
            if (!_clientEmails.TryGetValue(body.ClientId, out var recipientEmail))
            {
                context.Logger.LogWarning($"Unknown client ID: {body.ClientId}");
                return CreateResponse(400, new { error = "Invalid client ID" }, headers);
            }

            // Send email
            context.Logger.LogInformation("Attempting to send email via SES");
            var messageId = await SendEmailAsync(body, recipientEmail, context);

            context.Logger.LogInformation($"Email sent successfully. MessageId: {messageId}");

            return CreateResponse(200, new 
            { 
                success = true, 
                message = "Email sent successfully",
                messageId = messageId
            }, headers);
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error processing request: {ex.Message}");
            context.Logger.LogError(ex.StackTrace);
            return CreateResponse(500, new { error = "Internal server error" }, headers);
        }
    }

    private APIGatewayProxyResponse CreateResponse(int statusCode, object body, Dictionary<string, string> headers)
    {
        return new APIGatewayProxyResponse
        {
            StatusCode = statusCode,
            Body = JsonSerializer.Serialize(body),
            Headers = headers
        };
    }
    
    private bool ValidateApiKey(IDictionary<string, string> headers)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return false;

        var requestApiKey = headers.ContainsKey("x-api-key") 
            ? headers["x-api-key"] 
            : headers.ContainsKey("X-Api-Key") 
                ? headers["X-Api-Key"] 
                : "";

        return requestApiKey == _apiKey;
    }
    
    private bool IsOriginAllowed(string origin)
    {
        if (string.IsNullOrEmpty(origin) || _allowedOrigins.Count == 0)
            return false;

        return _allowedOrigins.Any(allowed => origin.Contains(allowed, StringComparison.OrdinalIgnoreCase));
    }
    
    private string? ValidateFormRequest(IFormRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
            return "Client ID is required";

        var formData = request.GetFormData();
        
        // Check required fields
        foreach (var (key, value) in formData)
        {
            if (string.IsNullOrWhiteSpace(value))
                return $"{key} is required";
        }

        return null;
    }

    private async Task<string> SendEmailAsync(IFormRequest request, string recipientEmail, ILambdaContext context)
    {
        var (subject, textBody, htmlBody) = _emailFormatter.FormatEmail(request);
        
        var emailAddress = request.GetFormData()
            .FirstOrDefault(x => x.Key.Contains("Email", StringComparison.OrdinalIgnoreCase))
            .Value ?? "";

        var sendRequest = new SendEmailRequest
        {
            Source = _verifiedSender,
            Destination = new Destination
            {
                ToAddresses = new List<string> { recipientEmail }
            },
            Message = new Message
            {
                Subject = new Content(subject),
                Body = new Body
                {
                    Text = new Content(textBody),
                    Html = new Content(htmlBody)
                }
            },
            ReplyToAddresses = !string.IsNullOrEmpty(emailAddress) 
                ? new List<string> { emailAddress } 
                : null
        };

        var response = await _sesClient.SendEmailAsync(sendRequest);
        return response.MessageId;
    }
}
