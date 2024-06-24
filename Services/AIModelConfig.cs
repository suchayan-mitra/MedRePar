using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;

namespace MedRePar.Services
{
    public class AIModelConfig
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string ApiKey { get; set; }

        public static List<AIModelConfig> LoadAIModels()
        {
            var aiModels = new List<AIModelConfig>();
            try
            {
                var section = (NameValueCollection)ConfigurationManager.GetSection("aiModelsSection");

                foreach (string key in section)
                {
                    var values = section[key].Split(';');
                    if (values.Length == 2)
                    {
                        aiModels.Add(new AIModelConfig
                        {
                            Name = key,
                            Url = values[0],
                            ApiKey = values[1]
                        });
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
