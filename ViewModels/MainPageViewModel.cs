using CallREC_Scribe.Models;
using CallREC_Scribe.Services;
using CallREC_Scribe.Views;
using CallREC_Scribe.ViewModels;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
#if ANDROID
using Android.Content;
#endif

namespace CallREC_Scribe.ViewModels
{
    public partial class MainPageViewModel : ObservableObject
    {
        private readonly DatabaseService _dbService;
        private readonly TencentAsrService _asrService;
        private readonly FilenameParsingService _parsingService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IPopupService _popupService;
        private readonly ExportService _exportService;

        [ObservableProperty]
        private string _recordingsFolder;

        [ObservableProperty]
        private bool _isBusy = false; // 用于控制加载指示器

        [ObservableProperty]
        private string _currentTaskDescription;

        public ObservableCollection<RecordingFile> RecordingFiles { get; } = new();

        public MainPageViewModel(
            DatabaseService dbService, TencentAsrService asrService,
            FilenameParsingService parsingService, IServiceProvider serviceProvider, IPopupService popupService, ExportService exportService)
        {
            _dbService = dbService;
            _asrService = asrService;
            _parsingService = parsingService;
            _serviceProvider = serviceProvider;
            _popupService = popupService;
            _exportService = exportService;

            // 从本地配置加载保存的路径和API密钥
            RecordingsFolder = Preferences.Get("RecordingsFolder", "请选择文件夹...");
        }
        public async Task InitializeAsync()
        {
            await LoadRecordingsAsync();
        }

        [RelayCommand]
        private async Task BrowseFolderAsync()
        {
#if ANDROID
            try
            {
                // 1. 创建一个 Intent 来请求打开文件夹选择器
                var intent = new Intent(Intent.ActionOpenDocumentTree);

                // 2. 创建一个 TaskCompletionSource，它会等待 OnActivityResult 的结果
                MainActivity.PickFolderTaskCompletionSource = new TaskCompletionSource<string>();

                // 3. 启动文件夹选择器 Activity
                // 我们使用了 Platform.CurrentActivity 来获取当前的 Android Activity 实例
                Platform.CurrentActivity.StartActivityForResult(intent, MainActivity.PickFolderRequestCode);

                // 4. 异步等待 TaskCompletionSource 被完成 (在 OnActivityResult 中完成)
                string folderPath = await MainActivity.PickFolderTaskCompletionSource.Task;

                // 5. 如果用户成功选择了文件夹 (路径不为 null)
                if (!string.IsNullOrWhiteSpace(folderPath))
                {
                    RecordingsFolder = folderPath;
                    Preferences.Set("RecordingsFolder", RecordingsFolder);
                    await LoadRecordingsAsync();
                }
            }
            catch (Exception ex)
            {
                await App.Current.MainPage.DisplayAlert("错误", $"选择文件夹失败: {ex.Message}", "好的");
            }

#elif WINDOWS
    // --- Windows 平台的逻辑保持不变 ---
    try
    {
        var folderPicker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop
        };
        folderPicker.FileTypeFilter.Add("*");

        var window = App.Current.Windows.FirstOrDefault()?.Handler.PlatformView as MauiWinUIWindow;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

        var result = await folderPicker.PickSingleFolderAsync();

        if (result != null)
        {
            RecordingsFolder = result.Path;
            Preferences.Set("RecordingsFolder", RecordingsFolder);
            await LoadRecordingsAsync();
        }
    }
    catch (Exception ex)
    {
        await App.Current.MainPage.DisplayAlert("错误", $"选择文件夹失败: {ex.Message}", "好的");
    }
#endif
        }

        [RelayCommand]
        private async Task ConfigureApiAsync()
        {
            // 使用 IServiceProvider 来获取我们刚刚创建的弹窗实例
            var popup = _serviceProvider.GetRequiredService<ApiConfigPopup>();

            // 显示弹窗
            await App.Current.MainPage.ShowPopupAsync(popup);
        }

