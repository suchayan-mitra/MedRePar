using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MedRePar.Services
{
    internal class NormalizationService
    {
        public static Dictionary<string, string> ParseNormalizedData(string responseText)
        {
            var normalizedData = new Dictionary<string, string>();

            // Assuming the response is a JSON object containing normalized data
            JObject responseJson = JObject.Parse(responseText);
            foreach (var item in responseJson)
            {
                normalizedData[item.Key] = item.Value.ToString();
            }

            return normalizedData;
        }
    }
}
