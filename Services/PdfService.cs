using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using System.Text;

namespace MedRePar.Services
{
    internal class PdfService
    {
        public static string ExtractTextFromPdf(string filePath)
        {
            using (PdfReader reader = new PdfReader(filePath))
            {
                StringBuilder text = new StringBuilder();
                for (int i = 1; i <= reader.NumberOfPages; i++)
                {
                    text.Append(PdfTextExtractor.GetTextFromPage(reader, i));
                }
                return text.ToString();
            }
        }
    }
}
