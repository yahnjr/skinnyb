using SkinnyB.Services;
using SkinnyB.Models;
using SkinnyB.Pages;

namespace SkinnyB;

public partial class AppShell : Shell
{
    private readonly GoogleSheetsService _sheetsService;

    public AppShell(GoogleSheetsService sheetsService)
    {
        InitializeComponent();
        _sheetsService = sheetsService;

        Routing.RegisterRoute("burnCalendar", typeof(BurnCalendarPage));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await NavigateToDefaultTabAsync();
    }

    private async Task NavigateToDefaultTabAsync()
    {
        try
        {
            MetaEntry meta = await _sheetsService.GetMetaAsync();

            bool isBurn = string.Equals(
                meta.CurrentMode, "Burn", StringComparison.OrdinalIgnoreCase);

            if (isBurn)
                await GoToAsync("//burn");
        }
        catch (Exception ex)
        {
            // Non-fatal: just stay on the default weekly tab
            System.Diagnostics.Debug.WriteLine($"[AppShell] Could not read mode: {ex.Message}");
        }
    }
}
