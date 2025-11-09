using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Polly;
using Polly.Retry;

namespace MedRePar.Services
{
    internal class PdfService
    {
        private static DocumentAnalysisClient? _documentAnalysisClient;
        private static readonly AsyncRetryPolicy _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 4,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    LoggingService.LogWarning($"Retry {retryCount} after {timeSpan.TotalSeconds}s due to: {exception.Message}");
                });

        /// <summary>
        /// Initializes the Azure Document Intelligence client for OCR capabilities.
        /// Call this during application startup with credentials from App.config.
        /// </summary>
        public static void InitializeOcrClient(string endpoint, string apiKey)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey))
                {
                    _documentAnalysisClient = new DocumentAnalysisClient(
                        new Uri(endpoint),
                        new AzureKeyCredential(apiKey));
                    LoggingService.LogInfo("Azure Document Intelligence OCR client initialized successfully.");
                }
                else
                {
                    LoggingService.LogWarning("Azure Document Intelligence credentials not provided. OCR will be limited to embedded text extraction only.");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Failed to initialize Azure Document Intelligence client", ex);
                throw;
            }
        }

        /// <summary>
        /// Intelligently extracts text from PDF - detects if scanned and uses appropriate method.
        /// Prioritizes embedded text extraction (fast), falls back to OCR for scanned documents.
        /// </summary>
        public static async Task<string> ExtractTextFromPdfAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"PDF file not found: {filePath}");
            }

            try
            {
                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    // Step 1: Try embedded text extraction first (fast for digital PDFs)
                    string embeddedText = ExtractEmbeddedText(filePath);

                    // Step 2: Check if we got meaningful text
                    if (IsTextMeaningful(embeddedText))
                    {
                        LoggingService.LogInfo($"Successfully extracted embedded text from {Path.GetFileName(filePath)} ({embeddedText.Length} chars)");
                        return embeddedText;
                    }

                    // Step 3: Text is empty or garbage - this is likely a scanned PDF, use OCR
                    LoggingService.LogInfo($"Embedded text extraction failed or returned minimal content. Attempting OCR for {Path.GetFileName(filePath)}");

                    if (_documentAnalysisClient == null)
                    {
                        throw new InvalidOperationException(
                            "This appears to be a scanned PDF requiring OCR, but Azure Document Intelligence is not configured. " +
                            "Please add Azure Document Intelligence credentials to App.config. " +
                            "Alternatively, convert scanned PDFs to searchable PDFs using Adobe Acrobat or similar tools.");
                    }

                    string ocrText = await ExtractTextUsingOcr(filePath);
                    LoggingService.LogInfo($"OCR extraction completed for {Path.GetFileName(filePath)} ({ocrText.Length} chars)");
                    return ocrText;
                });
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to extract text from PDF: {filePath}", ex);
                throw;
            }
        }

        /// <summary>
        /// Synchronous wrapper for backward compatibility
        /// </summary>
        public static string ExtractTextFromPdf(string filePath)
        {
            return ExtractTextFromPdfAsync(filePath).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Extracts embedded text from PDF using iTextSharp (works only for digital PDFs with selectable text)
        /// </summary>
        private static string ExtractEmbeddedText(string filePath)
        {
            try
            {
                using (PdfReader reader = new PdfReader(filePath))
                {
                    StringBuilder text = new StringBuilder();
                    for (int i = 1; i <= reader.NumberOfPages; i++)
                    {
                        string pageText = PdfTextExtractor.GetTextFromPage(reader, i);
                        text.AppendLine(pageText);
                        text.AppendLine($"\n--- Page {i} ---\n");
                    }
                    return text.ToString();
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning($"Embedded text extraction failed: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Uses Azure Document Intelligence (Form Recognizer) for OCR on scanned PDFs
        /// </summary>
        private static async Task<string> ExtractTextUsingOcr(string filePath)
        {
            if (_documentAnalysisClient == null)
            {
                throw new InvalidOperationException("Azure Document Intelligence client is not initialized.");
            }

            try
            {
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    // Use the prebuilt "read" model which is optimized for text extraction
                    AnalyzeDocumentOperation operation = await _documentAnalysisClient.AnalyzeDocumentAsync(
                        WaitUntil.Completed,
                        "prebuilt-read",
                        stream);

                    AnalyzeResult result = operation.Value;
                    StringBuilder text = new StringBuilder();

                    // Extract text maintaining document structure
                    foreach (DocumentPage page in result.Pages)
                    {
                        text.AppendLine($"\n--- Page {page.PageNumber} ---\n");

                        // Extract lines in reading order
                        foreach (DocumentLine line in page.Lines)
                        {
                            text.AppendLine(line.Content);
                        }
                    }

                    return text.ToString();
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 429)
            {
                LoggingService.LogWarning("Azure Document Intelligence rate limit exceeded. Consider upgrading your tier or reducing request frequency.");
                throw;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("OCR extraction failed", ex);
                throw;
            }
        }

        /// <summary>
        /// Determines if extracted text is meaningful (vs empty or garbled)
        /// </summary>
        private static bool IsTextMeaningful(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            // Remove whitespace and check if we have substantial content
            string trimmed = text.Trim();

            // Need at least 50 characters for a meaningful medical report
            if (trimmed.Length < 50)
            {
                return false;
            }

            // Check if text contains mostly readable characters (not binary garbage)
            int readableChars = 0;
            int totalChars = 0;

            foreach (char c in trimmed)
            {
                totalChars++;
                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c))
                {
                    readableChars++;
                }
            }

            // If less than 70% readable characters, it's probably garbage
            double readableRatio = (double)readableChars / totalChars;
            return readableRatio > 0.7;
        }

        /// <summary>
        /// Validates that a file is a valid PDF
        /// </summary>
        public static bool IsValidPdf(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return false;
                }

                // Check file extension
                if (!filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Try to open with PdfReader to validate PDF structure
                using (PdfReader reader = new PdfReader(filePath))
                {
                    return reader.NumberOfPages > 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
