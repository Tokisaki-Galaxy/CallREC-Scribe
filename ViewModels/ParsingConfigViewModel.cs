using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;

namespace CallREC_Scribe.ViewModels
{
    public partial class ParsingConfigViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _selectedStrategy;

        [ObservableProperty]
        private string _customRegexPattern;

        [ObservableProperty]
        private bool _isCustomRegexSelected;

        public Action ClosePopupAction { get; set; }

        public ParsingConfigViewModel()
        {
            // 从存储加载当前设置
            SelectedStrategy = Preferences.Get("ParsingStrategy", "Standard");
            CustomRegexPattern = Preferences.Get("CustomParsingRegex", @"^(?<name>.*)@(?<phone>[\d\s\+]+)_(?<date>\d{14})$");
        }

        // 当 SelectedStrategy 变化时，这个方法会被自动调用
        partial void OnSelectedStrategyChanged(string value)
        {
            IsCustomRegexSelected = value == "CustomRegex";
        }

        [RelayCommand]
        private void Save()
        {
            Preferences.Set("ParsingStrategy", SelectedStrategy);
            if (IsCustomRegexSelected)
            {
                Preferences.Set("CustomParsingRegex", CustomRegexPattern);
            }
            ClosePopupAction?.Invoke();
        }

        [RelayCommand]
        private void Cancel()
        {
            ClosePopupAction?.Invoke();
        }
    }
}