using SkinnyB.Controls;
using SkinnyB.ViewModels;

namespace SkinnyB.Pages;

public partial class StatsPage : ContentPage
{
    private readonly StatsViewModel _vm;
    private readonly WeightGraphDrawable _drawable = new();

    public StatsPage(StatsViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        BindingContext = _vm;

        UpdateDrawableTheme();
        WeightGraph.Drawable = _drawable;

        // Redraw the graph whenever WeightHistory changes
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(StatsViewModel.WeightHistory)
                              or nameof(StatsViewModel.HasWeightData))
            {
                _drawable.Points = _vm.WeightHistory;
                WeightGraph.Invalidate();
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateDrawableTheme();

        try
        {
            if (_vm.WeightHistory.Count == 0)
                await _vm.LoadDataAsync();
            else
                WeightGraph.Invalidate(); 
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StatsPage] {ex}");
        }
    }

    // Keeps graphu up to date with theme changes
    private void UpdateDrawableTheme()
    {
        bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

        _drawable.LineColor = Microsoft.Maui.Graphics.Color.FromArgb("#2D6A4F");
        _drawable.DotColor = Microsoft.Maui.Graphics.Color.FromArgb("#52B788");
        _drawable.FillColor = Microsoft.Maui.Graphics.Color.FromArgb("#1A2D6A4F");
        _drawable.AxisColor = Microsoft.Maui.Graphics.Color.FromArgb(isDark ? "#444442" : "#CCCCCC");
        _drawable.LabelColor = Microsoft.Maui.Graphics.Color.FromArgb(isDark ? "#888880" : "#666660");
    }
}