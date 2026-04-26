using SkinnyB.Models;
using SkinnyB.Services;
using SkinnyB.ViewModels;

namespace SkinnyB.Pages;

[QueryProperty(nameof(Burn), "Burn")]
public partial class BurnCalendarPage : ContentPage
{
    private readonly GoogleSheetsService _sheetsService;
    private BurnCalendarViewModel? _vm;

    public BurnCalendarPage(GoogleSheetsService sheetsService)
    {
        InitializeComponent();
        _sheetsService = sheetsService;
    }

    private BurnEntry? _burn;
    public BurnEntry? Burn
    {
        get => _burn;
        set
        {
            _burn = value;
            if (_burn is not null)
            {
                _vm = new BurnCalendarViewModel(_burn, _sheetsService);
                BindingContext = _vm;
            }
        }
    }
}
