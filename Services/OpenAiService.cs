using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Newtonsoft.Json.Linq;
using OpenAI.Chat;
using OpenAI;
using Polly;
using Polly.Retry;

namespace MedRePar.Services
{
    public static class OpenAiService
    {
        private static readonly AsyncRetryPolicy _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<RequestFailedException>(ex => ex.Status == 429 || ex.Status >= 500)
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                retryCount: 4,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    LoggingService.LogWarning($"AI API retry {retryCount} after {timeSpan.TotalSeconds}s due to: {exception.Message}");
                });

        public static async Task<Dictionary<string, string>> NormalizeParametersUsingOpenAI(AIModelConfig modelConfig, string extractedText)
        {
            if (modelConfig == null)
            {
                throw new ArgumentNullException(nameof(modelConfig));
            }

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                throw new ArgumentException("Extracted text cannot be empty", nameof(extractedText));
            }

            try
            {
                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    string prompt = GetPrompt(extractedText);

                    if (modelConfig is AzureOpenAIConfig azureConfig)
                    {
                        return await CallAzureOpenAI(azureConfig, prompt);
                    }
                    else if (modelConfig is OpenAIConfig openAIConfig)
                    {
                        return await CallOpenAI(openAIConfig, prompt);
                    }
                    else
                    {
                        throw new NotSupportedException($"Unsupported AI model configuration type: {modelConfig.GetType().Name}");
                    }
                });
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error calling AI API after all retries", ex);
                throw;
            }
        }

        private static string GetPrompt(string extractedText)
        {
            return (
                "You are a medical data extraction assistant. Extract health parameters and their values along with the date from the following medical report. "
                + "The parameters might be reported on multiple pages, including summary reports and individual categories. "
                + "Ensure that you:\n"
                + "1. Share only UNIQUE parameters (no duplicates)\n"
                + "2. Group related parameters properly (e.g., 'Lipid Profile' should have LDL, HDL, Triglycerides, Total Cholesterol under it)\n"
                + "3. Use standard medical category names (e.g., 'Complete Blood Count', 'Lipid Profile', 'Liver Function Test', etc.)\n"
                + "4. Extract the REPORT DATE (not collection date or received date)\n"
                + "5. Include units with values when available (e.g., '150 mg/dL' not just '150')\n"
                + "6. Preserve reference ranges if present (e.g., '150 mg/dL (70-100)')\n\n"
                + "Input Report:\n"
                + $"{extractedText}\n\n"
                + "IMPORTANT: Return ONLY valid JSON in the exact format below. Do not include any explanatory text before or after the JSON.\n\n"
                + "Required JSON format:\n"
                + "{\n"
                + "    \"Date\": \"yyyy-mm-dd\",\n"
                + "    \"Parameters\": {\n"
                + "        \"Lipid Profile\": {\n"
                + "            \"LDL Cholesterol\": \"150 mg/dL (70-100)\",\n"
                + "            \"HDL Cholesterol\": \"45 mg/dL (>40)\",\n"
                + "            \"Triglycerides\": \"200 mg/dL (0-150)\",\n"
                + "            \"Total Cholesterol\": \"220 mg/dL (0-200)\"\n"
                + "        },\n"
                + "        \"Complete Blood Count\": {\n"
                + "            \"Hemoglobin\": \"14.5 g/dL (13-17)\",\n"
                + "            \"White Blood Cells\": \"7500 /cumm (4000-11000)\"\n"
                + "        }\n"
                + "    }\n"
                + "}"
            );
        }

        private static async Task<Dictionary<string, string>> CallAzureOpenAI(AzureOpenAIConfig config, string prompt)
        {
            try
            {
                var azureClient = new AzureOpenAIClient(new Uri(config.Url), new AzureKeyCredential(config.ApiKey));
                ChatClient chatClient = azureClient.GetChatClient(config.DeploymentId);

                var chatMessages = new ChatMessage[]
                {
                    new SystemChatMessage("You are a medical data extraction assistant specialized in parsing medical reports. Always return valid JSON."),
                    new UserChatMessage(prompt)
                };

                ChatCompletionOptions options = new ChatCompletionOptions
                {
                    Temperature = 0.1f, // Low temperature for consistent, factual extraction
                    MaxTokens = 4000,
                    ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat() // Force JSON output
                };

                ChatCompletion completion = await chatClient.CompleteChatAsync(chatMessages, options);

                var data = new Dictionary<string, string>();
                foreach (var choice in completion.Content)
                {
                    data["NormalizedParameters"] = choice.Text;
                }

                // Validate that we got valid JSON
                if (data.ContainsKey("NormalizedParameters"))
                {
                    try
                    {
                        JObject.Parse(data["NormalizedParameters"]); // Validate JSON
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError("Azure OpenAI returned invalid JSON", ex);
                        throw new InvalidOperationException("AI model returned invalid JSON format", ex);
                    }
                }

                return data;
            }
            catch (RequestFailedException ex)
            {
                LoggingService.LogError($"Azure OpenAI API error (Status: {ex.Status})", ex);
                throw;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error calling Azure OpenAI API", ex);
                throw;
            }
        }

        private static async Task<Dictionary<string, string>> CallOpenAI(OpenAIConfig config, string prompt)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(2); // Set reasonable timeout
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");

                    var content = new StringContent(config.GetRequestBody(prompt, config.Name), Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await client.PostAsync(config.Url, content);
                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();
                    LoggingService.LogDebug($"OpenAI API Response: {responseBody}");

                    JObject jsonResponse = JObject.Parse(responseBody);

                    // Extract the actual content from OpenAI response structure
                    var data = new Dictionary<string, string>();

                    // OpenAI API returns: {choices: [{message: {content: "..."}}]}
                    if (jsonResponse["choices"] != null && jsonResponse["choices"].HasValues)
                    {
                        var firstChoice = jsonResponse["choices"][0];
                        var messageContent = firstChoice["message"]?["content"]?.ToString();

                        if (!string.IsNullOrWhiteSpace(messageContent))
                        {
                            data["NormalizedParameters"] = messageContent;

                            // Validate JSON
                            try
                            {
                                JObject.Parse(messageContent);
                            }
                            catch (Exception ex)
                            {
                                LoggingService.LogError("OpenAI returned invalid JSON", ex);
                                throw new InvalidOperationException("AI model returned invalid JSON format", ex);
                            }
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("OpenAI API response missing expected 'choices' structure");
                    }

                    return data;
                }
            }
            catch (HttpRequestException ex)
            {
                LoggingService.LogError($"Network error calling OpenAI API: {ex.Message}", ex);
                throw;
            }
            catch (TaskCanceledException ex)
            {
                LoggingService.LogError("OpenAI API request timeout", ex);
                throw;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error calling OpenAI API", ex);
                throw;
            }
        }
    }
}
