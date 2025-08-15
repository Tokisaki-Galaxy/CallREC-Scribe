using CommunityToolkit.Mvvm.ComponentModel;
using SQLite;

namespace CallREC_Scribe.Models
{
    // 使用 ObservableObject 可以让UI在属性变化时自动更新
    public partial class RecordingFile : ObservableObject
    {
        [PrimaryKey]
        public string FilePath { get; set; } // 文件路径作为主键

        public DateTime RecordingDate { get; set; }
        public string PhoneNumber { get; set; }

        [ObservableProperty] // 当这个属性变化时，UI会自动更新
        private string _transcriptionPreview;

        private bool _isSelected;

        // 将 [Ignore] 特性应用在公共属性上
        [Ignore]
        public bool IsSelected
        {
            get => _isSelected;
            // 使用 ObservableObject 提供的 SetProperty 方法来通知UI更新
            set => SetProperty(ref _isSelected, value);
        }
    }
}