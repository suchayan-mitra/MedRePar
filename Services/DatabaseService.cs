using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Newtonsoft.Json.Linq;

namespace MedRePar.Services
{
    internal class DatabaseService
    {
        public static void InitializeDb(string dbPath)
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    string sql = @"CREATE TABLE IF NOT EXISTS medical_data (
                                    id INTEGER PRIMARY KEY,
                                    parameter TEXT,
                                    value TEXT,
                                    date TEXT,
                                    run_id TEXT)";
                    SQLiteCommand command = new SQLiteCommand(sql, conn);
                    command.ExecuteNonQuery();
                    LoggingService.LogInfo("Database table 'medical_data' created or already exists.");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error initializing database", ex);
                throw;
            }
        }

        public static void StoreData(string dbPath, Dictionary<string, string> data, string date, string runId)
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    if (data.TryGetValue("NormalizedParameters", out string jsonString))
                    {
                        LoggingService.LogInfo($"Original JSON String: {jsonString}");
                        jsonString = ExtractJsonContent(jsonString); // Extract the JSON content
                        LoggingService.LogInfo($"Extracted JSON Content: {jsonString}");
                        var parsedData = JObject.Parse(jsonString);
                        string reportDate = parsedData["Date"].ToString();

                        foreach (var category in parsedData["Parameters"])
                        {
                            foreach (var item in category.Children<JProperty>())
                            {
                                string sql = "INSERT INTO medical_data (parameter, value, date, run_id) VALUES (@parameter, @value, @date, @run_id)";
                                SQLiteCommand command = new SQLiteCommand(sql, conn);
                                command.Parameters.AddWithValue("@parameter", $"{category.Path} - {item.Name}");
                                command.Parameters.AddWithValue("@value", item.Value.ToString());
                                command.Parameters.AddWithValue("@date", reportDate);
                                command.Parameters.AddWithValue("@run_id", runId);
                                command.ExecuteNonQuery();
                            }
                        }
                    }
                    LoggingService.LogInfo("Data stored in database successfully.");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error storing data in database", ex);
                throw;
            }
        }

        private static string ExtractJsonContent(string jsonString)
        {
            // Find the start and end of the JSON content within the string
            int startIndex = jsonString.IndexOf("{");
            int endIndex = jsonString.LastIndexOf("}") + 1;

            // Extract and return the JSON content
            return jsonString.Substring(startIndex, endIndex - startIndex);
        }
    }
}
