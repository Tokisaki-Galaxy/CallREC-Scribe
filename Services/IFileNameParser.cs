using CallREC_Scribe.Models;

namespace CallREC_Scribe.Services
{
    public interface IFileNameParser
    {
        ParsingResult Parse(string fileName);
    }
}