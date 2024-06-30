using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms.DataVisualization.Charting;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Diagnostics;

namespace MedRePar.Services
{
    internal class ChartService
    {
        public static void GenerateTrendChart(string dbPath, string parameter, Chart chart, string runId)
        {
            try
            {
                List<(DateTime date, double value)> dataPoints = new List<(DateTime date, double value)>();

                using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    string sql = "SELECT date, value FROM medical_data WHERE parameter LIKE @parameter AND run_id = @run_id ORDER BY date";
                    LoggingService.LogInfo($"Executing SQL Query: {sql}");
                    SQLiteCommand command = new SQLiteCommand(sql, conn);
                    command.Parameters.AddWithValue("@parameter", "%" + parameter + "%");
                    command.Parameters.AddWithValue("@run_id", runId);
                    SQLiteDataReader reader = command.ExecuteReader();

                    LoggingService.LogInfo("Retrieved Data Points:");
                    while (reader.Read())
                    {
                        DateTime date = DateTime.Parse(reader["date"].ToString());
                        double value = double.Parse(reader["value"].ToString());
                        dataPoints.Add((date, value));
                        LoggingService.LogInfo($"Date: {date}, Value: {value}");
                    }
                }

                // Use BeginInvoke to ensure the chart update is performed on the UI thread
                chart.BeginInvoke(new Action(() =>
                {
                    chart.Series.Clear();
                    Series series = new Series(parameter)
                    {
                        ChartType = SeriesChartType.Line,
                        XValueType = ChartValueType.Date
                    };

                    foreach (var dataPoint in dataPoints)
                    {
                        series.Points.AddXY(dataPoint.date, dataPoint.value);
                    }

                    chart.Series.Add(series);
                    chart.Invalidate(); // Refresh the chart

                    // Save the chart as an image
                    string imagePath = SaveChartAsImage(chart);

                    // Save the image as a PDF
                    string pdfPath = SaveImageAsPdf(imagePath);

                    // Open the PDF for the user
                    OpenPdf(pdfPath);

                    LoggingService.LogInfo("Trend chart generated and saved successfully.");
                }));
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error generating trend chart", ex);
                throw;
            }
        }

        private static string SaveChartAsImage(Chart chart)
        {
            string imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrendChart.png");
            chart.SaveImage(imagePath, ChartImageFormat.Png);
            return imagePath;
        }

        private static string SaveImageAsPdf(string imagePath)
        {
            string pdfPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrendChart.pdf");
            using (PdfDocument document = new PdfDocument())
            {
                PdfPage page = document.AddPage();
                XGraphics gfx = XGraphics.FromPdfPage(page);
                XImage image = XImage.FromFile(imagePath);
                gfx.DrawImage(image, 0, 0, page.Width, page.Height);
                document.Save(pdfPath);
            }
            LoggingService.LogInfo($"Trend chart saved as PDF: {pdfPath}");
            return pdfPath;
        }

        private static void OpenPdf(string pdfPath)
        {
            if (File.Exists(pdfPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
        }
    }
}
