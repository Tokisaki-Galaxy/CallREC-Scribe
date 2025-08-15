using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using CallREC_Scribe.Services;
using CallREC_Scribe.ViewModels;
using CallREC_Scribe.Views;

namespace CallREC_Scribe
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif
            // 依赖注入：注册服务和ViewModel
            builder.Services.AddSingleton<IPopupService, PopupService>();
            // 服务通常注册为单例 (Singleton)
            builder.Services.AddSingleton<DatabaseService>();
            builder.Services.AddSingleton<TencentAsrService>();
            builder.Services.AddSingleton<FilenameParsingService>();

            // 页面和ViewModel通常注册为瞬态 (Transient)
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<MainPageViewModel>();

            builder.Services.AddTransientPopup < ParsingConfigPopup, ParsingConfigViewModel>();

            return builder.Build();
        }
    }
}