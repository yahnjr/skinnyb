using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SkinnyB.Models;
using SkinnyB.Services;

namespace SkinnyB.ViewModels;

public class BurnViewModel : INotifyPropertyChanged
{
    private readonly GoogleSheetsService _sheetsService;

    // State

    private ObservableCollection<BurnEntry> _burns = [];
    public ICommand LaunchBurnCommand { get; }

    private double? _currentWeight;

    public ObservableCollection<BurnEntry> Burns
    {
        get => _burns;
        private set { _burns = value; OnPropertyChanged(); }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    private BurnEntry? _defaultOpenBurn;
    public BurnEntry? DefaultOpenBurn
    {
        get => _defaultOpenBurn;
        private set { _defaultOpenBurn = value; OnPropertyChanged(); }
    }

    private bool _isInBurnMode;
    public bool IsInBurnMode
    {
        get => _isInBurnMode;
        private set
        {
            _isInBurnMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartBurn));
            ((Command)LaunchBurnCommand).ChangeCanExecute();
        }
    }

    public bool CanStartBurn => !IsInBurnMode;


    // Constructor

    public ICommand RefreshCommand { get; }
    public ICommand OpenBurnCommand { get; }

    public BurnViewModel(GoogleSheetsService sheetsService)
    {
        _sheetsService = sheetsService;
        RefreshCommand = new Command(async () => await LoadDataAsync());
        OpenBurnCommand = new Command<BurnEntry>(async b =>
            await Shell.Current.GoToAsync("burnCalendar", new Dictionary<string, object>
            {
                { "Burn", b }
            }));

        LaunchBurnCommand = new Command(async () => await LaunchBurnAsync(), () => !IsInBurnMode);
    }

    // Loading

    public async Task LoadDataAsync(MetaEntry? meta = null)
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var burnsTask = _sheetsService.GetBurnsAsync();
            var metaTask = meta is not null
                ? Task.FromResult(meta)
                : _sheetsService.GetMetaAsync();

            await Task.WhenAll(burnsTask, metaTask);

            var burns = burnsTask.Result;
            var metaData = metaTask.Result;

            IsInBurnMode = string.Equals(
                metaData.CurrentMode, "Burn", StringComparison.OrdinalIgnoreCase);

            // Show most recent burn first
            Burns = new ObservableCollection<BurnEntry>(
                burns.OrderByDescending(b => b.Number));

            // If in burn mode, the active burn opens by default
            DefaultOpenBurn = IsInBurnMode
                ? Burns.FirstOrDefault(b => b.IsActive)
                : null;

            // Auto-end burn if 30 days have passed
            if (IsInBurnMode && DefaultOpenBurn is null)
            {
                await _sheetsService.UpdateMetaAsync("Weekly", metaData.CurrentWeight);
                IsInBurnMode = false;
                System.Diagnostics.Debug.WriteLine("[BurnVM] Burn ended automatically, mode reset to Weekly");
            }

            _currentWeight = metaData.CurrentWeight;

            ((Command) LaunchBurnCommand).ChangeCanExecute();
}
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load burns: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LaunchBurnAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var newBurn = await _sheetsService.CreateNewBurnAsync();
            if (newBurn is null)
            {
                ErrorMessage = "Failed to create burn session";
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[Launch] Writing CurrentMode=Burn, CurrentWeight={_currentWeight}");
            await _sheetsService.UpdateMetaAsync("Burn", _currentWeight);
            System.Diagnostics.Debug.WriteLine("[Launch] UpdateMetaAsync done");

            var serviceProvider = Application.Current!.Handler.MauiContext!.Services;
            var sheetsService = serviceProvider.GetService<GoogleSheetsService>()!;
            Application.Current.MainPage = new AppShell(sheetsService);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Launch burn failed: {ex.Message}";
            IsLoading = false;
        }
    }

    // INotifyPropertyChanged 

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
