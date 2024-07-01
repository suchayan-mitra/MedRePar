using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Diagnostics;
using System.IO;

namespace MedRePar.Services
{
    internal class ChartService
    {
        public static List<string> GenerateTrendCharts(string dbPath, List<string> parameters, Chart chart, string runId)
        {
            List<string> imagePaths = new List<string>();

            try
            {
                var dataByCategory = GetDataByCategory(dbPath, parameters, runId);

                foreach (var category in dataByCategory)
                {
                    GenerateCategoryChart(category.Key, category.Value, chart, imagePaths);
                }

                foreach (var category in dataByCategory)
                {
                    GenerateCompositeChart(category.Key, category.Value, chart, imagePaths);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error generating trend charts", ex);
                throw;
            }

            return imagePaths;
        }

        private static Dictionary<string, Dictionary<string, (List<(DateTime date, double value)>, string alias)>> GetDataByCategory(string dbPath, List<string> parameters, string runId)
        {
            var dataByCategory = new Dictionary<string, Dictionary<string, (List<(DateTime date, double value)>, string alias)>>();

            using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                conn.Open();
                string sql = @"
                    SELECT medical_data.date, medical_data.value, parameters.name as parameter, parameters.alias as alias, categories.name as category
                    FROM medical_data
                    INNER JOIN parameters ON medical_data.parameter_id = parameters.id
                    INNER JOIN categories ON parameters.category_id = categories.id
                    WHERE parameters.name LIKE @parameter AND medical_data.run_id = @run_id 
                    ORDER BY medical_data.date";

                foreach (var parameter in parameters)
                {
                    SQLiteCommand command = new SQLiteCommand(sql, conn);
                    command.Parameters.AddWithValue("@parameter", "%" + parameter + "%");
                    command.Parameters.AddWithValue("@run_id", runId);

                    SQLiteDataReader reader = command.ExecuteReader();

                    while (reader.Read())
                    {
                        DateTime date = DateTime.Parse(reader["date"].ToString());
                        string rawValue = reader["value"].ToString();
                        string parameterName = reader["parameter"].ToString();
                        string alias = reader["alias"].ToString();
                        string category = reader["category"].ToString();

                        if (double.TryParse(rawValue.Split(' ')[0], out double value))
                        {
                            if (!dataByCategory.ContainsKey(category))
                            {
                                dataByCategory[category] = new Dictionary<string, (List<(DateTime date, double value)>, string alias)>();
                            }
                            if (!dataByCategory[category].ContainsKey(parameterName))
                            {
                                dataByCategory[category][parameterName] = (new List<(DateTime date, double value)>(), alias);
                            }
                            dataByCategory[category][parameterName].Item1.Add((date, value));
                        }
                        else
                        {
                            LoggingService.LogWarn($"Non-numeric value for {parameterName}: {rawValue}");
                        }
                    }
                }
            }

            return dataByCategory;
        }

