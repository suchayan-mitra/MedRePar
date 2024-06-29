using System;
using System.Collections.Generic;

namespace MedRePar.Services
{
    public class AzureOpenAIConfig : AIModelConfig
    {
        public string DeploymentId { get; set; }
        public string ApiVersion { get; set; }

        public override Dictionary<string, string> GetHeaders()
        {
            return new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {ApiKey}" }
            };
        }

        public override string GetRequestBody(string extractedText, string model)
        {
            return $"{{\"messages\": [{{\"role\": \"system\", \"content\": \"You are a helpful assistant.\"}}, {{\"role\": \"user\", \"content\": \"Normalize the following medical parameters using model {model}:\\n\\n{extractedText}\"}}]}}";
        }
    }
}