        [RelayCommand]
        private async Task TranscribeSelectedAsync()
        {
            var selectedFiles = RecordingFiles.Where(f => f.IsSelected).ToList();
            if (!selectedFiles.Any())
            {
                await App.Current.MainPage.DisplayAlert("提示", "请至少选择一个文件进行转译。", "好的");
                return;
            }

            var alreadyTranscribed = selectedFiles.Count(f => !string.IsNullOrEmpty(f.TranscriptionPreview));
            if (alreadyTranscribed > 0)
            {
                bool retranscribe = await App.Current.MainPage.DisplayAlert("确认",
                    $"{alreadyTranscribed} 个文件已有转录内容。您要重新转译这些文件吗？",
                    "重新转译", "跳过");

                if (!retranscribe)
                {
                    selectedFiles = selectedFiles.Where(f => string.IsNullOrEmpty(f.TranscriptionPreview)).ToList();
                }
            }

            if (!selectedFiles.Any()) return;

            IsBusy = true;
            CurrentTaskDescription = "准备开始转译...";
            var secretId = Preferences.Get("TencentSecretId", string.Empty);
            var secretKey = Preferences.Get("TencentSecretKey", string.Empty);

            if (string.IsNullOrEmpty(secretId) || string.IsNullOrEmpty(secretKey))
            {
                await App.Current.MainPage.DisplayAlert("错误", "请先配置腾讯云的 Secret ID 和 Secret Key。", "好的");
                IsBusy = false;
                return;
            }

            foreach (var file in selectedFiles)
            {
                CurrentTaskDescription = $"正在处理: {Path.GetFileName(file.FilePath)}";
                var result = await _asrService.TranscribeAsync(file.FilePath, secretId, secretKey);
                file.TranscriptionPreview = result;
                await _dbService.SaveRecordingAsync(file);
            }

            IsBusy = false;
            CurrentTaskDescription = string.Empty;
            await App.Current.MainPage.DisplayAlert("完成", "选定文件的转译已完成。", "好的");
        }

        [RelayCommand]
        private void SelectAll()
        {
            foreach (var file in RecordingFiles)
            {
                file.IsSelected = true;
            }
        }

        [RelayCommand]
        private void InvertSelection()
        {
            foreach (var file in RecordingFiles)
            {
                file.IsSelected = !file.IsSelected;
            }
        }

        [RelayCommand]
        private async Task ConfigureParsingAsync()
        {
            try
            {
                // 1. ★★★ 核心修正：不再创建 Popup，而是创建 Popup 的 ViewModel ★★★
                // 我们从 DI 容器只获取 ViewModel 实例。
                var viewModel = _serviceProvider.GetRequiredService<ParsingConfigViewModel>();

                // 2. ★★★ 将 ViewModel 实例传递给 ShowPopupAsync ★★★
                // Community Toolkit 的 PopupService 会自动查找在 MauiProgram.cs 中注册的、
                // 与 ParsingConfigViewModel 相关联的 View（也就是 ParsingConfigPopup），
                // 然后为我们创建并显示它。
                // 这种方式完全没有方法重载的歧义。
                await _popupService.ShowPopupAsync(viewModel);

                // 3. 弹窗关闭后，重新加载文件列表以应用新的解析规则
                await LoadRecordingsAsync();
            }
            catch (Exception ex)
            {
                await App.Current.MainPage.DisplayAlert("错误", ex.ToString(), "好的");
            }
        }

