using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace PortfolioContactApi;

public class Function
{
    private readonly IAmazonSimpleEmailService _sesClient;
    private readonly Dictionary<string, string> _clientEmails;
    private readonly HashSet<string> _allowedOrigins;
    private readonly string _apiKey;
    private readonly string _verifiedSender;

    public Function()
    {
        _sesClient = new AmazonSimpleEmailServiceClient();
        _apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "";
        _verifiedSender = Environment.GetEnvironmentVariable("VERIFIED_SENDER_EMAIL") ?? "";
        var clientMappings = Environment.GetEnvironmentVariable("CLIENT_EMAIL_MAPPINGS");
        _clientEmails = ParseClientEmails(clientMappings);
        var allowedOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? "";
        _allowedOrigins = allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(o => o.Trim()).ToHashSet();
        
        Console.WriteLine($"Function initialized - API Key set: {!string.IsNullOrEmpty(_apiKey)}, Verified Sender: {_verifiedSender}, Client Emails count: {_clientEmails.Count}, Allowed Origins count: {_allowedOrigins.Count}");
    }

    public Function(IAmazonSimpleEmailService sesClient)
    {
        _sesClient = sesClient;
        _apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "";
        _verifiedSender = Environment.GetEnvironmentVariable("VERIFIED_SENDER_EMAIL") ?? "";
        var clientMappings = Environment.GetEnvironmentVariable("CLIENT_EMAIL_MAPPINGS");
        _clientEmails = ParseClientEmails(clientMappings);
        var allowedOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? "";
        _allowedOrigins = allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(o => o.Trim()).ToHashSet();
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
            
            var body = JsonSerializer.Deserialize<ContactRequest>(request.Body, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (body == null)
            {
                context.Logger.LogError("Failed to deserialize request body");
                return CreateResponse(400, new { error = "Invalid request body" }, headers);
            }
            
            context.Logger.LogInformation($"Request parsed - ClientId: {body.ClientId}, Name: {body.Name}, Email: {body.Email}");

            // Validate required fields
            var validationError = ValidateContactRequest(body);
            if (validationError != null)
            {
                context.Logger.LogWarning($"Validation failed: {validationError}");
                return CreateResponse(400, new { error = validationError }, headers);
            }
            
            context.Logger.LogInformation("Request validation passed");

            // Get recipient email from client ID
            context.Logger.LogInformation($"Looking up recipient email for ClientId: {body.ClientId}");
            context.Logger.LogInformation($"Available client IDs: {string.Join(", ", _clientEmails.Keys)}");
            
            if (!_clientEmails.TryGetValue(body.ClientId, out var recipientEmail))
            {
                context.Logger.LogWarning($"Unknown client ID: {body.ClientId}");
                return CreateResponse(400, new { error = "Invalid client ID" }, headers);
            }
            
            context.Logger.LogInformation($"Recipient email found: {recipientEmail}");

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
    
    private string? ValidateContactRequest(ContactRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
            return "Client ID is required";

        if (string.IsNullOrWhiteSpace(request.Name))
            return "Name is required";

        if (string.IsNullOrWhiteSpace(request.Email))
            return "Email is required";

        if (string.IsNullOrWhiteSpace(request.Message))
            return "Message is required";

        // Validate email format
        var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        if (!emailRegex.IsMatch(request.Email))
            return "Invalid email format";

        // Length validations
        if (request.Name.Length > 100)
            return "Name is too long (max 100 characters)";

        if (request.Email.Length > 100)
            return "Email is too long (max 100 characters)";

        if (request.Message.Length > 5000)
            return "Message is too long (max 5000 characters)";

        return null;
    }
    
    private async Task<string> SendEmailAsync(ContactRequest request, string recipientEmail, ILambdaContext context)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
        var subject = "You've received a form submission!";
        var textBody = $@"New contact form submission from {request.ClientId}

Name: {request.Name}
Email: {request.Email}

Message:
{request.Message}

---
Sent: {timestamp}
Client: {request.ClientId}";

        var htmlBody = $@"
<html>
<head></head>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
        <h2 style='color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 10px;'>
            New Contact Form Submission
        </h2>
        
        <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
            <p style='margin: 5px 0;'><strong>Name:</strong> {System.Web.HttpUtility.HtmlEncode(request.Name)}</p>
            <p style='margin: 5px 0;'><strong>Email:</strong> <a href='mailto:{request.Email}'>{System.Web.HttpUtility.HtmlEncode(request.Email)}</a></p>
        </div>
        
        <div style='background-color: #fff; padding: 15px; border-left: 4px solid #3498db; margin: 20px 0;'>
            <p style='margin: 0 0 10px 0;'><strong>Message:</strong></p>
            <p style='white-space: pre-wrap; margin: 0;'>{System.Web.HttpUtility.HtmlEncode(request.Message)}</p>
        </div>
        
        <hr style='border: none; border-top: 1px solid #ddd; margin: 20px 0;'>
        
        <p style='color: #7f8c8d; font-size: 12px; margin: 5px 0;'>
            Sent: {timestamp}<br>
            Client: {request.ClientId}
        </p>
    </div>
</body>
</html>";

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
            ReplyToAddresses = new List<string> { request.Email }
        };

        var response = await _sesClient.SendEmailAsync(sendRequest);
        return response.MessageId;
    }
}
