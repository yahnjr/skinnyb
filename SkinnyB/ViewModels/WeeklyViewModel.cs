using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SkinnyB.Models;
using SkinnyB.Services;

namespace SkinnyB.ViewModels;

public class WeeklyViewModel : INotifyPropertyChanged
{
    private readonly GoogleSheetsService _sheetsService;
    private const int TableLimit = 10;
    private List<WorkoutEntry> _suggestedWorkouts = [];

    public List<WorkoutEntry> SuggestedWorkouts
    {
        get => _suggestedWorkouts;
        private set { _suggestedWorkouts = value; OnPropertyChanged(); }
    }

    public ICommand RefreshWorkoutsCommand { get; }
    public ICommand OpenWorkoutCommand { get; }

    private bool _workoutsExpanded = false;
    public bool WorkoutsExpanded
    {
        get => _workoutsExpanded;
        set { _workoutsExpanded = value; OnPropertyChanged(); }
    }
    public ICommand ToggleWorkoutsCommand => new Command(() => WorkoutsExpanded = !WorkoutsExpanded);

    // Collections

    private ObservableCollection<WeekEntry> _weeks = [];
    public ObservableCollection<WeekEntry> Weeks
    {
        get => _weeks;
        private set { _weeks = value; OnPropertyChanged(); RefreshAllDerived(); }
    }

    private MetaEntry _meta = new();
    public MetaEntry Meta
    {
        get => _meta;
        private set { _meta = value; OnPropertyChanged(); RefreshAllDerived(); }
    }

    private List<WeekEntry> _allEntries = [];

    // Loading / error state

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

    // SCOREBOARD — progress bars 

    public int ThisWeekTotal
    {
        get
        {
            DateTime thisMonday = GoogleSheetsService.GetWeekStartOf(DateTime.Today);
            return Weeks
                .FirstOrDefault(w => w.WeekStartDate.Date == thisMonday.Date)
                ?.Total ?? 0;
        }
    }

    public double ThisWeekPercent => Math.Round(ThisWeekTotal / (double)WeekEntry.GoalTotal * 100, 1);
    public double ThisWeekProgressValue => Math.Min(ThisWeekPercent / 100.0, 1.0);
    public bool ThisWeekOverGoal => ThisWeekTotal > WeekEntry.GoalTotal;
    public string ThisWeekLabel => $"{ThisWeekPercent:0}%";

    public double MonthPercent
    {
        get
        {
            DateTime thisMonday = GoogleSheetsService.GetWeekStartOf(DateTime.Today);
            var prior = Weeks
                .Where(w => w.WeekStartDate.Date < thisMonday.Date)
                .OrderByDescending(w => w.WeekStartDate)
                .Take(4)
                .ToList();

            return prior.Count == 0
                ? 0
                : Math.Round(prior.Average(w => w.GoalScorePercent), 1);
        }
    }

    public double MonthProgressValue => Math.Min(MonthPercent / 100.0, 1.0);
    public bool MonthOverGoal => MonthPercent > 100;
    public string MonthLabel => $"{MonthPercent:0}%";

    public double TotalPercent => Meta.AllTimeAverageScore ?? 0;
    public double TotalProgressValue => Math.Min(TotalPercent / 100.0, 1.0);
    public bool TotalOverGoal => TotalPercent > 100;
    public string TotalLabel => $"{TotalPercent:0}%";

    // Weight panel 

    public double? CurrentWeight
    {
        get
        {
            return _allEntries
                .Where(e => e.Weight.HasValue)
                .OrderByDescending(e => e.WeekStartDate)
                .FirstOrDefault()
                ?.Weight;
        }
    }

    public string CurrentWeightDisplay => CurrentWeight.HasValue
        ? $"{CurrentWeight:0.#} lbs"
        : "— lbs";

    public double? WeightDeltaFull
    {
        get
        {
            var measurements = _allEntries
                .Where(w => w.Weight.HasValue)
                .OrderByDescending(w => w.WeekStartDate)
                .ToList();

            if (measurements.Count < 5) return null;

            return Math.Round(
                measurements[0].Weight!.Value - measurements[4].Weight!.Value, 1);
        }
    }

    public string WeightDeltaDisplay
    {
        get
        {
            if (WeightDeltaFull is null) return "—";
            double d = WeightDeltaFull.Value;
            string sign = d > 0 ? "+" : d < 0 ? "-" : "";
            return $"{sign}{Math.Abs(d):0.#} lbs";
        }
    }

    public double WeightArrowRotation
    {
        get
        {
            if (WeightDeltaFull is null) return 0;
            // Positive delta = weight gain → arrow tilts UP (-45°)
            // Negative delta = weight loss → arrow tilts DOWN (+45°)
            return WeightDeltaFull.Value > 0 ? -45 : WeightDeltaFull.Value < 0 ? 45 : 0;
        }
    }

