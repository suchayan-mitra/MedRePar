using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;

namespace MedRePar.Services
{
    public abstract class AIModelConfig
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string ApiKey { get; set; }

        public abstract Dictionary<string, string> GetHeaders();
        public abstract string GetRequestBody(string extractedText, string model);

        public static List<AIModelConfig> LoadAIModels()
        {
            var aiModels = new List<AIModelConfig>();
            try
            {
                var section = (NameValueCollection)ConfigurationManager.GetSection("aiModelsSection");

                foreach (string key in section)
                {
                    var values = section[key].Split(';');
                    AIModelConfig aiModel = null;

                    if (key.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase) && values.Length >= 4)
                    {
                        aiModel = new AzureOpenAIConfig
                        {
                            Name = key,
                            Url = values[0],
                            ApiKey = values[1],
                            DeploymentId = values[2],
                            ApiVersion = values[3]
                        };
                    }
                    else if (key.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) && values.Length >= 2)
                    {
                        aiModel = new OpenAIConfig
                        {
                            Name = key,
                            Url = values[0],
                            ApiKey = values[1]
                        };
                    }

                    if (aiModel != null)
                    {
                        aiModels.Add(aiModel);
                    }
                }
                LoggingService.LogInfo("AI models loaded from configuration successfully.");
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error loading AI models from configuration", ex);
                throw;
            }

            return aiModels;
        }
    }
}
