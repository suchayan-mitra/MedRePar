using System;
using System.Collections.Generic;
using System.Data.SQLite;

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
                                    value REAL,
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
                    foreach (var item in data)
                    {
                        string sql = "INSERT INTO medical_data (parameter, value, date, run_id) VALUES (@parameter, @value, @date, @run_id)";
                        SQLiteCommand command = new SQLiteCommand(sql, conn);
                        command.Parameters.AddWithValue("@parameter", item.Key);
                        command.Parameters.AddWithValue("@value", item.Value);
                        command.Parameters.AddWithValue("@date", date);
                        command.Parameters.AddWithValue("@run_id", runId);
                        command.ExecuteNonQuery();
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
    }
}
