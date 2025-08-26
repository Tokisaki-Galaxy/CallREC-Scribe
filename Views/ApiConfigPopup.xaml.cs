using CallREC_Scribe.ViewModels;
using CommunityToolkit.Maui.Views;

namespace CallREC_Scribe.Views
{
    public partial class ApiConfigPopup : Popup
    {
        public ApiConfigPopup(ApiConfigViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;

            // 将弹窗自身的 Close 方法与 ViewModel 的关闭委托关联
            viewModel.ClosePopupAction = () => Close();
        }
    }
}