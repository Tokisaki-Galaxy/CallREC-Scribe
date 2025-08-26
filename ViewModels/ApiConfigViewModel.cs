// ViewModels/ApiConfigViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CallREC_Scribe.ViewModels
{
    public partial class ApiConfigViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _secretId;

        [ObservableProperty]
        private string _secretKey;

        // --- 新增属性 ---
        [ObservableProperty]
        private string _selectedEngineModel;

        public Action ClosePopupAction { get; set; }

        public ApiConfigViewModel()
        {
            // 从存储中加载当前已保存的设置
            SecretId = Preferences.Get("TencentSecretId", string.Empty);
            SecretKey = Preferences.Get("TencentSecretKey", string.Empty);

            // --- 加载引擎设置，如果不存在，则默认为 "8k_zh" ---
            SelectedEngineModel = Preferences.Get("TencentEngineModel", "8k_zh");
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            // 保存所有设置到持久化存储
            Preferences.Set("TencentSecretId", SecretId);
            Preferences.Set("TencentSecretKey", SecretKey);
            // --- 保存新的引擎设置 ---
            Preferences.Set("TencentEngineModel", SelectedEngineModel);

            await App.Current.MainPage.DisplayAlert("成功", "API 配置已保存。", "好的");

            ClosePopupAction?.Invoke();
        }

        [RelayCommand]
        private void Cancel()
        {
            ClosePopupAction?.Invoke();
        }
    }
}