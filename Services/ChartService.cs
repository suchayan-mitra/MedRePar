using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
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
        public static List<string> GenerateTrendCharts(string dbPath, List<string> parameters, Chart chart, string runId)
        {
            List<string> imagePaths = new List<string>();

            try
            {
                foreach (var parameter in parameters)
                {
                    Dictionary<string, List<(DateTime date, double value)>> dataPointsByCategory = new Dictionary<string, List<(DateTime date, double value)>>();

                    using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                    {
                        conn.Open();
                        string sql = @"
                    SELECT medical_data.date, medical_data.value, categories.name as category
                    FROM medical_data
                    INNER JOIN parameters ON medical_data.parameter_id = parameters.id
                    INNER JOIN categories ON parameters.category_id = categories.id
                    WHERE parameters.name LIKE @parameter AND medical_data.run_id = @run_id 
                    ORDER BY medical_data.date";

                        LoggingService.LogInfo($"Executing SQL Query: {sql}");
                        SQLiteCommand command = new SQLiteCommand(sql, conn);
                        command.Parameters.AddWithValue("@parameter", "%" + parameter + "%");
                        command.Parameters.AddWithValue("@run_id", runId);

                        LoggingService.LogInfo($"Parameter: {parameter}");
                        LoggingService.LogInfo($"Run ID: {runId}");

                        SQLiteDataReader reader = command.ExecuteReader();

                        LoggingService.LogInfo("Retrieved Data Points:");
                        while (reader.Read())
                        {
                            DateTime date = DateTime.Parse(reader["date"].ToString());
                            string rawValue = reader["value"].ToString();
                            double value;
                            if (!TryParseDouble(rawValue, out value))
                            {
                                LoggingService.LogWarn($"Skipping invalid value: {rawValue}");
                                continue;
                            }
                            string category = reader["category"].ToString();

                            if (!dataPointsByCategory.ContainsKey(category))
                            {
                                dataPointsByCategory[category] = new List<(DateTime date, double value)>();
                            }

                            dataPointsByCategory[category].Add((date, value));
                            LoggingService.LogInfo($"Category: {category}, Date: {date}, Value: {value}");
                        }
                    }

                    if (dataPointsByCategory.Count == 0)
                    {
                        LoggingService.LogInfo("No data points retrieved for the specified parameter and run ID.");
                        continue;
                    }

                    // Use BeginInvoke to ensure the chart update is performed on the UI thread
                    chart.BeginInvoke(new Action(() =>
                    {
                        chart.Series.Clear();
                        chart.ChartAreas.Clear();
                        ChartArea chartArea = new ChartArea("MainArea");
                        chart.ChartAreas.Add(chartArea);

                        chartArea.AxisX.Title = "Date";
                        chartArea.AxisY.Title = "Value";
                        chartArea.AxisX.LabelStyle.Format = "yyyy-MM-dd";
                        chartArea.AxisX.IntervalType = DateTimeIntervalType.Days;
                        chartArea.AxisX.Interval = 1;
                        chartArea.AxisX.LabelStyle.Angle = -45;
                        chartArea.AxisX.LabelStyle.IsEndLabelVisible = true;

                        foreach (var category in dataPointsByCategory.Keys)
                        {
                            Series series = new Series($"{category} - {parameter}")
                            {
                                ChartType = SeriesChartType.Line,
                                XValueType = ChartValueType.Date
                            };

                            foreach (var dataPoint in dataPointsByCategory[category])
                            {
                                series.Points.AddXY(dataPoint.date, dataPoint.value);
                                LoggingService.LogInfo($"Plotting: {category} - Date: {dataPoint.date}, Value: {dataPoint.value}");
                            }

                            chart.Series.Add(series);
                        }

                        chart.Legends.Clear();
                        Legend legend = new Legend
                        {
                            Docking = Docking.Bottom
                        };
                        chart.Legends.Add(legend);

                        chart.Titles.Clear();
                        chart.Titles.Add(new Title(parameter, Docking.Top, new Font("Verdana", 12), Color.Black));

                        chart.Invalidate(); // Refresh the chart

                        // Save the chart as an image
                        string imagePath = SaveChartAsImage(chart);
                        imagePaths.Add(imagePath);
                        LoggingService.LogInfo($"Chart image saved at: {imagePath}");

                        LoggingService.LogInfo("Trend chart generated and saved successfully.");
                    }));

                    // Wait a bit to ensure chart has been rendered and saved
                    System.Threading.Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error generating trend charts", ex);
                throw;
            }

            return imagePaths;
        }

        private static bool TryParseDouble(string input, out double result)
        {
            input = new string(input.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
            return double.TryParse(input, out result);
        }



        public static string SaveImagesAsPdf(List<string> imagePaths)
        {
            string pdfPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrendCharts.pdf");

            try
            {
                using (PdfDocument document = new PdfDocument())
                {
                    foreach (var imagePath in imagePaths)
                    {
                        if (!string.IsNullOrEmpty(imagePath))
                        {
                            PdfPage page = document.AddPage();
                            XGraphics gfx = XGraphics.FromPdfPage(page);
                            XImage image = XImage.FromFile(imagePath);
                            gfx.DrawImage(image, 0, 0, page.Width, page.Height);
                        }
                    }
                    document.Save(pdfPath);
                }
                LoggingService.LogInfo($"Trend charts saved as PDF: {pdfPath}");
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error saving trend charts as PDF", ex);
                throw;
            }

            // Delete the temporary image files
            DeleteTemporaryFiles(imagePaths);

            return pdfPath;
        }

        private static void DeleteTemporaryFiles(List<string> imagePaths)
        {
            try
            {
                foreach (var imagePath in imagePaths)
                {
                    if (File.Exists(imagePath))
                    {
                        File.Delete(imagePath);
                        LoggingService.LogInfo($"Deleted temporary file: {imagePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error deleting temporary files", ex);
                throw;
            }
        }

        private static string SaveChartAsImage(Chart chart)
        {
            string imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"TrendChart_{Guid.NewGuid()}.png");
            chart.SaveImage(imagePath, ChartImageFormat.Png);
            return imagePath;
        }

        public static void OpenPdf(string pdfPath)
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
