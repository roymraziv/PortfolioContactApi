using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PortfolioContactApi
{
    public class RateLimiter
    {
        private readonly IAmazonDynamoDB _dynamoClient;
        private readonly string _tableName;
        private readonly int _maxRequestsPerHour;
        private readonly int _maxEmailsPerDay;

        public RateLimiter(string tableName, int maxRequestsPerHour = 10, int maxEmailsPerDay = 20)
        {
            _dynamoClient = new AmazonDynamoDBClient();
            _tableName = tableName;
            _maxRequestsPerHour = maxRequestsPerHour;
            _maxEmailsPerDay = maxEmailsPerDay;
        }

        // For testing - allow dependency injection
        public RateLimiter(IAmazonDynamoDB dynamoClient, string tableName, int maxRequestsPerHour = 10, int maxEmailsPerDay = 20)
        {
            _dynamoClient = dynamoClient;
            _tableName = tableName;
            _maxRequestsPerHour = maxRequestsPerHour;
            _maxEmailsPerDay = maxEmailsPerDay;
        }

        /// <summary>
        /// Check if an IP address has exceeded the rate limit
        /// </summary>
        /// <param name="ipAddress">IP address to check</param>
        /// <returns>True if allowed, false if rate limited</returns>
        public async Task<bool> CheckIpRateLimitAsync(string ipAddress)
        {
            if (string.IsNullOrEmpty(_tableName))
                return true; // No rate limiting if table not configured

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var oneHourAgo = now - 3600;
            var key = $"ip:{ipAddress}";

            try
            {
                // Get existing item
                var getResponse = await _dynamoClient.GetItemAsync(new GetItemRequest
                {
                    TableName = _tableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        { "pk", new AttributeValue { S = key } }
                    }
                });

                List<long> timestamps;

                if (getResponse.Item != null && getResponse.Item.Count > 0)
                {
                    // Parse existing timestamps
                    timestamps = getResponse.Item.ContainsKey("timestamps")
                        ? getResponse.Item["timestamps"].L
                            .Select(av => long.Parse(av.N))
                            .Where(ts => ts > oneHourAgo) // Filter old timestamps
                            .ToList()
                        : new List<long>();

                    // Check if rate limit exceeded
                    if (timestamps.Count >= _maxRequestsPerHour)
                    {
                        return false; // Rate limit exceeded
                    }
                }
                else
                {
                    timestamps = new List<long>();
                }

                // Add current timestamp
                timestamps.Add(now);

                // Update DynamoDB
                await _dynamoClient.PutItemAsync(new PutItemRequest
                {
                    TableName = _tableName,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        { "pk", new AttributeValue { S = key } },
                        { "timestamps", new AttributeValue 
                            { 
                                L = timestamps.Select(ts => new AttributeValue { N = ts.ToString() }).ToList() 
                            } 
                        },
                        { "ttl", new AttributeValue { N = (now + 7200).ToString() } } // Clean up after 2 hours
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Rate limit check error: {ex.Message}");
                // Fail open - allow request if DynamoDB has issues
                return true;
            }
        }

        /// <summary>
        /// Check if a client has exceeded daily email limit
        /// </summary>
        /// <param name="clientId">Client identifier</param>
        /// <returns>True if allowed, false if rate limited</returns>
        public async Task<bool> CheckEmailRateLimitAsync(string clientId)
        {
            if (string.IsNullOrEmpty(_tableName))
                return true; // No rate limiting if table not configured

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var oneDayAgo = now - 86400; // 24 hours
            var key = $"email:{clientId}";

            try
            {
                // Get existing item
                var getResponse = await _dynamoClient.GetItemAsync(new GetItemRequest
                {
                    TableName = _tableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        { "pk", new AttributeValue { S = key } }
                    }
                });

                List<long> timestamps;

                if (getResponse.Item != null && getResponse.Item.Count > 0)
                {
                    // Parse existing timestamps
                    timestamps = getResponse.Item.ContainsKey("timestamps")
                        ? getResponse.Item["timestamps"].L
                            .Select(av => long.Parse(av.N))
                            .Where(ts => ts > oneDayAgo) // Filter old timestamps
                            .ToList()
                        : new List<long>();

                    // Check if rate limit exceeded
                    if (timestamps.Count >= _maxEmailsPerDay)
                    {
                        return false; // Rate limit exceeded
                    }
                }
                else
                {
                    timestamps = new List<long>();
                }

                // Add current timestamp
                timestamps.Add(now);

                // Update DynamoDB
                await _dynamoClient.PutItemAsync(new PutItemRequest
                {
                    TableName = _tableName,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        { "pk", new AttributeValue { S = key } },
                        { "timestamps", new AttributeValue 
                            { 
                                L = timestamps.Select(ts => new AttributeValue { N = ts.ToString() }).ToList() 
                            } 
                        },
                        { "ttl", new AttributeValue { N = (now + 172800).ToString() } } // Clean up after 2 days
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Email rate limit check error: {ex.Message}");
                // Fail open - allow request if DynamoDB has issues
                return true;
            }
        }

        /// <summary>
        /// Get remaining requests for an IP address
        /// </summary>
        public async Task<int> GetRemainingRequestsAsync(string ipAddress)
        {
            if (string.IsNullOrEmpty(_tableName))
                return _maxRequestsPerHour;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var oneHourAgo = now - 3600;
            var key = $"ip:{ipAddress}";

            try
            {
                var getResponse = await _dynamoClient.GetItemAsync(new GetItemRequest
                {
                    TableName = _tableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        { "pk", new AttributeValue { S = key } }
                    }
                });

                if (getResponse.Item == null || !getResponse.Item.ContainsKey("timestamps"))
                {
                    return _maxRequestsPerHour;
                }

                var recentTimestamps = getResponse.Item["timestamps"].L
                    .Select(av => long.Parse(av.N))
                    .Count(ts => ts > oneHourAgo);

                return Math.Max(0, _maxRequestsPerHour - recentTimestamps);
            }
            catch
            {
                return _maxRequestsPerHour;
            }
        }
    }
}
