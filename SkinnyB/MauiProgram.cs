using Microsoft.Extensions.Logging;
using SkinnyB.Services;
using SkinnyB.Pages;
using SkinnyB.ViewModels;

namespace SkinnyB
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });


            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddSingleton<App>();
            builder.Services.AddSingleton<GoogleSheetsService>();
            builder.Services.AddTransient<WeeklyViewModel>();
            builder.Services.AddSingleton<BurnViewModel>();
            builder.Services.AddSingleton<StatsViewModel>();
            builder.Services.AddTransient<WeeklyPage>();
            builder.Services.AddTransient<BurnPage>();
            builder.Services.AddSingleton<StatsPage>();
            builder.Services.AddTransient<BurnCalendarPage>();
#if DEBUG
    		builder.Logging.AddDebug();

#endif

            return builder.Build();
        }
    }
}
