using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui; // 引入 Community Toolkit
using CallREC_Scribe.Services; // 引入你的服务命名空间
using CallREC_Scribe.ViewModels; // 引入你的ViewModel命名空间

namespace CallREC_Scribe
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit() // 初始化 Community Toolkit
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif
            // 依赖注入：注册服务和ViewModel
            // 服务通常注册为单例 (Singleton)
            builder.Services.AddSingleton<DatabaseService>();
            builder.Services.AddSingleton<TencentAsrService>();

            // 页面和ViewModel通常注册为瞬态 (Transient)
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<MainPageViewModel>();

            return builder.Build();
        }
    }
}