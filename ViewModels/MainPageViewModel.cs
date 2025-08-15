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

        [ObservableProperty]
        private string _recordingsFolder;

        [ObservableProperty]
        private bool _isBusy = false; // 用于控制加载指示器

        public ObservableCollection<RecordingFile> RecordingFiles { get; } = new();

        public MainPageViewModel(
            DatabaseService dbService, TencentAsrService asrService,
            FilenameParsingService parsingService, IServiceProvider serviceProvider, IPopupService popupService)
        {
            _dbService = dbService;
            _asrService = asrService;
            _parsingService = parsingService;
            _serviceProvider = serviceProvider;
            _popupService = popupService;

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
            string id = await App.Current.MainPage.DisplayPromptAsync("腾讯云配置", "请输入 Secret ID:", initialValue: Preferences.Get("TencentSecretId", ""));
            if (id != null)
            {
                Preferences.Set("TencentSecretId", id);
            }

            string key = await App.Current.MainPage.DisplayPromptAsync("腾讯云配置", "请输入 Secret Key:", initialValue: Preferences.Get("TencentSecretKey", ""));
            if (key != null)
            {
                Preferences.Set("TencentSecretKey", key);
            }

            if (id != null && key != null)
            {
                await App.Current.MainPage.DisplayAlert("成功", "API密钥已保存。", "好的");
            }
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
                var result = await _asrService.TranscribeAsync(file.FilePath, secretId, secretKey);
                file.TranscriptionPreview = result;
                await _dbService.SaveRecordingAsync(file);
            }

            IsBusy = false;
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
            // 注意：我们不再在这里清空列表，以防加载中途失败导致白屏
            try
            {
                if (Directory.Exists(RecordingsFolder))
                {
                    // --- 修改点 1: 创建一个临时的 List 来收集文件 ---
                    // 我们不再直接向 ObservableCollection 中添加，以避免不必要的UI更新
                    var loadedFiles = new List<RecordingFile>();


                    var allowedExtensions = new[] { ".mp3", ".m4a" };
                    var files = Directory.EnumerateFiles(RecordingsFolder)
                        .Where(file => allowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                        .ToList();

                    foreach (var file in files)
                    {
                        var parsingResult = _parsingService.Parse(file);

                        var recording = new RecordingFile();
                        if (parsingResult.Success)
                        {
                            recording.PhoneNumber = parsingResult.PhoneNumber;
                            recording.RecordingDate = parsingResult.RecordingDate;
                            if (!string.IsNullOrEmpty(parsingResult.ContactName))
                            {
                                recording.PhoneNumber = $"{parsingResult.ContactName} ({parsingResult.PhoneNumber})";
                            }
                        }
                        else
                        {
                            recording.PhoneNumber = Path.GetFileNameWithoutExtension(file);
                            recording.RecordingDate = File.GetLastWriteTime(file);
                        }

                        recording.FilePath = file;
                        recording.TranscriptionPreview = string.Empty;

                        var saved = await _dbService.GetRecordingAsync(file);
                        if (saved != null && !string.IsNullOrEmpty(saved.TranscriptionPreview))
                        {
                            recording.TranscriptionPreview = saved.TranscriptionPreview;
                        }

                        // --- 修改点 2: 将新对象添加到临时 List 中 ---
                        loadedFiles.Add(recording);
                    }

                    // --- 修改点 3: 在所有文件加载完毕后，对临时 List 进行排序 ---
                    // 使用 LINQ 的 OrderByDescending 方法按日期倒序排列
                    var sortedFiles = loadedFiles.OrderByDescending(f => f.RecordingDate).ToList();

                    // --- 修改点 4: 清空并一次性填充 UI 绑定的 ObservableCollection ---
                    RecordingFiles.Clear();
                    foreach (var file in sortedFiles)
                    {
                        RecordingFiles.Add(file);
                    }
                }
                else
                {
                    // 如果文件夹不存在，也要确保列表是空的
                    RecordingFiles.Clear();
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
    }
}