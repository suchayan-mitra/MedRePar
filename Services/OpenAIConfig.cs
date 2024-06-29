using System;
using System.Collections.Generic;

namespace MedRePar.Services
{
    public class OpenAIConfig : AIModelConfig
    {
        public override Dictionary<string, string> GetHeaders()
        {
            return new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {ApiKey}" }
            };
        }

        public override string GetRequestBody(string extractedText, string model)
        {
            return $"{{\"prompt\": \"Normalize the following medical parameters using model {model}:\\n\\n{extractedText}\", \"max_tokens\": 500}}";
        }
    }
}
