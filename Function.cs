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
        _apiKey = Environment.GetEnvironmentVariable("API_KEY");
        _verifiedSender = Environment.GetEnvironmentVariable("VERIFIED_SENDER_EMAIL");
        var clientMappings = Environment.GetEnvironmentVariable("CLIENT_EMAIL_MAPPINGS");
        _clientEmails = ParseClientEmails(clientMappings);
        var allowedOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS");
        _allowedOrigins = allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(o => o.Trim()).ToHashSet();
    }

    public Function(IAmazonSimpleEmailService sesClient)
    {
        _sesClient = sesClient;
        _apiKey = Environment.GetEnvironmentVariable("API_KEY");
        _verifiedSender = Environment.GetEnvironmentVariable("VERIFIED_SENDER_EMAIL");
        var clientMappings = Environment.GetEnvironmentVariable("CLIENT_EMAIL_MAPPINGS");
        _clientEmails = ParseClientEmails(clientMappings);
        var allowedOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS");
        _allowedOrigins = allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(o => o.Trim()).ToHashSet();
    }

    private Dictionary<string, string> ParseClientEmails(string mappings)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(mappings)) return result;
        
        var pairs = mappings.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split(':');
            if (parts.Length != 2) continue;
            result.Add(parts[0], parts[1]);
        }
        return result;
    }
    
    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        // Get origin for CORS
        var origin = request.Headers.ContainsKey("origin") 
            ? request.Headers["origin"] 
            : request.Headers.ContainsKey("Origin") 
                ? request.Headers["Origin"] 
                : "";

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
        if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return CreateResponse(200, new { message = "OK" }, headers);
        }

        try
        {
            // Validate API key
            if (!ValidateApiKey(request.Headers))
            {
                context.Logger.LogWarning("Invalid API key attempt");
                return CreateResponse(403, new { error = "Forbidden: Invalid API key" }, headers);
            }

            // Validate origin
            if (!IsOriginAllowed(origin))
            {
                context.Logger.LogWarning($"Request from unauthorized origin: {origin}");
                return CreateResponse(403, new { error = "Forbidden: Unauthorized origin" }, headers);
            }

            // Parse request body
            var body = JsonSerializer.Deserialize<ContactRequest>(request.Body, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            if (body == null)
            {
                return CreateResponse(400, new { error = "Invalid request body" }, headers);
            }

            // Validate required fields
            var validationError = ValidateContactRequest(body);
            if (validationError != null)
            {
                return CreateResponse(400, new { error = validationError }, headers);
            }

            // Get recipient email from client ID
            if (!_clientEmails.TryGetValue(body.ClientId, out var recipientEmail))
            {
                context.Logger.LogWarning($"Unknown client ID: {body.ClientId}");
                return CreateResponse(400, new { error = "Invalid client ID" }, headers);
            }

            // Send email
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
