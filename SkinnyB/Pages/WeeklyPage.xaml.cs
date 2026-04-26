using SkinnyB.Controls;
using SkinnyB.Models;
using SkinnyB.ViewModels;

namespace SkinnyB.Pages;

public partial class WeeklyPage : ContentPage
{
	private readonly WeeklyViewModel _vm;
	private EditableCell _activeCell;

	public WeeklyPage(WeeklyViewModel viewModel)
	{
		InitializeComponent();
		_vm = viewModel;
		BindingContext = _vm;

		var pageTap = new TapGestureRecognizer();
		pageTap.Tapped += OnPageTapped;
		Content.GestureRecognizers.Add(pageTap);
	}

	// Interaction handlers
	private void OnPageTapped(object? sender, TappedEventArgs e)
	{
		_activeCell?.Deselect();
		_activeCell = null;
	}

	private void OnCellSelected(object? sender, EventArgs e)
	{
		if (sender is not EditableCell cell) return;
		if (_activeCell != null & _activeCell != cell)
			_activeCell.Deselect();

		_activeCell = cell;
	}

	private async void OnCellValueChanged(object? sender, EventArgs e)
	{
		if (sender is not SkinnyB.Controls.EditableCell cell) return;
		if (cell.BindingContext is not WeekEntry entry) return;

		await _vm.SaveEntryAsync(entry);
	}

    // Button handlers
    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        await _vm.SaveAllAsync();
    }

    protected override async void OnAppearing()
	{
		base.OnAppearing();

		try
		{
            if (_vm.Weeks.Count == 0)
                await _vm.LoadDataAsync();

            // Auto-scroll to bottom after data loads (newest week at bottom)
            await Task.Delay(100);
            await WeeksScrollView.ScrollToAsync(0, double.MaxValue, false);
        }
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[WeeklyPage] {ex}");
		}
	}
}