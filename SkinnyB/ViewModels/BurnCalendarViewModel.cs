using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SkinnyB.Models;
using SkinnyB.Services;

namespace SkinnyB.ViewModels;

// Represents one cell in the calendar grid
public class CalendarCell : INotifyPropertyChanged
{
    public BurnDayEntry? Day { get; init; }

    public bool IsEmpty => Day is null || Day.IsFuture;
    public bool HasData => !IsEmpty;

    public string DayNumber => Day is null ? "" : Day.Date.Day.ToString();

    // Score badge colour based on DayTotal (0=grey … 6=deep green)
    public Microsoft.Maui.Graphics.Color BadgeColor
    {
        get
        {
            if (Day is null || Day.IsFuture) return Microsoft.Maui.Graphics.Colors.Transparent;
            return Day.DayTotal switch
            {
                0 => Microsoft.Maui.Graphics.Color.FromArgb("#CCCCCC"),
                1 => Microsoft.Maui.Graphics.Color.FromArgb("#F4A261"),
                2 => Microsoft.Maui.Graphics.Color.FromArgb("#E76F51"),
                3 => Microsoft.Maui.Graphics.Color.FromArgb("#F4D03F"),
                4 => Microsoft.Maui.Graphics.Color.FromArgb("#A8D5A2"),
                5 => Microsoft.Maui.Graphics.Color.FromArgb("#52B788"),
                _ => Microsoft.Maui.Graphics.Color.FromArgb("#2D6A4F")  
            };
        }
    }

    public string ScoreText => Day is null || Day.IsFuture ? "" : Day.DayTotal.ToString();

