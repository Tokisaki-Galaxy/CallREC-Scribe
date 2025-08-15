using CallREC_Scribe.ViewModels;

namespace CallREC_Scribe;

public partial class MainPage : ContentPage
{
    // ViewModel会通过依赖注入自动传入
    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}