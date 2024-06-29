using System;
using System.Data.SQLite;
using System.Windows.Forms.DataVisualization.Charting;

namespace MedRePar.Services
{
    internal class ChartService
    {
        public static void GenerateTrendChart(string dbPath, string parameter, Chart chart, string runId)
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    string sql = "SELECT date, value FROM medical_data WHERE parameter = @parameter AND run_id = @run_id ORDER BY date";
                    SQLiteCommand command = new SQLiteCommand(sql, conn);
                    command.Parameters.AddWithValue("@parameter", parameter);
                    command.Parameters.AddWithValue("@run_id", runId);
                    SQLiteDataReader reader = command.ExecuteReader();

                    chart.Series.Clear();
                    Series series = new Series(parameter)
                    {
                        ChartType = SeriesChartType.Line
                    };

                    while (reader.Read())
                    {
                        series.Points.AddXY(Convert.ToDateTime(reader["date"]), Convert.ToDouble(reader["value"]));
                    }

                    chart.Series.Add(series);
                    LoggingService.LogInfo("Trend chart generated successfully.");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error generating trend chart", ex);
                throw;
            }
        }
    }
}