    public void RefreshColors()
    {
        OnPropertyChanged(nameof(BadgeColor));
        OnPropertyChanged(nameof(ScoreText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class BurnCalendarViewModel : INotifyPropertyChanged
{
    // State
    private readonly GoogleSheetsService _sheetsService;
    private List<WorkoutEntry> _workouts = [];
    public BurnEntry Burn { get; }

    // Flat list of calendar cells (padded to complete weeks)
    public ObservableCollection<CalendarCell> Cells { get; } = [];

    public IReadOnlyList<string> DayHeaders { get; } =
        ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    private BurnDayEntry? _selectedDay;
    public BurnDayEntry? SelectedDay
    {
        get => _selectedDay;
        set
        {
            _selectedDay = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDaySelected));
            OnPropertyChanged(nameof(SelectedWorkout));

            if (value is not null)
            {
                EditNoBreakfast = value.NoBreakfast;
                EditNoSnacks = value.NoSnacks;
                EditPortions = value.Portions;
                EditNoAlcohol = value.NoAlcohol;
                EditExercise = value.Exercise;
                EditGtbh = value.Gtbh;
                EditWeightText = value.Weight.HasValue ? value.Weight.Value.ToString("0.#") : "";
            }
        }
    }

    public bool IsDaySelected => SelectedDay is not null;

    // Editable fields

    private bool _editNoBreakfast;
    public bool EditNoBreakfast
    {
        get => _editNoBreakfast;
        set { _editNoBreakfast = value; OnPropertyChanged(); }
    }

    private bool _editNoSnacks;
    public bool EditNoSnacks
    {
        get => _editNoSnacks;
        set { _editNoSnacks = value; OnPropertyChanged(); }
    }

    private bool _editPortions;
    public bool EditPortions
    {
        get => _editPortions;
        set { _editPortions = value; OnPropertyChanged(); }
    }

    private bool _editNoAlcohol;
    public bool EditNoAlcohol
    {
        get => _editNoAlcohol;
        set { _editNoAlcohol = value; OnPropertyChanged(); }
    }

    private bool _editExercise;
    public bool EditExercise
    {
        get => _editExercise;
        set { _editExercise = value; OnPropertyChanged(); }
    }

    private bool _editGtbh;
    public bool EditGtbh
    {
        get => _editGtbh;
        set { _editGtbh = value; OnPropertyChanged(); }
    }

    private string _editWeightText = "";
    public string EditWeightText
    {
        get => _editWeightText;
        set { _editWeightText = value; OnPropertyChanged(); }
    }

    // Save state

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
        set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    // Set of row indices that have been edited since last save
    private readonly HashSet<int> _dirtyRows = [];
    public bool HasUnsavedChanges => _dirtyRows.Count > 0;

    // Constructor & Commands

    public ICommand SelectDayCommand { get; }
    public ICommand CommitDayCommand { get; }   // apply edits to the in-memory entry
    public ICommand DismissDayCommand { get; }
    public ICommand SaveCommand { get; }

    public BurnCalendarViewModel(BurnEntry burn, GoogleSheetsService sheetsService)
    {
        Burn = burn;
        _sheetsService = sheetsService;

        SelectDayCommand = new Command<CalendarCell>(OnSelectDay);
        CommitDayCommand = new Command(OnCommitDay);
        DismissDayCommand = new Command(OnDismissDay);
        OpenWorkoutCommand = new Command(async () => await OnOpenWorkoutAsync());
        SaveCommand = new Command(async () => await SaveAsync(),
                                        () => HasUnsavedChanges && !IsLoading);

        BuildCalendarCells();
        _ = LoadWorkoutsAsync();
    }

    // Calendar construction

    private void BuildCalendarCells()
    {
        Cells.Clear();

        if (Burn.StartDate is null) return;

        DateTime start = Burn.StartDate.Value.Date;

        // Pad to the start of the week (Sunday = 0)
        int leadingPad = (int)start.DayOfWeek;
        for (int i = 0; i < leadingPad; i++)
            Cells.Add(new CalendarCell());

        var dayLookup = Burn.Days.ToDictionary(d => d.Date.Date);

        // Show 30 slots regardless of actual days logged, but only up to today
        int totalDays = 30;
        for (int i = 0; i < totalDays; i++)
        {
            DateTime cellDate = start.AddDays(i);
            dayLookup.TryGetValue(cellDate, out BurnDayEntry? entry);

            // If date is in the future and no entry exists, show empty cell
            if (cellDate.Date > DateTime.Today && entry is null)
            {
                Cells.Add(new CalendarCell());
                continue;
            }

            entry ??= new BurnDayEntry { Date = cellDate, SheetRowIndex = -1 };

            Cells.Add(new CalendarCell { Day = entry });
        }

        // Pad end to complete the last week row
        while (Cells.Count % 7 != 0)
            Cells.Add(new CalendarCell());
    }

    // Day selection & editing

    private void OnSelectDay(CalendarCell cell)
    {
        if (cell.IsEmpty) return;
        SelectedDay = cell.Day;
    }
    private async Task SyncWeekAsync(BurnDayEntry committedDay)
    {
        try
        {
            DateTime weekStart = GoogleSheetsService.GetWeekStartOf(committedDay.Date);

            int rowIndex = await _sheetsService.FindWeeklyRowAsync(weekStart);
            if (rowIndex == -1)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[BurnSync] No weekly row found for {weekStart:MMM d}, skipping.");
                return;
            }

            // Read existing weekly values first
            var (existingEat, existingEx, existingAlc) =
                await _sheetsService.ReadWeeklyBurnColumnsAsync(rowIndex);

            // Add 1 for each category this day qualifies for, cap at 7
            int newEat = committedDay.NoBreakfast && committedDay.NoSnacks && committedDay.Portions
                ? Math.Min(existingEat + 1, 7) : existingEat;
            int newEx = committedDay.Exercise
                ? Math.Min(existingEx + 1, 7) : existingEx;
            int newAlc = committedDay.NoAlcohol
                ? Math.Min(existingAlc + 1, 7) : existingAlc;

            await _sheetsService.UpdateWeeklyBurnColumnsAsync(rowIndex, newEat, newEx, newAlc);

            System.Diagnostics.Debug.WriteLine(
                $"[BurnSync] Row {rowIndex} ({weekStart:MMM d}) → " +
                $"Eat={existingEat}→{newEat} Ex={existingEx}→{newEx} Alc={existingAlc}→{newAlc}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BurnSync] Failed: {ex.Message}");
            ErrorMessage = $"Weekly sync failed: {ex.Message}";
        }
    }
    private async void OnCommitDay()
    {
        if (SelectedDay is null) return;
        var committedDay = SelectedDay;

        SelectedDay.NoBreakfast = EditNoBreakfast;
        SelectedDay.NoSnacks = EditNoSnacks;
        SelectedDay.Portions = EditPortions;
        SelectedDay.NoAlcohol = EditNoAlcohol;
        SelectedDay.Exercise = EditExercise;
        SelectedDay.Gtbh = EditGtbh;

        if (double.TryParse(EditWeightText,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out double w))
            SelectedDay.Weight = w;
        else
            SelectedDay.Weight = null;

        // Mark as dirty if it has a real sheet row
        if (SelectedDay.SheetRowIndex > 0)
            _dirtyRows.Add(SelectedDay.SheetRowIndex);

        var cell = Cells.FirstOrDefault(c => c.Day == SelectedDay);
        cell?.RefreshColors();

        OnPropertyChanged(nameof(HasUnsavedChanges));
        ((Command)SaveCommand).ChangeCanExecute();

        SelectedDay = null;

        await SyncWeekAsync(committedDay);
    }

    private void OnDismissDay()
    {
        SelectedDay = null;
    }


    // Load and open workouts
    public async Task LoadWorkoutsAsync()
    {
        try
        {
            _workouts = await _sheetsService.GetWorkoutsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Workouts] Load failed: {ex.Message}");
        }
    }
    public WorkoutEntry? SelectedWorkout =>
        SelectedDay is null ? null :
        _workouts.FirstOrDefault(w => w.Number == SelectedDay.WorkoutNum);

    public ICommand OpenWorkoutCommand { get; }

    private async Task OnOpenWorkoutAsync()
    {
        if (SelectedWorkout is null || string.IsNullOrWhiteSpace(SelectedWorkout.Url))
            return;

        // Open the video
        await Launcher.OpenAsync(new Uri(SelectedWorkout.Url));

        // Increment count in background
        _ = _sheetsService.IncrementWorkoutCountAsync(SelectedWorkout.Number);
    }

    // Saving

    private async Task SaveAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var dirtyDays = Burn.Days
                .Where(d => _dirtyRows.Contains(d.SheetRowIndex))
                .ToList();

            await _sheetsService.UpdateBurnDaysAsync(Burn, dirtyDays);

            _dirtyRows.Clear();
            OnPropertyChanged(nameof(HasUnsavedChanges));
            ((Command)SaveCommand).ChangeCanExecute();
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

    // INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
