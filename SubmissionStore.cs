using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using PortfolioContactApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PortfolioContactApi
{
    public class SubmissionStore
    {
        private readonly IAmazonDynamoDB _dynamoClient;
        private readonly string _tableName;

        public SubmissionStore(string tableName)
        {
            _dynamoClient = new AmazonDynamoDBClient();
            _tableName = tableName;
        }

        // For testing - allow dependency injection
        public SubmissionStore(IAmazonDynamoDB dynamoClient, string tableName)
        {
            _dynamoClient = dynamoClient;
            _tableName = tableName;
        }

        /// <summary>
        /// Permanently store a form submission with all details
        /// </summary>
        /// <param name="request">The form request data</param>
        /// <param name="ipAddress">Client IP address</param>
        /// <param name="messageId">SES message ID (if email was sent)</param>
        /// <returns>The submission ID</returns>
        public async Task<string> StoreSubmissionAsync(IFormRequest request, string ipAddress, string? messageId = null)
        {
            if (string.IsNullOrEmpty(_tableName))
            {
                Console.WriteLine("Warning: Submission store table not configured. Skipping storage.");
                return string.Empty;
            }

            var submissionId = Guid.NewGuid().ToString();
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var key = $"submission:{submissionId}";

            try
            {
                var formData = request.GetFormData();
                var item = new Dictionary<string, AttributeValue>
                {
                    { "pk", new AttributeValue { S = key } },
                    { "submissionId", new AttributeValue { S = submissionId } },
                    { "timestamp", new AttributeValue { N = timestamp.ToString() } },
                    { "timestampIso", new AttributeValue { S = DateTimeOffset.UtcNow.ToString("o") } },
                    { "ipAddress", new AttributeValue { S = ipAddress } },
                    { "clientId", new AttributeValue { S = request.ClientId } },
                    { "formType", new AttributeValue { S = request.GetFormType() } }
                };

                // Add SES message ID if provided
                if (!string.IsNullOrEmpty(messageId))
                {
                    item.Add("sesMessageId", new AttributeValue { S = messageId });
                }

                // Store all form fields as individual attributes
                foreach (var (key_name, value) in formData)
                {
                    var attributeName = $"field_{key_name}";
                    item.Add(attributeName, new AttributeValue { S = value ?? "" });
                }

                await _dynamoClient.PutItemAsync(new PutItemRequest
                {
                    TableName = _tableName,
                    Item = item
                });

                Console.WriteLine($"Submission stored successfully: {submissionId}");
                return submissionId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error storing submission: {ex.Message}");
                // Don't fail the request if storage fails - just log the error
                return string.Empty;
            }
        }

        /// <summary>
        /// Retrieve a submission by ID
        /// </summary>
        public async Task<Dictionary<string, AttributeValue>?> GetSubmissionAsync(string submissionId)
        {
            if (string.IsNullOrEmpty(_tableName))
                return null;

            try
            {
                var response = await _dynamoClient.GetItemAsync(new GetItemRequest
                {
                    TableName = _tableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        { "pk", new AttributeValue { S = $"submission:{submissionId}" } }
                    }
                });

                return response.Item.Count > 0 ? response.Item : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving submission: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Query submissions by client ID (requires GSI on clientId)
        /// </summary>
        public async Task<List<Dictionary<string, AttributeValue>>> GetSubmissionsByClientAsync(string clientId, int limit = 100)
        {
            if (string.IsNullOrEmpty(_tableName))
                return new List<Dictionary<string, AttributeValue>>();

            try
            {
                var response = await _dynamoClient.QueryAsync(new QueryRequest
                {
                    TableName = _tableName,
                    IndexName = "clientId-timestamp-index", // You'll need to create this GSI
                    KeyConditionExpression = "clientId = :clientId",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":clientId", new AttributeValue { S = clientId } }
                    },
                    ScanIndexForward = false, // Most recent first
                    Limit = limit
                });

                return response.Items;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error querying submissions by client: {ex.Message}");
                return new List<Dictionary<string, AttributeValue>>();
            }
        }
    }
}
