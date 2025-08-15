using CallREC_Scribe.ViewModels;

namespace CallREC_Scribe;

public partial class MainPage : ContentPage
{
    // 将 viewModel 提升为类的字段，以便在 OnAppearing 中访问
    private readonly MainPageViewModel _viewModel;

    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    // 重写页面的 OnAppearing 生命周期方法
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // 当页面即将显示时，调用 ViewModel 的异步初始化方法
        // 这会加载初始数据
        await _viewModel.InitializeAsync();
    }
}