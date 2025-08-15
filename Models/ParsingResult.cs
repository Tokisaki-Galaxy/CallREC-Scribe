namespace CallREC_Scribe.Models
{
    public class ParsingResult
    {
        public bool Success { get; set; }
        public string PhoneNumber { get; set; }
        public DateTime RecordingDate { get; set; }
        public string ContactName { get; set; } // 可能为空
    }
}