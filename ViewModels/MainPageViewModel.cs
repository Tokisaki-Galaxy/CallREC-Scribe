using CallREC_Scribe.Models;
using CallREC_Scribe.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace CallREC_Scribe.ViewModels
{
    public partial class MainPageViewModel : ObservableObject
    {
        private readonly DatabaseService _dbService;
        private readonly TencentAsrService _asrService;

        [ObservableProperty]
        private string _recordingsFolder;

        [ObservableProperty]
        private bool _isBusy = false; // 用于控制加载指示器

        public ObservableCollection<RecordingFile> RecordingFiles { get; } = new();

        public MainPageViewModel(DatabaseService dbService, TencentAsrService asrService)
        {
            _dbService = dbService;
            _asrService = asrService;

            // 从本地配置加载保存的路径和API密钥
            RecordingsFolder = Preferences.Get("RecordingsFolder", "请选择文件夹...");
            LoadRecordings();
        }

        [RelayCommand]
        private async Task BrowseFolderAsync()
        {
#if WINDOWS
    try
    {
        // 1. 创建 Windows 平台原生的文件夹选择器
        var folderPicker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop
        };
        folderPicker.FileTypeFilter.Add("*"); // 允许选择任何文件夹

        // 2. 这是在 WinUI 3 (MAUI Windows 的底层) 中显示对话框所必需的样板代码
        // 它将选择器与当前应用的主窗口关联起来
        var window = App.Current.Windows.FirstOrDefault()?.Handler.PlatformView as MauiWinUIWindow;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

        // 3. 异步显示文件夹选择器并等待用户选择
        var result = await folderPicker.PickSingleFolderAsync();

        if (result != null)
        {
            // 4. 如果用户选择了文件夹，更新路径并加载文件
            RecordingsFolder = result.Path;
            Preferences.Set("RecordingsFolder", RecordingsFolder);
            LoadRecordings();
        }
    }
    catch (Exception ex)
    {
        await App.Current.MainPage.DisplayAlert("错误", $"选择文件夹失败: {ex.Message}", "好的");
    }

#else
            try
            {
#if ANDROID
                var status = await Permissions.RequestAsync<Permissions.StorageRead>();
                if (status != PermissionStatus.Granted)
                {
                    await App.Current.MainPage.DisplayAlert("权限错误", "需要存储读取权限才能浏览文件。", "好的");
                    return;
                }
#endif
                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "选择任意一个录音文件以确定文件夹"
                });

                if (result != null)
                {
                    RecordingsFolder = Path.GetDirectoryName(result.FullPath);
                    Preferences.Set("RecordingsFolder", RecordingsFolder);
                    LoadRecordings();
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

        private void LoadRecordings()
        {
            IsBusy = true;
            RecordingFiles.Clear();
            try
            {
                if (Directory.Exists(RecordingsFolder))
                {
                    var files = Directory.GetFiles(RecordingsFolder, "*.mp3");
                    foreach (var file in files)
                    {
                        // 尝试从文件名解析信息，你需要根据你的文件名格式修改这里
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var parts = fileName.Split('_');

                        var recording = new RecordingFile
                        {
                            FilePath = file,
                            // 这是一个示例解析，你需要修改
                            RecordingDate = parts.Length > 0 ? ParseDate(parts[0]) : File.GetCreationTime(file),
                            PhoneNumber = parts.Length > 1 ? parts[1] : "未知号码",
                            TranscriptionPreview = "待转录"
                        };

                        // 从数据库加载已有的转录
                        var saved = _dbService.GetRecordingAsync(file).Result;
                        if (saved != null && !string.IsNullOrEmpty(saved.TranscriptionPreview))
                        {
                            recording.TranscriptionPreview = saved.TranscriptionPreview;
                        }

                        RecordingFiles.Add(recording);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Current.MainPage.DisplayAlert("错误", $"加载文件失败: {ex.Message}", "好的");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // 示例日期解析函数，你需要根据实际情况修改
        private DateTime ParseDate(string dateStr)
        {
            if (DateTime.TryParseExact(dateStr, "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out var result))
            {
                return result;
            }
            return DateTime.MinValue;
        }
    }
}