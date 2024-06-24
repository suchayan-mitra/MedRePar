using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms.DataVisualization.Charting;
using System.Data.SQLite;

namespace MedRePar.Services
{
    internal class ChartService
    {
        public static void GenerateTrendChart(string dbPath, string parameter, Chart chart)
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    string sql = "SELECT date, value FROM medical_data WHERE parameter = @parameter ORDER BY date";
                    SQLiteCommand command = new SQLiteCommand(sql, conn);
                    command.Parameters.AddWithValue("@parameter", parameter);
                    SQLiteDataReader reader = command.ExecuteReader();

                    chart.Series.Clear();
                    Series series = new Series(parameter)
                    {
                        ChartType = SeriesChartType.Line
                    };

                    while (reader.Read())
                    {
                        series.Points.AddXY(reader["date"], reader["value"]);
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