        private static void GenerateCategoryChart(string category, Dictionary<string, (List<(DateTime date, double value)>, string alias)> data, Chart chart, List<string> imagePaths)
        {
            foreach (var parameter in data)
            {
                if (parameter.Value.Item1.Count == 0)
                {
                    LoggingService.LogWarn($"No valid numeric data points for {category} - {parameter.Key}. Skipping chart generation.");
                    continue;
                }

                try
                {
                    chart.Invoke((MethodInvoker)delegate
                    {
                        chart.Series.Clear();
                        chart.ChartAreas.Clear();
                        ChartArea chartArea = new ChartArea("MainArea");
                        chart.ChartAreas.Add(chartArea);

                        chartArea.AxisX.Title = "Date";
                        chartArea.AxisY.Title = "Value";
                        chartArea.AxisX.LabelStyle.Format = "yyyy-MM-dd";
                        chartArea.AxisX.IntervalType = DateTimeIntervalType.Auto;
                        chartArea.AxisX.LabelStyle.Angle = -45;
                        chartArea.AxisX.LabelStyle.Font = new Font("Arial", 8);
                        chartArea.AxisY.LabelStyle.Font = new Font("Arial", 8);

                        Series series = new Series(parameter.Value.Item2)
                        {
                            ChartType = SeriesChartType.Line,
                            XValueType = ChartValueType.Date,
                            MarkerStyle = MarkerStyle.Circle,
                            MarkerSize = 8
                        };

                        foreach (var dataPoint in parameter.Value.Item1.OrderBy(d => d.date))
                        {
                            series.Points.AddXY(dataPoint.date, dataPoint.value);
                        }

                        series.IsValueShownAsLabel = true;
                        series.LabelFormat = "F2";
                        series.Font = new Font("Arial", 8);

                        chart.Series.Add(series);

                        chart.Titles.Clear();
                        chart.Titles.Add(new Title($"{category} - {parameter.Value.Item2}", Docking.Top, new Font("Arial", 14, FontStyle.Bold), Color.Black));

                        chart.Legends.Clear();
                        chart.Legends.Add(new Legend() { Font = new Font("Arial", 10) });
                    });

                    string imagePath = SaveChartAsImage(chart, $"{category}_{parameter.Key}");
                    if (!string.IsNullOrEmpty(imagePath))
                    {
                        imagePaths.Add(imagePath);
                        LoggingService.LogInfo($"Chart generated for {category} - {parameter.Value.Item2}");
                    }
                    else
                    {
                        LoggingService.LogWarn($"Failed to generate chart for {category} - {parameter.Value.Item2}");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogError($"Error generating chart for {category} - {parameter.Value.Item2}: {ex.Message}");
                    LoggingService.LogError($"Stack Trace: {ex.StackTrace}");
                }
            }
        }

        private static void GenerateCompositeChart(string category, Dictionary<string, (List<(DateTime date, double value)>, string alias)> data, Chart chart, List<string> imagePaths)
        {
            if (data.All(d => d.Value.Item1.Count == 0))
            {
                LoggingService.LogWarn($"No valid numeric data points for any parameter in {category}. Skipping composite chart generation.");
                return;
            }

            try
            {
                chart.Invoke((MethodInvoker)delegate
                {
                    chart.Series.Clear();
                    chart.ChartAreas.Clear();
                    ChartArea chartArea = new ChartArea("MainArea");
                    chart.ChartAreas.Add(chartArea);

                    chartArea.AxisX.Title = "Date";
                    chartArea.AxisY.Title = "Value";
                    chartArea.AxisX.LabelStyle.Format = "yyyy-MM-dd";
                    chartArea.AxisX.IntervalType = DateTimeIntervalType.Auto;
                    chartArea.AxisX.LabelStyle.Angle = -45;
                    chartArea.AxisX.LabelStyle.Font = new Font("Arial", 8);
                    chartArea.AxisY.LabelStyle.Font = new Font("Arial", 8);

                    int seriesIndex = 0;
                    foreach (var parameter in data)
                    {
                        if (parameter.Value.Item1.Count > 0)
                        {
                            Series series = new Series($"DataSeries{seriesIndex}")
                            {
                                ChartType = SeriesChartType.Line,
                                XValueType = ChartValueType.Date,
                                LegendText = parameter.Value.Item2
                            };

                            foreach (var dataPoint in parameter.Value.Item1.OrderBy(d => d.date))
                            {
                                series.Points.AddXY(dataPoint.date, dataPoint.value);
                            }

                            chart.Series.Add(series);
                            seriesIndex++;
                        }
                    }

                    chart.Legends.Clear();
                    chart.Legends.Add(new Legend() { Font = new Font("Arial", 10) });

                    chart.Titles.Clear();
                    chart.Titles.Add(new Title($"{category} - Composite Chart", Docking.Top, new Font("Arial", 14, FontStyle.Bold), Color.Black));
                });

                string imagePath = SaveChartAsImage(chart, $"{category}_Composite");
                if (!string.IsNullOrEmpty(imagePath))
                {
                    imagePaths.Add(imagePath);
                    LoggingService.LogInfo($"Composite chart generated for {category}");
                }
                else
                {
                    LoggingService.LogWarn($"Failed to generate composite chart for {category}");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Error generating composite chart for {category}: {ex.Message}");
                LoggingService.LogError($"Stack Trace: {ex.StackTrace}");
            }
        }

        private static string SaveChartAsImage(Chart chart, string chartName)
        {
            string directoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrendCharts");

            string sanitizedChartName = string.Join("_", chartName.Split(Path.GetInvalidFileNameChars()));

            string imagePath = Path.Combine(directoryPath, $"TrendChart_{sanitizedChartName}_{Guid.NewGuid()}.png");

            try
            {
                Directory.CreateDirectory(directoryPath);

                chart.Invoke((MethodInvoker)delegate
                {
                    if (chart.Series.Count > 0 && chart.Series[0].Points.Count > 0)
                    {
                        chart.Width = 1200;  // Increased width
                        chart.Height = 800;  // Increased height

                        chart.SaveImage(imagePath, ChartImageFormat.Png);
                        LoggingService.LogInfo($"Chart image saved: {imagePath}");
                    }
                    else
                    {
                        LoggingService.LogWarn($"Chart {chartName} has no valid points to plot.");
                        imagePath = null;
                    }
                });

                return imagePath;
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Error saving chart image for {chartName}: {ex.Message}");
                LoggingService.LogError($"Stack Trace: {ex.StackTrace}");
                return null;
            }
        }

        public static string SaveImagesAsPdf(List<string> imagePaths)
        {
            string directoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrendCharts");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            string pdfPath = Path.Combine(directoryPath, $"HealthTrends_{timestamp}.pdf");

            try
            {
                Directory.CreateDirectory(directoryPath);

                using (PdfDocument document = new PdfDocument())
                {
                    foreach (var imagePath in imagePaths)
                    {
                        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
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

            DeleteTemporaryFiles(imagePaths);

            return pdfPath;
        }

        private static void DeleteTemporaryFiles(List<string> imagePaths)
        {
            foreach (var imagePath in imagePaths)
            {
                if (File.Exists(imagePath))
                {
                    try
                    {
                        File.Delete(imagePath);
                        LoggingService.LogInfo($"Deleted temporary file: {imagePath}");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError($"Error deleting temporary file {imagePath}: {ex.Message}");
                    }
                }
            }
        }

        public static void OpenPdf(string pdfPath)
        {
            if (File.Exists(pdfPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = pdfPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                catch (Exception ex)
                {
                    LoggingService.LogError($"Error opening PDF {pdfPath}: {ex.Message}");
                }
            }
            else
            {
                LoggingService.LogWarn($"PDF file not found: {pdfPath}");
            }
        }
    }
}