        private async Task LoadRecordingsAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                var filesOnDisk = new HashSet<string>();
                if (Directory.Exists(RecordingsFolder))
                {
                    var allowedExtensions = new[] { ".mp3", ".m4a" ,".wav", ".pcm", ".ogg"};
                    var files = Directory.EnumerateFiles(RecordingsFolder)
                        .Where(file => allowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()));
                    filesOnDisk = new HashSet<string>(files);
                }

                // --- 获取数据库中所有已知的录音记录 ---
                var recordingsInDb = await _dbService.GetAllRecordingsAsync();

                // --- 创建一个临时的列表来存放最终要显示的所有记录 ---
                var allRecordingsToShow = new List<RecordingFile>();

                // --- 优先处理数据库中的记录，并验证其文件是否存在 ---
                foreach (var dbRecord in recordingsInDb)
                {
                    if (filesOnDisk.Contains(dbRecord.FilePath))
                    {
                        // 情况 1: 文件正常存在
                        // 数据库记录有效，直接使用。
                        allRecordingsToShow.Add(dbRecord);
                        // 从待处理的文件列表中移除，剩下的就是“新文件”。
                        filesOnDisk.Remove(dbRecord.FilePath);
                    }
                    else
                    {
                        // 情况 2: 文件已被删除
                        // 修改显示内容，然后添加到最终列表。
                        dbRecord.PhoneNumber = $"(已删除) {dbRecord.PhoneNumber}";
                        allRecordingsToShow.Add(dbRecord);
                    }
                }

                // --- 处理文件系统中剩余的“新文件” (即不在数据库中的文件) ---
                foreach (var newFilePath in filesOnDisk)
                {
                    // 情况 3: 新文件
                    var parsingResult = _parsingService.Parse(newFilePath);
                    var newRecording = new RecordingFile();

                    if (parsingResult.Success)
                    {
                        newRecording.PhoneNumber = parsingResult.PhoneNumber;
                        newRecording.RecordingDate = parsingResult.RecordingDate;
                        if (!string.IsNullOrEmpty(parsingResult.ContactName))
                        {
                            newRecording.PhoneNumber = $"{parsingResult.ContactName} ({parsingResult.PhoneNumber})";
                        }
                    }
                    else
                    {
                        // 解析失败的处理
                        newRecording.PhoneNumber = Path.GetFileNameWithoutExtension(newFilePath);
                        newRecording.RecordingDate = File.GetLastWriteTime(newFilePath);
                    }

                    newRecording.FilePath = newFilePath;
                    newRecording.TranscriptionPreview = string.Empty; // 新文件没有转录

                    allRecordingsToShow.Add(newRecording);
                    // 关键：将新发现的文件存入数据库，以便下次加载时能识别
                    await _dbService.SaveRecordingAsync(newRecording);
                }

                // --- 对最终的完整列表进行排序，并一次性更新到UI ---
                // 先排序后更新
                var sortedFiles = allRecordingsToShow.OrderByDescending(f => f.RecordingDate).ToList();

                RecordingFiles.Clear();
                foreach (var file in sortedFiles)
                {
                    RecordingFiles.Add(file);
                }
            }
            catch (Exception ex)
            {
                await App.Current.MainPage.DisplayAlert("错误", $"加载文件失败: {ex.Message}", "好的");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // 示例日期解析函数，你需要根据实际情况修改
        private DateTime ParseDate(string dateStr)
        {
            if (DateTime.TryParseExact(dateStr, "yyyyMMdd_HHmmss", null, System.Globalization.DateTimeStyles.None, out var result))
            {
                return result;
            }
            return DateTime.MinValue;
        }

        [RelayCommand]
        private async Task ExportAsync()
        {
            var selectedFiles = RecordingFiles.Where(f => f.IsSelected).ToList();
            if (selectedFiles.Count == 0)
            {
                await App.Current.MainPage.DisplayAlert("提示", "请至少选择一个文件进行导出。", "好的");
                return;
            }

            IsBusy = true;
            try
            {
                // 1. 调用服务生成文件，并获取临时文件路径
                string filePath = await _exportService.ExportFilesAsync(selectedFiles);

                if (string.IsNullOrEmpty(filePath))
                {
                    await App.Current.MainPage.DisplayAlert("错误", "创建导出文件失败。", "好的");
                    return;
                }

                // 2. 使用 MAUI Essentials 的 Share 功能将文件分享/保存
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "导出录音转录",
                    File = new ShareFile(filePath)
                });
            }
            catch (Exception ex)
            {
                await App.Current.MainPage.DisplayAlert("导出失败", $"发生错误: {ex.Message}", "好的");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task DeleteSelectedAsync()
        {
            var selectedFiles = RecordingFiles.Where(f => f.IsSelected).ToList();
            if (selectedFiles.Count == 0)
            {
                await App.Current.MainPage.DisplayAlert("提示", "请至少选择一个条目进行删除。", "好的");
                return;
            }

            // --- 核心修改：使用 DisplayActionSheet 来提供多个选项 ---

            // 1. 定义我们希望用户看到的选项文字
            const string deleteBothAction = "删除记录和文件";      // 破坏性最强的选项
            const string deleteRecordOnlyAction = "仅删除此记录"; // 保留文件的选项
            const string cancelAction = "取消";

            // 2. 调用 DisplayActionSheet
            // 它会返回用户点击的按钮的文字
            string userChoice = await App.Current.MainPage.DisplayActionSheet(
                "确认删除",            // 标题
                cancelAction,            // 取消按钮的文字
                deleteBothAction,        // 破坏性操作按钮的文字 (在iOS上通常会显示为红色)
                deleteRecordOnlyAction   // 其他选项按钮
            );

            // 3. 根据用户的选择来决定下一步操作
            if (userChoice == null || userChoice == cancelAction)
            {
                return; // 用户取消了操作，直接返回
            }

            bool shouldDeleteFile = (userChoice == deleteBothAction);

            IsBusy = true;
            try
            {
                foreach (var fileToDelete in selectedFiles)
                {
                    // 步骤 A: 从数据库中删除记录 (这一步总是执行)
                    await _dbService.DeleteRecordingAsync(fileToDelete);

                    // 步骤 B: 根据用户的选择，决定是否删除物理文件
                    if (shouldDeleteFile)
                    {
                        try
                        {
                            if (File.Exists(fileToDelete.FilePath))
                            {
                                File.Delete(fileToDelete.FilePath);
                            }
                        }
                        catch (Exception ex)
                        {
                            // 如果文件删除失败，在控制台输出日志，但继续处理下一条记录
                            Console.WriteLine($"Failed to delete file {fileToDelete.FilePath}: {ex.Message}");
                        }
                    }

                    // 步骤 C: 从UI集合中移除该项 (这一步也总是执行)
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        RecordingFiles.Remove(fileToDelete);
                    });
                }
            }
            catch (Exception ex)
            {
                await App.Current.MainPage.DisplayAlert("删除失败", $"发生错误: {ex.Message}", "好的");
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}