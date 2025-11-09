using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
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

                    string sqlCategories = @"CREATE TABLE IF NOT EXISTS categories (
                                                id INTEGER PRIMARY KEY,
                                                name TEXT UNIQUE)";
                    SQLiteCommand commandCategories = new SQLiteCommand(sqlCategories, conn);
                    commandCategories.ExecuteNonQuery();

                    string sqlParameters = @"CREATE TABLE IF NOT EXISTS parameters (
                                                id INTEGER PRIMARY KEY,
                                                category_id INTEGER,
                                                name TEXT,
                                                alias TEXT,
                                                UNIQUE(category_id, name))";
                    SQLiteCommand commandParameters = new SQLiteCommand(sqlParameters, conn);
                    commandParameters.ExecuteNonQuery();

                    string sqlMedicalData = @"CREATE TABLE IF NOT EXISTS medical_data (
                                                id INTEGER PRIMARY KEY,
                                                parameter_id INTEGER,
                                                value TEXT,
                                                date TEXT,
                                                run_id TEXT,
                                                created_at DATETIME DEFAULT CURRENT_TIMESTAMP)";
                    SQLiteCommand commandMedicalData = new SQLiteCommand(sqlMedicalData, conn);
                    commandMedicalData.ExecuteNonQuery();

                    // Create indices for better query performance
                    string indexRunId = "CREATE INDEX IF NOT EXISTS idx_run_id ON medical_data(run_id)";
                    SQLiteCommand commandIndexRunId = new SQLiteCommand(indexRunId, conn);
                    commandIndexRunId.ExecuteNonQuery();

                    string indexDate = "CREATE INDEX IF NOT EXISTS idx_date ON medical_data(date)";
                    SQLiteCommand commandIndexDate = new SQLiteCommand(indexDate, conn);
                    commandIndexDate.ExecuteNonQuery();

                    LoggingService.LogInfo("Database tables and indices created or already exist.");
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
            if (data == null || !data.ContainsKey("NormalizedParameters"))
            {
                throw new ArgumentException("Data dictionary must contain 'NormalizedParameters' key");
            }

            if (string.IsNullOrWhiteSpace(runId))
            {
                throw new ArgumentException("Run ID cannot be empty");
            }

            SQLiteConnection? conn = null;
            SQLiteTransaction? transaction = null;

            try
            {
                conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
                conn.Open();

                // Start transaction for data integrity
                transaction = conn.BeginTransaction();

                string jsonString = data["NormalizedParameters"];
                LoggingService.LogInfo($"Original JSON String: {jsonString}");

                // Extract and validate JSON
                jsonString = ExtractJsonContent(jsonString);
                LoggingService.LogInfo($"Extracted JSON Content: {jsonString}");

                JObject parsedData;
                try
                {
                    parsedData = JObject.Parse(jsonString);
                }
                catch (JsonException ex)
                {
                    LoggingService.LogError("Invalid JSON format received from AI model", ex);
                    throw new InvalidOperationException("Failed to parse AI model response as JSON. The response may be malformed.", ex);
                }

                // Validate required fields
                if (parsedData["Date"] == null)
                {
                    throw new InvalidOperationException("JSON response missing required 'Date' field");
                }

                if (parsedData["Parameters"] == null)
                {
                    throw new InvalidOperationException("JSON response missing required 'Parameters' field");
                }

                string reportDate = parsedData["Date"]?.ToString() ?? string.Empty;
                LoggingService.LogInfo($"Report Date: {reportDate}");

                // Validate date format
                if (!DateTime.TryParse(reportDate, out _))
                {
                    LoggingService.LogWarning($"Invalid date format: {reportDate}. Using current date instead.");
                    reportDate = DateTime.Now.ToString("yyyy-MM-dd");
                }

                int parametersProcessed = 0;

                foreach (var category in parsedData["Parameters"]!)
                {
                    if (category is not JProperty categoryProp)
                    {
                        continue;
                    }

                    string categoryName = categoryProp.Name;

                    if (string.IsNullOrWhiteSpace(categoryName))
                    {
                        LoggingService.LogWarning("Skipping category with empty name");
                        continue;
                    }

                    int categoryId = GetCategoryId(conn, categoryName);

                    foreach (var item in categoryProp.Value.Children<JProperty>())
                    {
                        string parameterName = item.Name;
                        string value = item.Value?.ToString() ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(parameterName))
                        {
                            LoggingService.LogWarning($"Skipping parameter with empty name in category '{categoryName}'");
                            continue;
                        }

                        string normalizedName = NormalizeParameterName(parameterName);
                        int parameterId = GetParameterId(conn, categoryId, normalizedName, parameterName);

                        // Insert medical data
                        string sql = "INSERT INTO medical_data (parameter_id, value, date, run_id) VALUES (@parameter_id, @value, @date, @run_id)";
                        SQLiteCommand command = new SQLiteCommand(sql, conn, transaction);
                        command.Parameters.AddWithValue("@parameter_id", parameterId);
                        command.Parameters.AddWithValue("@value", value);
                        command.Parameters.AddWithValue("@date", reportDate);
                        command.Parameters.AddWithValue("@run_id", runId);

                        int rowsAffected = command.ExecuteNonQuery();
                        parametersProcessed++;

                        LoggingService.LogInfo($"Inserted: Category='{categoryName}', Parameter='{parameterName}', Value='{value}', Rows Affected: {rowsAffected}");
                    }
                }

                // Commit transaction
                transaction.Commit();
                LoggingService.LogInfo($"Successfully stored {parametersProcessed} parameters in database for run ID: {runId}");
            }
            catch (Exception ex)
            {
                // Rollback on error
                try
                {
                    transaction?.Rollback();
                    LoggingService.LogWarning("Transaction rolled back due to error");
                }
                catch (Exception rollbackEx)
                {
                    LoggingService.LogError("Error during transaction rollback", rollbackEx);
                }

                LoggingService.LogError("Error storing data in database", ex);
                throw;
            }
            finally
            {
                transaction?.Dispose();
                conn?.Close();
                conn?.Dispose();
            }
        }

        private static string NormalizeParameterName(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return string.Empty;
            }

            // Remove special characters but preserve alphanumeric and spaces
            string normalized = parameterName.ToLower()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "")
                .Replace(":", "")
                .Replace(".", "")
                .Replace("/", "");

            // Remove any remaining non-alphanumeric characters
            normalized = Regex.Replace(normalized, @"[^a-z0-9]", "");

            return normalized;
        }

        private static int GetCategoryId(SQLiteConnection conn, string categoryName)
        {
            // Check if the category already exists
            string sql = "SELECT id FROM categories WHERE name = @name";
            SQLiteCommand command = new SQLiteCommand(sql, conn);
            command.Parameters.AddWithValue("@name", categoryName);
            object? result = command.ExecuteScalar();

            if (result != null)
            {
                return Convert.ToInt32(result);
            }
            else
            {
                // Insert new category
                string insertSql = "INSERT INTO categories (name) VALUES (@name); SELECT last_insert_rowid()";
                SQLiteCommand insertCommand = new SQLiteCommand(insertSql, conn);
                insertCommand.Parameters.AddWithValue("@name", categoryName);
                return Convert.ToInt32(insertCommand.ExecuteScalar());
            }
        }

        private static int GetParameterId(SQLiteConnection conn, int categoryId, string parameterName, string aliasName)
        {
            // Normalize the parameter name
            parameterName = NormalizeParameterName(parameterName);

            // Check if the parameter already exists
            string sql = "SELECT id FROM parameters WHERE category_id = @category_id AND name = @name";
            SQLiteCommand command = new SQLiteCommand(sql, conn);
            command.Parameters.AddWithValue("@category_id", categoryId);
            command.Parameters.AddWithValue("@name", parameterName);
            object? result = command.ExecuteScalar();

            if (result != null)
            {
                return Convert.ToInt32(result);
            }
            else
            {
                // Insert new parameter
                string insertSql = "INSERT INTO parameters (category_id, name, alias) VALUES (@category_id, @name, @alias); SELECT last_insert_rowid()";
                SQLiteCommand insertCommand = new SQLiteCommand(insertSql, conn);
                insertCommand.Parameters.AddWithValue("@category_id", categoryId);
                insertCommand.Parameters.AddWithValue("@name", parameterName);
                insertCommand.Parameters.AddWithValue("@alias", aliasName);
                return Convert.ToInt32(insertCommand.ExecuteScalar());
            }
        }

        public static void PrintTableStructure(string dbPath)
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();

                    string[] tables = { "categories", "parameters", "medical_data" };

                    foreach (string tableName in tables)
                    {
                        string sql = $"PRAGMA table_info({tableName})";
                        SQLiteCommand command = new SQLiteCommand(sql, conn);
                        SQLiteDataReader reader = command.ExecuteReader();

                        LoggingService.LogInfo($"Table Structure for '{tableName}':");
                        while (reader.Read())
                        {
                            LoggingService.LogInfo($"  Column: {reader["name"]}, Type: {reader["type"]}");
                        }
                        reader.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error printing table structure", ex);
                throw;
            }
        }

        public static void PrintAllTableData(string dbPath)
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    string sql = @"SELECT
                                    medical_data.id,
                                    categories.name as category,
                                    parameters.name as parameter,
                                    parameters.alias as alias,
                                    medical_data.value,
                                    medical_data.date,
                                    medical_data.run_id
                                   FROM medical_data
                                   INNER JOIN parameters ON medical_data.parameter_id = parameters.id
                                   INNER JOIN categories ON parameters.category_id = categories.id
                                   ORDER BY medical_data.date DESC, categories.name, parameters.name
                                   LIMIT 100";

                    SQLiteCommand command = new SQLiteCommand(sql, conn);
                    SQLiteDataReader reader = command.ExecuteReader();

                    LoggingService.LogInfo("Recent Data from 'medical_data' (Last 100 entries):");

                    int count = 0;
                    while (reader.Read())
                    {
                        LoggingService.LogInfo($"ID: {reader["id"]}, Category: {reader["category"]}, Parameter: {reader["parameter"]}, Alias: {reader["alias"]}, Value: {reader["value"]}, Date: {reader["date"]}, Run ID: {reader["run_id"]}");
                        count++;
                    }

                    LoggingService.LogInfo($"Total records displayed: {count}");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error printing table data", ex);
                throw;
            }
        }

        private static string ExtractJsonContent(string jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                throw new ArgumentException("JSON string cannot be empty");
            }

            // Find the start and end of the JSON content within the string
            int startIndex = jsonString.IndexOf("{");
            int endIndex = jsonString.LastIndexOf("}");

            if (startIndex == -1 || endIndex == -1 || endIndex <= startIndex)
            {
                throw new InvalidOperationException("No valid JSON object found in the string");
            }

            // Extract and return the JSON content
            return jsonString.Substring(startIndex, endIndex - startIndex + 1);
        }
    }
}