    public Microsoft.Maui.Graphics.Color WeightArrowColor
    {
        get
        {
            if (WeightDeltaFull is null) return Microsoft.Maui.Graphics.Colors.Grey;
            return WeightDeltaFull.Value > 0
                ? Microsoft.Maui.Graphics.Color.FromArgb("#C0392B")   // gain  → red
                : Microsoft.Maui.Graphics.Color.FromArgb("#2D6A4F");  // loss  → green
        }
    }

    public Microsoft.Maui.Graphics.Color WeightDeltaTextColor => WeightArrowColor;

    public bool WeightArrowVisible => WeightDeltaFull.HasValue;
    public bool WeightDashVisible => !WeightDeltaFull.HasValue;

    // Constructor & commands

    public ICommand RefreshCommand { get; }

    public WeeklyViewModel(GoogleSheetsService sheetsService)
    {
        _sheetsService = sheetsService;
        RefreshCommand = new Command(async () => await LoadDataAsync());
        RefreshWorkoutsCommand = new Command(async () => await LoadWorkoutsAsync());
        OpenWorkoutCommand = new Command<WorkoutEntry>(async w => await OnOpenWorkoutAsync(w));
    }

    // Data loading

    public async Task LoadDataAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var entriesTask = _sheetsService.GetWeekEntriesAsync();
            var metaTask = _sheetsService.GetMetaAsync();
            await Task.WhenAll(entriesTask, metaTask);

            _allEntries = entriesTask.Result;
            var meta = metaTask.Result;

            // Apply goals to the static WeekEntry fields so all instances update
            WeekEntry.GoalNutrition = meta.NutritionGoal;
            WeekEntry.GoalExercise = meta.ExerciseGoal;
            WeekEntry.GoalAlcohol = meta.AlcoholGoal;

            Meta = meta; 

            DateTime thisMonday = GoogleSheetsService.GetWeekStartOf(DateTime.Today);

            var recent = _allEntries
                .OrderBy(e => e.WeekStartDate)
                .TakeLast(TableLimit)
                .ToList();

            if (!recent.Any(e => e.WeekStartDate.Date == thisMonday.Date))
            {
                var cur = _allEntries.FirstOrDefault(e => e.WeekStartDate.Date == thisMonday.Date);
                if (cur is not null) recent.Add(cur);
            }

            Weeks = new ObservableCollection<WeekEntry>(
                recent.OrderBy(e => e.WeekStartDate));

            await LoadWorkoutsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadWorkoutsAsync()
    {
        try
        {
            SuggestedWorkouts = await _sheetsService.GetWeeklyWorkoutSuggestionsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Workouts] {ex.Message}");
        }
    }

    private async Task OnOpenWorkoutAsync(WorkoutEntry workout)
    {
        if (string.IsNullOrWhiteSpace(workout.Url)) return;
        await Launcher.OpenAsync(new Uri(workout.Url));
        _ = _sheetsService.IncrementWorkoutCountAsync(workout.Number);
        await LoadWorkoutsAsync();
    }

    // Saving 

    public async Task SaveEntryAsync(WeekEntry entry)
    {
        try { await _sheetsService.UpdateRowAsync(entry); }
        catch (Exception ex) { ErrorMessage = $"Save failed: {ex.Message}"; }
    }

    public async Task SaveAllAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;

        try { await _sheetsService.UpdateAllRowsAsync(Weeks.ToList()); }
        catch (Exception ex) { ErrorMessage = $"Save failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    // Helpers

    private void RefreshAllDerived()
    {
        OnPropertyChanged(nameof(ThisWeekTotal));
        OnPropertyChanged(nameof(ThisWeekPercent));
        OnPropertyChanged(nameof(ThisWeekProgressValue));
        OnPropertyChanged(nameof(ThisWeekOverGoal));
        OnPropertyChanged(nameof(ThisWeekLabel));
        OnPropertyChanged(nameof(MonthPercent));
        OnPropertyChanged(nameof(MonthProgressValue));
        OnPropertyChanged(nameof(MonthOverGoal));
        OnPropertyChanged(nameof(MonthLabel));
        OnPropertyChanged(nameof(TotalPercent));
        OnPropertyChanged(nameof(TotalProgressValue));
        OnPropertyChanged(nameof(TotalOverGoal));
        OnPropertyChanged(nameof(TotalLabel));
        OnPropertyChanged(nameof(CurrentWeight));
        OnPropertyChanged(nameof(CurrentWeightDisplay));
        OnPropertyChanged(nameof(WeightDeltaFull));
        OnPropertyChanged(nameof(WeightDeltaDisplay));
        OnPropertyChanged(nameof(WeightArrowRotation));
        OnPropertyChanged(nameof(WeightArrowColor));
        OnPropertyChanged(nameof(WeightDeltaTextColor));
        OnPropertyChanged(nameof(WeightArrowVisible));
        OnPropertyChanged(nameof(WeightDashVisible));
        OnPropertyChanged(nameof(SuggestedWorkouts));
    }

    // INotifyPropertyChanged 

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}