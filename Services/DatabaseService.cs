using SQLite;
using CallREC_Scribe.Models;

namespace CallREC_Scribe.Services
{
    public class DatabaseService
    {
        private SQLiteAsyncConnection _database;
        private bool _isInitialized = false;

        private async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            // 数据库文件将保存在应用的安全数据目录中
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "Recordings.db3");
            _database = new SQLiteAsyncConnection(dbPath);
            await _database.CreateTableAsync<RecordingFile>();
            _isInitialized = true;
        }

        public async Task<RecordingFile> GetRecordingAsync(string filePath)
        {
            await InitializeAsync();
            return await _database.Table<RecordingFile>().Where(r => r.FilePath == filePath).FirstOrDefaultAsync();
        }

        public async Task SaveRecordingAsync(RecordingFile recording)
        {
            await InitializeAsync();
            // Upsert: 如果已存在则更新，否则插入
            await _database.InsertOrReplaceAsync(recording);
        }

        public async Task<List<RecordingFile>> GetAllRecordingsAsync()
        {
            await InitializeAsync();
            return await _database.Table<RecordingFile>().ToListAsync();
        }

        public async Task DeleteRecordingAsync(RecordingFile recording)
        {
            await InitializeAsync();
            await _database.DeleteAsync(recording);
        }
    }
}