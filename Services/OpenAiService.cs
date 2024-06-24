using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace MedRePar.Services
{
    public static class OpenAiService
    {
        public static async Task<string> NormalizeParametersUsingOpenAI(string apiKey, string apiUrl, string extractedText, string model)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                    var content = new StringContent($"{{\"prompt\": \"Normalize the following medical parameters using model {model}:\\n\\n{extractedText}\", \"max_tokens\": 500}}", Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await client.PostAsync(apiUrl, content);

                    LoggingService.LogInfo("API call to OpenAI successful.");
                    return await response.Content.ReadAsStringAsync();
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
