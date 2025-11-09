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
                LoggingService.LogInfo($"Starting chart generation for {parameters.Count} parameters");

                var dataByCategory = GetDataByCategory(dbPath, parameters, runId);

                if (dataByCategory.Count == 0)
                {
                    LoggingService.LogWarning("No data found for chart generation");
                    throw new InvalidOperationException("No data available to generate charts. Please ensure PDFs were processed successfully.");
                }

                LoggingService.LogInfo($"Retrieved data for {dataByCategory.Count} categories");

                // Generate individual parameter charts
                foreach (var category in dataByCategory)
                {
                    GenerateCategoryChart(category.Key, category.Value, chart, imagePaths);
                }

                // Generate composite charts (all parameters in a category)
                foreach (var category in dataByCategory)
                {
                    GenerateCompositeChart(category.Key, category.Value, chart, imagePaths);
                }

                LoggingService.LogInfo($"Successfully generated {imagePaths.Count} chart images");
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error generating trend charts", ex);
                throw;
            }

            return imagePaths;
        }

        private static Dictionary<string, Dictionary<string, (List<(DateTime date, double value, string fullValue)>, string alias)>> GetDataByCategory(string dbPath, List<string> parameters, string runId)
        {
            var dataByCategory = new Dictionary<string, Dictionary<string, (List<(DateTime date, double value, string fullValue)>, string alias)>>();

            using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                conn.Open();

                // Fixed query: Use exact match instead of LIKE to avoid substring matching issues
                string sql = @"
                    SELECT
                        medical_data.date,
                        medical_data.value,
                        parameters.name as parameter,
                        parameters.alias as alias,
                        categories.name as category
                    FROM medical_data
                    INNER JOIN parameters ON medical_data.parameter_id = parameters.id
                    INNER JOIN categories ON parameters.category_id = categories.id
                    WHERE parameters.name = @parameter AND medical_data.run_id = @run_id
                    ORDER BY medical_data.date";

                foreach (var parameter in parameters)
                {
                    SQLiteCommand command = new SQLiteCommand(sql, conn);
                    command.Parameters.AddWithValue("@parameter", parameter);
                    command.Parameters.AddWithValue("@run_id", runId);

                    SQLiteDataReader reader = command.ExecuteReader();

                    while (reader.Read())
                    {
                        DateTime date = DateTime.Parse(reader["date"].ToString());
                        string fullValue = reader["value"].ToString();
                        string parameterName = reader["parameter"].ToString();
                        string alias = reader["alias"].ToString();
                        string category = reader["category"].ToString();

                        // Extract numeric value from string (handles "150 mg/dL", "14.5 g/dL (13-17)", etc.)
                        double numericValue = ExtractNumericValue(fullValue);

                        if (numericValue > 0)
                        {
                            if (!dataByCategory.ContainsKey(category))
                            {
                                dataByCategory[category] = new Dictionary<string, (List<(DateTime date, double value, string fullValue)>, string alias)>();
                            }
                            if (!dataByCategory[category].ContainsKey(parameterName))
                            {
                                dataByCategory[category][parameterName] = (new List<(DateTime date, double value, string fullValue)>(), alias);
                            }
                            dataByCategory[category][parameterName].Item1.Add((date, numericValue, fullValue));
                        }
                        else
                        {
                            LoggingService.LogWarning($"Could not extract numeric value from '{fullValue}' for {alias}");
                        }
                    }

                    reader.Close();
                }
            }

            return dataByCategory;
        }

        /// <summary>
        /// Extracts numeric value from strings like "150 mg/dL", "14.5 g/dL (13-17)", "7500 /cumm"
        /// </summary>
        private static double ExtractNumericValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            // Try to parse the first numeric token
            var tokens = value.Split(new[] { ' ', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                if (double.TryParse(token, out double result))
                {
                    return result;
                }
            }

            return 0;
        }

        private static void GenerateCategoryChart(string category, Dictionary<string, (List<(DateTime date, double value, string fullValue)>, string alias)> data, Chart chart, List<string> imagePaths)
        {
            foreach (var parameter in data)
            {
                if (parameter.Value.Item1.Count == 0)
                {
                    LoggingService.LogWarning($"No data points for {category} - {parameter.Value.Item2}. Skipping.");
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

                        // Configure X-axis (Date)
                        chartArea.AxisX.Title = "Date";
                        chartArea.AxisX.TitleFont = new Font("Arial", 10, FontStyle.Bold);
                        chartArea.AxisX.LabelStyle.Format = "yyyy-MM-dd";
                        chartArea.AxisX.IntervalType = DateTimeIntervalType.Auto;
                        chartArea.AxisX.LabelStyle.Angle = -45;
                        chartArea.AxisX.LabelStyle.Font = new Font("Arial", 9);

                        // Configure Y-axis (Value)
                        chartArea.AxisY.Title = "Value";
                        chartArea.AxisY.TitleFont = new Font("Arial", 10, FontStyle.Bold);
                        chartArea.AxisY.LabelStyle.Font = new Font("Arial", 9);

                        // Enable gridlines for better readability
                        chartArea.AxisX.MajorGrid.Enabled = true;
                        chartArea.AxisX.MajorGrid.LineColor = Color.LightGray;
                        chartArea.AxisY.MajorGrid.Enabled = true;
                        chartArea.AxisY.MajorGrid.LineColor = Color.LightGray;

                        // Create series
                        Series series = new Series(parameter.Value.Item2)
                        {
                            ChartType = SeriesChartType.Line,
                            XValueType = ChartValueType.Date,
                            MarkerStyle = MarkerStyle.Circle,
                            MarkerSize = 10,
                            BorderWidth = 3,
                            Color = Color.FromArgb(0, 112, 192) // Professional blue
                        };

                        // Add data points
                        var orderedData = parameter.Value.Item1.OrderBy(d => d.date).ToList();

                        foreach (var dataPoint in orderedData)
                        {
                            int pointIndex = series.Points.AddXY(dataPoint.date, dataPoint.value);
                            // Store full value (with units) as tooltip
                            series.Points[pointIndex].ToolTip = $"{dataPoint.date:yyyy-MM-dd}\n{dataPoint.fullValue}";
                        }

                        // Show values on data points
                        series.IsValueShownAsLabel = true;
                        series.LabelFormat = "0.##"; // Show up to 2 decimal places
                        series.Font = new Font("Arial", 9, FontStyle.Bold);
                        series.LabelForeColor = Color.Black;

                        chart.Series.Add(series);

                        // Add title
                        chart.Titles.Clear();
                        chart.Titles.Add(new Title(
                            $"{category} - {parameter.Value.Item2}",
                            Docking.Top,
                            new Font("Arial", 16, FontStyle.Bold),
                            Color.FromArgb(0, 112, 192)));

                        // Add subtitle with data point count and date range
                        if (orderedData.Count > 0)
                        {
                            string dateRange = orderedData.Count > 1
                                ? $"{orderedData.First().date:yyyy-MM-dd} to {orderedData.Last().date:yyyy-MM-dd}"
                                : $"{orderedData.First().date:yyyy-MM-dd}";

                            chart.Titles.Add(new Title(
                                $"({orderedData.Count} data point{(orderedData.Count > 1 ? "s" : "")} | {dateRange})",
                                Docking.Top,
                                new Font("Arial", 10),
                                Color.Gray));
                        }

                        // Configure legend
                        chart.Legends.Clear();
                        Legend legend = new Legend()
                        {
                            Font = new Font("Arial", 10),
                            Docking = Docking.Bottom
                        };
                        chart.Legends.Add(legend);

                        // Add background color
                        chartArea.BackColor = Color.White;
                        chart.BackColor = Color.WhiteSmoke;
                    });

                    string imagePath = SaveChartAsImage(chart, $"{category}_{parameter.Key}");
                    if (!string.IsNullOrEmpty(imagePath))
                    {
                        imagePaths.Add(imagePath);
                        LoggingService.LogInfo($"✓ Chart generated: {category} - {parameter.Value.Item2}");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogError($"Error generating chart for {category} - {parameter.Value.Item2}", ex);
                }
            }
        }

        private static void GenerateCompositeChart(string category, Dictionary<string, (List<(DateTime date, double value, string fullValue)>, string alias)> data, Chart chart, List<string> imagePaths)
        {
            if (data.All(d => d.Value.Item1.Count == 0))
            {
                LoggingService.LogWarning($"No data for composite chart in category: {category}");
                return;
            }

            // Skip composite chart if only one parameter (redundant with individual chart)
            if (data.Count(d => d.Value.Item1.Count > 0) <= 1)
            {
                LoggingService.LogInfo($"Skipping composite chart for {category} (only one parameter with data)");
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

                    // Configure axes
                    chartArea.AxisX.Title = "Date";
                    chartArea.AxisX.TitleFont = new Font("Arial", 10, FontStyle.Bold);
                    chartArea.AxisX.LabelStyle.Format = "yyyy-MM-dd";
                    chartArea.AxisX.IntervalType = DateTimeIntervalType.Auto;
                    chartArea.AxisX.LabelStyle.Angle = -45;
                    chartArea.AxisX.LabelStyle.Font = new Font("Arial", 9);

                    chartArea.AxisY.Title = "Value";
                    chartArea.AxisY.TitleFont = new Font("Arial", 10, FontStyle.Bold);
                    chartArea.AxisY.LabelStyle.Font = new Font("Arial", 9);

                    chartArea.AxisX.MajorGrid.Enabled = true;
                    chartArea.AxisX.MajorGrid.LineColor = Color.LightGray;
                    chartArea.AxisY.MajorGrid.Enabled = true;
                    chartArea.AxisY.MajorGrid.LineColor = Color.LightGray;

                    // Define color palette for multiple series
                    Color[] colors = new Color[]
                    {
                        Color.FromArgb(0, 112, 192),   // Blue
                        Color.FromArgb(237, 125, 49),  // Orange
                        Color.FromArgb(165, 165, 165), // Gray
                        Color.FromArgb(255, 192, 0),   // Yellow
                        Color.FromArgb(91, 155, 213),  // Light Blue
                        Color.FromArgb(112, 173, 71)   // Green
                    };

                    int seriesIndex = 0;
                    foreach (var parameter in data)
                    {
                        if (parameter.Value.Item1.Count > 0)
                        {
                            Series series = new Series($"Series{seriesIndex}")
                            {
                                ChartType = SeriesChartType.Line,
                                XValueType = ChartValueType.Date,
                                LegendText = parameter.Value.Item2,
                                MarkerStyle = MarkerStyle.Circle,
                                MarkerSize = 8,
                                BorderWidth = 2,
                                Color = colors[seriesIndex % colors.Length]
                            };

                            foreach (var dataPoint in parameter.Value.Item1.OrderBy(d => d.date))
                            {
                                int pointIndex = series.Points.AddXY(dataPoint.date, dataPoint.value);
                                series.Points[pointIndex].ToolTip = $"{parameter.Value.Item2}\n{dataPoint.date:yyyy-MM-dd}\n{dataPoint.fullValue}";
                            }

                            chart.Series.Add(series);
                            seriesIndex++;
                        }
                    }

                    // Configure legend
                    chart.Legends.Clear();
                    Legend legend = new Legend()
                    {
                        Font = new Font("Arial", 10),
                        Docking = Docking.Right,
                        Alignment = StringAlignment.Center
                    };
                    chart.Legends.Add(legend);

                    // Add title
                    chart.Titles.Clear();
                    chart.Titles.Add(new Title(
                        $"{category} - All Parameters",
                        Docking.Top,
                        new Font("Arial", 16, FontStyle.Bold),
                        Color.FromArgb(0, 112, 192)));

                    chart.Titles.Add(new Title(
                        $"Composite view of {seriesIndex} parameter{(seriesIndex > 1 ? "s" : "")}",
                        Docking.Top,
                        new Font("Arial", 10),
                        Color.Gray));

                    chartArea.BackColor = Color.White;
                    chart.BackColor = Color.WhiteSmoke;
                });

                string imagePath = SaveChartAsImage(chart, $"{category}_Composite");
                if (!string.IsNullOrEmpty(imagePath))
                {
                    imagePaths.Add(imagePath);
                    LoggingService.LogInfo($"✓ Composite chart generated: {category}");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Error generating composite chart for {category}", ex);
            }
        }

        private static string SaveChartAsImage(Chart chart, string chartName)
        {
            string directoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrendCharts");
            string sanitizedChartName = string.Join("_", chartName.Split(Path.GetInvalidFileNameChars()));
            string imagePath = Path.Combine(directoryPath, $"Chart_{sanitizedChartName}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.png");

            try
            {
                Directory.CreateDirectory(directoryPath);

                chart.Invoke((MethodInvoker)delegate
                {
                    if (chart.Series.Count > 0 && chart.Series.Any(s => s.Points.Count > 0))
                    {
                        chart.Width = 1400;  // Wider for better readability
                        chart.Height = 900;  // Taller for better readability

                        chart.SaveImage(imagePath, ChartImageFormat.Png);
                        LoggingService.LogDebug($"Chart saved to: {imagePath}");
                    }
                    else
                    {
                        LoggingService.LogWarning($"Chart {chartName} has no data points");
                        imagePath = null;
                    }
                });

                return imagePath;
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Error saving chart image for {chartName}", ex);
                return null;
            }
        }

        public static string SaveImagesAsPdf(List<string> imagePaths)
        {
            if (imagePaths == null || imagePaths.Count == 0)
            {
                throw new InvalidOperationException("No chart images available to create PDF");
            }

            string directoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrendCharts");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string pdfPath = Path.Combine(directoryPath, $"HealthTrends_{timestamp}.pdf");

            try
            {
                Directory.CreateDirectory(directoryPath);

                using (PdfDocument document = new PdfDocument())
                {
                    document.Info.Title = "Medical Report Trends";
                    document.Info.Author = "MedRePar";
                    document.Info.Subject = "Health Parameter Trends";
                    document.Info.CreationDate = DateTime.Now;

                    int validImageCount = 0;

                    foreach (var imagePath in imagePaths)
                    {
                        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                        {
                            PdfPage page = document.AddPage();
                            page.Width = XUnit.FromMillimeter(297);  // A4 landscape width
                            page.Height = XUnit.FromMillimeter(210); // A4 landscape height

                            XGraphics gfx = XGraphics.FromPdfPage(page);
                            XImage image = XImage.FromFile(imagePath);

                            // Draw image to fill the page while maintaining aspect ratio
                            double imageAspect = (double)image.PixelWidth / image.PixelHeight;
                            double pageAspect = page.Width / page.Height;

                            double width, height;
                            if (imageAspect > pageAspect)
                            {
                                width = page.Width;
                                height = page.Width / imageAspect;
                            }
                            else
                            {
                                height = page.Height;
                                width = page.Height * imageAspect;
                            }

                            double x = (page.Width - width) / 2;
                            double y = (page.Height - height) / 2;

                            gfx.DrawImage(image, x, y, width, height);
                            validImageCount++;
                        }
                        else
                        {
                            LoggingService.LogWarning($"Image file not found or invalid: {imagePath}");
                        }
                    }

                    if (validImageCount == 0)
                    {
                        throw new InvalidOperationException("No valid images found to create PDF");
                    }

                    document.Save(pdfPath);
                    LoggingService.LogInfo($"PDF created successfully: {pdfPath} ({validImageCount} pages)");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error creating PDF from chart images", ex);
                throw;
            }

            // Clean up temporary image files
            DeleteTemporaryFiles(imagePaths);

            return pdfPath;
        }

        private static void DeleteTemporaryFiles(List<string> imagePaths)
        {
            int deletedCount = 0;

            foreach (var imagePath in imagePaths)
            {
                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    try
                    {
                        File.Delete(imagePath);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning($"Could not delete temporary file {imagePath}: {ex.Message}");
                    }
                }
            }

            if (deletedCount > 0)
            {
                LoggingService.LogInfo($"Cleaned up {deletedCount} temporary chart image(s)");
            }
        }

        public static void OpenPdf(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
            {
                throw new ArgumentException("PDF path cannot be empty");
            }

            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException($"PDF file not found: {pdfPath}");
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true,
                    Verb = "open"
                };

                Process.Start(startInfo);
                LoggingService.LogInfo($"Opened PDF in default viewer: {pdfPath}");
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Error opening PDF: {pdfPath}", ex);
                throw new InvalidOperationException($"Failed to open PDF. You can manually open it at: {pdfPath}", ex);
            }
        }
    }
}
