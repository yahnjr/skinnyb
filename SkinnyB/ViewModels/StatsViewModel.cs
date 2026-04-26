using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SkinnyB.Models;
using SkinnyB.Services;

namespace SkinnyB.ViewModels;

public class StatsViewModel : INotifyPropertyChanged
{
    private readonly GoogleSheetsService _sheetsService;

    // ── Loading / error ───────────────────────────────────────────────────

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

    private string? _successMessage;
    public string? SuccessMessage
    {
        get => _successMessage;
        private set { _successMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSuccess)); }
    }
    public bool HasSuccess => !string.IsNullOrWhiteSpace(SuccessMessage);

    // ── Meta display ──────────────────────────────────────────────────────

    private MetaEntry _meta = new();
    public MetaEntry Meta
    {
        get => _meta;
        private set { _meta = value; OnPropertyChanged(); RefreshDerived(); }
    }

    public string CurrentWeightDisplay =>
        Meta.CurrentWeight.HasValue ? $"{Meta.CurrentWeight:0.#} lbs" : "—";

    public string AllTimeScoreDisplay =>
        Meta.AllTimeAverageScore.HasValue ? $"{Meta.AllTimeAverageScore:0.#}%" : "—";

    // ── Editable goals ────────────────────────────────────────────────────

    private int _nutritionGoal = 5;
    public int NutritionGoal
    {
        get => _nutritionGoal;
        set { _nutritionGoal = Math.Clamp(value, 0, 7); OnPropertyChanged(); }
    }

    private int _exerciseGoal = 3;
    public int ExerciseGoal
    {
        get => _exerciseGoal;
        set { _exerciseGoal = Math.Clamp(value, 0, 7); OnPropertyChanged(); }
    }

    private int _alcoholGoal = 5;
    public int AlcoholGoal
    {
        get => _alcoholGoal;
        set { _alcoholGoal = Math.Clamp(value, 0, 7); OnPropertyChanged(); }
    }

    // ── Weight graph data ─────────────────────────────────────────────────

    private List<(DateTime Date, double Weight)> _weightHistory = [];
    public List<(DateTime Date, double Weight)> WeightHistory
    {
        get => _weightHistory;
        private set {
            _weightHistory = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasWeightData));
            OnPropertyChanged(nameof(HasNoWeightData));
        }
    }

    public bool HasWeightData => WeightHistory.Count >= 2;
    public bool HasNoWeightData => !HasWeightData;

    // ── Commands ──────────────────────────────────────────────────────────

    public ICommand RefreshCommand { get; }
    public ICommand SaveGoalsCommand { get; }
    public ICommand IncrementNutritionCommand { get; }
    public ICommand DecrementNutritionCommand { get; }
    public ICommand IncrementExerciseCommand { get; }
    public ICommand DecrementExerciseCommand { get; }
    public ICommand IncrementAlcoholCommand { get; }
    public ICommand DecrementAlcoholCommand { get; }

    public StatsViewModel(GoogleSheetsService sheetsService)
    {
        _sheetsService = sheetsService;

        RefreshCommand = new Command(async () => await LoadDataAsync());
        SaveGoalsCommand = new Command(async () => await SaveGoalsAsync());

        IncrementNutritionCommand = new Command(() => NutritionGoal++);
        DecrementNutritionCommand = new Command(() => NutritionGoal--);
        IncrementExerciseCommand = new Command(() => ExerciseGoal++);
        DecrementExerciseCommand = new Command(() => ExerciseGoal--);
        IncrementAlcoholCommand = new Command(() => AlcoholGoal++);
        DecrementAlcoholCommand = new Command(() => AlcoholGoal--);
    }

    // ── Data loading ──────────────────────────────────────────────────────

    public async Task LoadDataAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        SuccessMessage = null;

        try
        {
            var metaTask = _sheetsService.GetMetaAsync();
            var historyTask = _sheetsService.GetWeightHistoryAsync();
            await Task.WhenAll(metaTask, historyTask);

            Meta = metaTask.Result;

            // Seed editable fields from loaded meta
            NutritionGoal = Meta.NutritionGoal;
            ExerciseGoal = Meta.ExerciseGoal;
            AlcoholGoal = Meta.AlcoholGoal;

            WeightHistory = historyTask.Result;
            OnPropertyChanged(nameof(HasWeightData));
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

    // ── Saving goals ──────────────────────────────────────────────────────

    private async Task SaveGoalsAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        SuccessMessage = null;

        try
        {
            await _sheetsService.UpdateGoalsAsync(NutritionGoal, ExerciseGoal, AlcoholGoal);

            // Push the new goals into the live WeekEntry statics
            WeekEntry.GoalNutrition = NutritionGoal;
            WeekEntry.GoalExercise = ExerciseGoal;
            WeekEntry.GoalAlcohol = AlcoholGoal;

            // Update local meta copy so display stays in sync
            Meta.NutritionGoal = NutritionGoal;
            Meta.ExerciseGoal = ExerciseGoal;
            Meta.AlcoholGoal = AlcoholGoal;

            SuccessMessage = "Goals saved!";

            // Clear message after 2 s
            _ = Task.Delay(2000).ContinueWith(_ =>
            {
                SuccessMessage = null;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(CurrentWeightDisplay));
        OnPropertyChanged(nameof(AllTimeScoreDisplay));
        NutritionGoal = Meta.NutritionGoal;
        ExerciseGoal = Meta.ExerciseGoal;
        AlcoholGoal = Meta.AlcoholGoal;
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}