using SkinnyB.Models;
using SkinnyB.ViewModels;

namespace SkinnyB.Pages;

public partial class BurnPage : ContentPage
{
    private readonly BurnViewModel _vm;

    public BurnPage(BurnViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            if (_vm.Burns.Count == 0)
                await _vm.LoadDataAsync();

            // If in burn mode, auto-navigate to the active burn's calendar page
            if (_vm.IsInBurnMode && _vm.DefaultOpenBurn is not null)
            {
                await Task.Delay(200);
                await Shell.Current.GoToAsync("burnCalendar", new Dictionary<string, object>
                {
                    { "Burn", _vm.DefaultOpenBurn }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BurnPage] {ex}");
        }
    }
}
