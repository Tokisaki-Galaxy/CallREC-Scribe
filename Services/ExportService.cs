using CallREC_Scribe.Models;
using ClosedXML.Excel;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xceed.Document.NET;
using Xceed.Words.NET;

namespace CallREC_Scribe.Services
{
    public class ExportService
    {
        // 公共方法，根据文件数量决定调用哪个具体的导出方法
        public async Task<string?> ExportFilesAsync(List<RecordingFile> files)
        {
            if (files == null || files.Count == 0)
            {
                return null;
            }

            if (files.Count == 1)
            {
                return await CreateWordDocumentAsync(files[0]);
            }
            else
            {
                return await CreateExcelWorkbookAsync(files);
            }
        }

        // 私有方法：创建 Word 文档
        private async Task<string> CreateWordDocumentAsync(RecordingFile file)
        {
            // 在应用的缓存目录中创建一个临时文件
            string tempFilePath = Path.Combine(FileSystem.CacheDirectory, $"Export_{file.PhoneNumber}_{file.RecordingDate:yyyyMMdd}.docx");

            // 使用 DocX 库创建文档
            using (var doc = DocX.Create(tempFilePath))
            {
                doc.InsertParagraph("通话录音转录").Bold().FontSize(16).Alignment = Alignment.center;
                doc.InsertParagraph(); // 空行

                doc.InsertParagraph($"日期:").Bold().Append($"\t{file.RecordingDate:yyyy-MM-dd HH:mm:ss}");
                doc.InsertParagraph($"号码/联系人:").Bold().Append($"\t{file.PhoneNumber}");
                doc.InsertParagraph();

                doc.InsertParagraph("转录内容:").Bold();
                // 确保转录内容不为空
                string transcription = string.IsNullOrEmpty(file.TranscriptionPreview) ? "（无转录内容）" : file.TranscriptionPreview;
                doc.InsertParagraph(transcription);

                doc.Save();
            }

            return tempFilePath;
        }

        // 私有方法：创建 Excel 工作簿
        private async Task<string> CreateExcelWorkbookAsync(List<RecordingFile> files)
        {
            string tempFilePath = Path.Combine(FileSystem.CacheDirectory, $"Export_Multiple_{DateTime.Now:yyyyMMddHHmmss}.xlsx");

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("录音转录");

                // 创建表头
                worksheet.Cell("A1").Value = "日期";
                worksheet.Cell("B1").Value = "号码/联系人";
                worksheet.Cell("C1").Value = "转录内容";
                worksheet.Row(1).Style.Font.Bold = true;

                // 填充数据行
                int currentRow = 2;
                foreach (var file in files)
                {
                    worksheet.Cell(currentRow, 1).Value = file.RecordingDate;
                    worksheet.Cell(currentRow, 2).Value = file.PhoneNumber;
                    string transcription = string.IsNullOrEmpty(file.TranscriptionPreview) ? "（无转录内容）" : file.TranscriptionPreview;
                    worksheet.Cell(currentRow, 3).Value = transcription;
                    currentRow++;
                }

                // 调整列宽
                worksheet.Columns().AdjustToContents();

                workbook.SaveAs(tempFilePath);
            }

            return tempFilePath;
        }
    }
}