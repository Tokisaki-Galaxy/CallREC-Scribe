using CallREC_Scribe.ViewModels;
using CommunityToolkit.Maui.Views;

namespace CallREC_Scribe.Views;

public partial class ParsingConfigPopup : Popup
{
    public ParsingConfigPopup(ParsingConfigViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;

        // 将 Close 方法与 ViewModel 的命令关联起来
        viewModel.ClosePopupAction = () => Close();
    }
}