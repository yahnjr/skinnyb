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

#if !ANDROID
    LoadEnv();
#endif

            return builder.Build();
        }

        private static void LoadEnv()
        {
#if ANDROID
            // On Android, load from bundled asset
            LoadEnvAsync().GetAwaiter().GetResult();
#else
            // On Windows, load from filesystem relative to project
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, ".env"),
                Path.Combine(AppContext.BaseDirectory, "../../../.env"),
                Path.Combine(AppContext.BaseDirectory, "../../../../.env"),
            };
            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                ParseEnvLines(File.ReadAllLines(path));
                System.Diagnostics.Debug.WriteLine($"[Env] Loaded from {path}");
                return;
            }
            System.Diagnostics.Debug.WriteLine("[Env] .env file not found.");
#endif
        }

#if ANDROID
        private static async Task LoadEnvAsync()
        {
            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("env");
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();
                System.Diagnostics.Debug.WriteLine($"[Env] Raw .env content: {content}");
                var lines = content.Split('\n');
                ParseEnvLines(lines);
                System.Diagnostics.Debug.WriteLine($"[Env] GOOGLE_CLIENT_ID after load: '{Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")}'");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Env] FAILED to load .env: {ex.Message}");
            }
        }
#endif

        private static void ParseEnvLines(IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                    Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
            }
        }
    }
}
