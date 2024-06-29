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

namespace MedRePar.Services
{
    public static class OpenAiService
    {
        public static async Task<Dictionary<string, string>> NormalizeParametersUsingOpenAI(AIModelConfig modelConfig, string extractedText)
        {
            try
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
                    throw new NotSupportedException("Unsupported AI model configuration.");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error calling AI API", ex);
                throw;
            }
        }

        private static string GetPrompt(string extractedText)
        {
            return (
                "Extract the health parameters and their values along with the date from the following report. "
                + "The parameters might be reported on multiple pages, including summary reports and individual categories. "
                + "Ensure that you share unique parameters and group related parameters properly (e.g., Lipid Profile should have LDL, HDL, etc. under it). "
                + "The returned report should be in a consistent JSON format with a single date for the entire report.\n\n"
                + "Input Report:\n"
                + $"{extractedText}\n\n"
                + "Please provide the data in the following JSON format:\n"
                + "{\n"
                + "    \"Date\": \"yyyy-mm-dd\",\n"
                + "    \"Parameters\": {\n"
                + "        \"Lipid Profile\": {\n"
                + "            \"LDL\": \"Value\",\n"
                + "            \"HDL\": \"Value\"\n"
                + "        },\n"
                + "        \"Complete Blood Count\": {\n"
                + "            \"Hemoglobin\": \"Value\",\n"
                + "            \"White Blood Cells\": \"Value\"\n"
                + "        }\n"
                + "        ...\n"
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

                ChatCompletion completion = chatClient.CompleteChat([
                    new SystemChatMessage("You are a helpful assistant."),
                    new UserChatMessage(prompt)
                    ]);

                var data = new Dictionary<string, string>();
                foreach (var choice in completion.Content)
                {
                    data["NormalizedParameters"] = choice.Text;
                }

                return data;
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
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");

                    var content = new StringContent(config.GetRequestBody(prompt, config.Name), Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await client.PostAsync(config.Url, content);
                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();
                    LoggingService.LogInfo($"API Response: {responseBody}");

                    JObject jsonResponse = JObject.Parse(responseBody);
                    var data = new Dictionary<string, string>();
                    foreach (var item in jsonResponse)
                    {
                        data[item.Key] = item.Value.ToString();
                    }

                    return data;
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error calling OpenAI API", ex);
                throw;
            }
        }
    }
}
