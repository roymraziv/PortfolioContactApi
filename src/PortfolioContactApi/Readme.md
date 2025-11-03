# Portfolio Contact API

A secure AWS Lambda function that processes contact form submissions from client portfolio websites and sends notifications via Amazon SES (Simple Email Service).

## Overview

This Lambda function serves as a backend API for contact forms embedded in portfolio websites. It provides a secure, scalable solution for handling contact form submissions with features including:

- **Multi-client support**: Route emails to different recipients based on client ID
- **Security**: API key authentication and origin validation (CORS)
- **Email delivery**: Sends formatted HTML and plain text emails via Amazon SES
- **Validation**: Input validation for all form fields
- **Reply-to handling**: Sets the sender's email as the reply-to address

## Architecture

- **Runtime**: .NET 8.0
- **Trigger**: API Gateway (HTTP POST requests)
- **Email Service**: Amazon SES
- **Framework**: AWS Lambda with API Gateway Events

## Features

### 1. Multi-Client Email Routing
Each portfolio website has a unique client ID that determines where contact form submissions are sent.

### 2. Security Features
- API key authentication via `x-api-key` header
- CORS origin validation against an allowlist
- Input sanitization and validation
- Maximum length checks on all fields

### 3. Email Formatting
- Professional HTML email templates
- Plain text fallback
- Reply-to address set to the sender's email for easy responses
- Timestamp and client ID included in emails

### 4. Request Validation
- Email format validation
- Required field checking
- Length limits:
  - Name: 100 characters max
  - Email: 100 characters max
  - Message: 5,000 characters max

## Request Format

### Endpoint
```
POST /contact
```

### Headers
```
Content-Type: application/json
x-api-key: <your-api-key>
```

### Request Body
```json
{
  "clientId": "client-identifier",
  "name": "John Doe",
  "email": "john@example.com",
  "message": "Your message here"
}
```

### Response
**Success (200)**
```json
{
  "success": true,
  "message": "Email sent successfully",
  "messageId": "ses-message-id"
}
```

**Error (400/403/500)**
```json
{
  "error": "Error message"
}
```

## Environment Variables

The Lambda function requires the following environment variables:

| Variable | Description | Example |
|----------|-------------|---------|
| `API_KEY` | Secret key for API authentication | `your-secure-api-key-here` |
| `VERIFIED_SENDER_EMAIL` | SES verified sender email address | `noreply@yourdomain.com` |
| `CLIENT_EMAIL_MAPPINGS` | Comma-separated client ID to email mappings | `client1:email1@domain.com,client2:email2@domain.com` |
| `ALLOWED_ORIGINS` | Comma-separated list of allowed origins for CORS | `https://client1.com,https://client2.com` |

## AWS Configuration

### IAM Permissions Required
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "ses:SendEmail",
        "ses:SendRawEmail"
      ],
      "Resource": "*"
    },
    {
      "Effect": "Allow",
      "Action": [
        "logs:CreateLogGroup",
        "logs:CreateLogStream",
        "logs:PutLogEvents"
      ],
      "Resource": "arn:aws:logs:*:*:*"
    }
  ]
}
```

### Amazon SES Setup
1. Verify your sender email address in Amazon SES
2. If in SES sandbox, verify recipient email addresses as well
3. Request production access to remove sandbox limitations

### API Gateway Configuration
1. Create a new API Gateway REST API or HTTP API
2. Create a POST method that triggers this Lambda function
3. Enable CORS on the API Gateway
4. Deploy the API to a stage

## Deployment

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- [AWS CLI](https://aws.amazon.com/cli/) configured
- [Amazon.Lambda.Tools](https://github.com/aws/aws-extensions-for-dotnet-cli#aws-lambda-amazonlambdatools)

### Install Lambda Tools
```bash
dotnet tool install -g Amazon.Lambda.Tools
```

Or update if already installed:
```bash
dotnet tool update -g Amazon.Lambda.Tools
```

### Deploy from Command Line
```bash
cd src/PortfolioContactApi
dotnet lambda deploy-function
```

### Deploy from Visual Studio
1. Right-click the project in Solution Explorer
2. Select **Publish to AWS Lambda**
3. Follow the deployment wizard

### Build for Production
```bash
dotnet publish -c Release
```

## Project Structure

```
PortfolioContactApi/
├── Function.cs                      # Main Lambda handler
├── ContactRequest.cs                # Request model
├── MyFolioVisionContactRequest.cs   # Alternative model (if needed)
├── PortfolioContactApi.csproj       # Project configuration
├── aws-lambda-tools-defaults.json   # Lambda deployment settings
└── Readme.md                        # This file
```

## Dependencies

- **Amazon.Lambda.Core** (2.7.0) - AWS Lambda runtime
- **Amazon.Lambda.APIGatewayEvents** (2.7.1) - API Gateway integration
- **Amazon.Lambda.Serialization.SystemTextJson** (2.4.4) - JSON serialization
- **AWSSDK.SimpleEmail** (4.0.2) - Amazon SES client
- **System.Text.Json** (9.0.10) - JSON processing

## Example Frontend Integration

```javascript
async function submitContactForm(formData) {
  try {
    const response = await fetch('https://your-api-gateway-url/contact', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'x-api-key': 'your-api-key'
      },
      body: JSON.stringify({
        clientId: 'your-client-id',
        name: formData.name,
        email: formData.email,
        message: formData.message
      })
    });

    const result = await response.json();
    
    if (response.ok) {
      console.log('Email sent successfully:', result.messageId);
      return true;
    } else {
      console.error('Error:', result.error);
      return false;
    }
  } catch (error) {
    console.error('Network error:', error);
    return false;
  }
}
```

## Monitoring and Logs

View Lambda execution logs in CloudWatch:
```bash
aws logs tail /aws/lambda/PortfolioContactApi --follow
```

## Error Handling

The function handles various error scenarios:
- **400 Bad Request**: Invalid or missing fields
- **403 Forbidden**: Invalid API key or unauthorized origin
- **500 Internal Server Error**: SES delivery failure or unexpected errors

All errors are logged to CloudWatch for troubleshooting.

## Performance

- **Memory**: 512 MB (configurable in `aws-lambda-tools-defaults.json`)
- **Timeout**: 30 seconds
- **Architecture**: x86_64
- **Cold Start Optimization**: PublishReadyToRun enabled

## Security Best Practices

1. **Never commit API keys** to version control
2. Store API keys in AWS Secrets Manager or Parameter Store
3. Rotate API keys regularly
4. Keep the allowed origins list as restrictive as possible
5. Monitor CloudWatch logs for suspicious activity
6. Use AWS WAF if you need rate limiting or additional protection

## License

This project is proprietary and intended for use with client portfolio websites.

## Support

For issues or questions, contact the development team.
