using CallREC_Scribe.Models;
using System.Text.RegularExpressions;

namespace CallREC_Scribe.Services
{
    public class FilenameParsingService
    {
        // 内部解析器类：新的、更智能的 StandardParser
        private class StandardParser : IFileNameParser
        {
            public ParsingResult Parse(string fileName)
            {
                var parts = fileName.Split('_');
                if (parts.Length != 2) return new ParsingResult { Success = false };

                var dateParseSuccess = DateTime.TryParseExact(parts[1], "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var date);
                if (!dateParseSuccess) return new ParsingResult { Success = false };

                var nameAndPhonePart = parts[0];
                string contactName = null;
                string phoneNumber;

                // 核心逻辑：检查是否存在 '@' 分隔符
                if (nameAndPhonePart.Contains('@'))
                {
                    var nameAndPhone = nameAndPhonePart.Split(new[] { '@' }, 2); // 只分割一次
                    if (nameAndPhone.Length == 2)
                    {
                        contactName = nameAndPhone[0].Trim();
                        phoneNumber = nameAndPhone[1].Trim();
                    }
                    else // 格式不正确，例如 "abc@"
                    {
                        phoneNumber = nameAndPhonePart.Trim();
                    }
                }
                else // 不存在 '@'，则全部视为电话号码
                {
                    phoneNumber = nameAndPhonePart.Trim();
                }

                return new ParsingResult
                {
                    Success = true,
                    PhoneNumber = phoneNumber,
                    RecordingDate = date,
                    ContactName = contactName
                };
            }
        }

        // 内部解析器类：自定义Regex模式 (保持不变)
        private class CustomRegexParser : IFileNameParser
        {
            private readonly string _pattern;
            public CustomRegexParser(string pattern)
            {
                _pattern = pattern;
            }

            public ParsingResult Parse(string fileName)
            {
                try
                {
                    var match = Regex.Match(fileName, _pattern);
                    if (!match.Success) return new ParsingResult { Success = false };

                    var dateStr = match.Groups["date"].Value;
                    var phoneStr = match.Groups["phone"].Value;
                    var nameStr = match.Groups["name"]?.Value;

                    var dateParseSuccess = DateTime.TryParseExact(dateStr, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var date);
                    if (!dateParseSuccess || string.IsNullOrEmpty(phoneStr)) return new ParsingResult { Success = false };

                    return new ParsingResult
                    {
                        Success = true,
                        PhoneNumber = phoneStr.Trim(),
                        RecordingDate = date,
                        ContactName = nameStr?.Trim()
                    };
                }
                catch (ArgumentException)
                {
                    return new ParsingResult { Success = false };
                }
            }
        }

        // 公共方法，根据存储的设置选择解析器
        public ParsingResult Parse(string fileName)
        {
            // 如果用户之前选择了 "ContactName"，它会自动回退到 "Standard"
            var strategy = Preferences.Get("ParsingStrategy", "Standard");
            var fileNameOnly = Path.GetFileNameWithoutExtension(fileName);

            IFileNameParser parser;
            switch (strategy)
            {
                // case "ContactName": <-- 我们删除了这个 case
                case "CustomRegex":
                    var pattern = Preferences.Get("CustomParsingRegex", string.Empty);
                    if (string.IsNullOrEmpty(pattern)) return new ParsingResult { Success = false };
                    parser = new CustomRegexParser(pattern);
                    break;
                default: // Standard
                    parser = new StandardParser();
                    break;
            }

            return parser.Parse(fileNameOnly);
        }
    }
}