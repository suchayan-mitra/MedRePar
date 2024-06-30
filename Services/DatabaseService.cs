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

                    string sqlCategories = @"CREATE TABLE IF NOT EXISTS categories (
                                                id INTEGER PRIMARY KEY,
                                                name TEXT)";
                    SQLiteCommand commandCategories = new SQLiteCommand(sqlCategories, conn);
                    commandCategories.ExecuteNonQuery();

                    string sqlParameters = @"CREATE TABLE IF NOT EXISTS parameters (
                                                id INTEGER PRIMARY KEY,
                                                category_id INTEGER,
                                                name TEXT,
                                                alias TEXT)";
                    SQLiteCommand commandParameters = new SQLiteCommand(sqlParameters, conn);
                    commandParameters.ExecuteNonQuery();

                    string sqlMedicalData = @"CREATE TABLE IF NOT EXISTS medical_data (
                                                id INTEGER PRIMARY KEY,
                                                parameter_id INTEGER,
                                                value TEXT,
                                                date TEXT,
                                                run_id TEXT)";
                    SQLiteCommand commandMedicalData = new SQLiteCommand(sqlMedicalData, conn);
                    commandMedicalData.ExecuteNonQuery();

                    LoggingService.LogInfo("Database tables created or already exist.");
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

                        JObject parsedData;
                        try
                        {
                            parsedData = JObject.Parse(jsonString);
                        }
                        catch (Exception ex)
                        {
                            LoggingService.LogError("Error parsing JSON", ex);
                            throw;
                        }

                        string reportDate = parsedData["Date"].ToString();
                        LoggingService.LogInfo($"Report Date: {reportDate}");

                        foreach (var category in parsedData["Parameters"])
                        {
                            string categoryName = ((JProperty)category).Name;
                            int categoryId = GetCategoryId(conn, categoryName);

                            foreach (var item in ((JProperty)category).Value.Children<JProperty>())
                            {
                                string parameterName = NormalizeParameterName(item.Name);
                                string aliasName = item.Name;
                                string value = item.Value.ToString();

                                int parameterId = GetParameterId(conn, categoryId, parameterName, aliasName);

                                string sql = "INSERT INTO medical_data (parameter_id, value, date, run_id) VALUES (@parameter_id, @value, @date, @run_id)";
                                SQLiteCommand command = new SQLiteCommand(sql, conn);
                                command.Parameters.AddWithValue("@parameter_id", parameterId);
                                command.Parameters.AddWithValue("@value", value);
                                command.Parameters.AddWithValue("@date", reportDate);
                                command.Parameters.AddWithValue("@run_id", runId);
                                int rowsAffected = command.ExecuteNonQuery();
                                LoggingService.LogInfo($"Inserted: {parameterName} = {value}, Rows Affected: {rowsAffected}");
                            }
                        }

                        LoggingService.LogInfo("Data stored in database successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error storing data in database", ex);
                throw;
            }
        }

        private static string NormalizeParameterName(string parameterName)
        {
            return parameterName.ToLower().Replace(" ", "").Replace("-", "").Replace(":", "");
        }

        private static int GetCategoryId(SQLiteConnection conn, string categoryName)
        {
            // Check if the category already exists
            string sql = "SELECT id FROM categories WHERE name = @name";
            SQLiteCommand command = new SQLiteCommand(sql, conn);
            command.Parameters.AddWithValue("@name", categoryName);
            object result = command.ExecuteScalar();

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
            string sql = "SELECT id FROM parameters WHERE category_id = @category_id AND (name = @name OR alias LIKE '%' || @name || '%')";
            SQLiteCommand command = new SQLiteCommand(sql, conn);
            command.Parameters.AddWithValue("@category_id", categoryId);
            command.Parameters.AddWithValue("@name", parameterName);
            object result = command.ExecuteScalar();

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
                    string sql = "PRAGMA table_info(medical_data)";
                    SQLiteCommand command = new SQLiteCommand(sql, conn);
                    SQLiteDataReader reader = command.ExecuteReader();

                    LoggingService.LogInfo("Table Structure for 'medical_data':");
                    while (reader.Read())
                    {
                        LoggingService.LogInfo($"Column: {reader["name"]}, Type: {reader["type"]}");
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
                    string sql = @"SELECT medical_data.id, parameters.name as parameter, parameters.alias as alias, medical_data.value, medical_data.date, medical_data.run_id
                           FROM medical_data
                           INNER JOIN parameters ON medical_data.parameter_id = parameters.id";
                    SQLiteCommand command = new SQLiteCommand(sql, conn);
                    SQLiteDataReader reader = command.ExecuteReader();

                    LoggingService.LogInfo("All Data from 'medical_data':");

                    // Print the column names
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        LoggingService.LogInfo($"Column {i}: {reader.GetName(i)}");
                    }

                    // Print the data
                    while (reader.Read())
                    {
                        LoggingService.LogInfo($"ID: {reader["id"]}, Parameter: {reader["parameter"]}, Alias: {reader["alias"]}, Value: {reader["value"]}, Date: {reader["date"]}, Run ID: {reader["run_id"]}");
                    }
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
            // Find the start and end of the JSON content within the string
            int startIndex = jsonString.IndexOf("{");
            int endIndex = jsonString.LastIndexOf("}") + 1;

            // Extract and return the JSON content
            return jsonString.Substring(startIndex, endIndex - startIndex);
        }
    }
}
