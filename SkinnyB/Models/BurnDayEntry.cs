using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SkinnyB.Models;

// One day's data within a burn session
public class BurnDayEntry : INotifyPropertyChanged
{
    public DateTime Date { get; set; }

    public int SheetRowIndex { get; set; }

    // Supress display uf in the future
    public bool IsFuture => Date.Date > DateTime.Today;

    private bool _noBreakfast;
    public bool NoBreakfast
    {
        get => _noBreakfast;
        set { _noBreakfast = value; OnPropertyChanged(); OnPropertyChanged(nameof(DayTotal)); }
    }

    private bool _noSnacks;
    public bool NoSnacks
    {
        get => _noSnacks;
        set { _noSnacks = value; OnPropertyChanged(); OnPropertyChanged(nameof(DayTotal)); }
    }

    private bool _portions;
    public bool Portions
    {
        get => _portions;
        set { _portions = value; OnPropertyChanged(); OnPropertyChanged(nameof(DayTotal)); }
    }

    private bool _noAlcohol;
    public bool NoAlcohol
    {
        get => _noAlcohol;
        set { _noAlcohol = value; OnPropertyChanged(); OnPropertyChanged(nameof(DayTotal)); }
    }

    private bool _exercise;
    public bool Exercise
    {
        get => _exercise;
        set { _exercise = value; OnPropertyChanged(); OnPropertyChanged(nameof(DayTotal)); }
    }

    private bool _gtbh;
    public bool Gtbh
    {
        get => _gtbh;
        set { _gtbh = value; OnPropertyChanged(); OnPropertyChanged(nameof(DayTotal)); }
    }

    private double? _weight;
    public double? Weight
    {
        get => _weight;
        set { _weight = value; OnPropertyChanged(); }
    }

    public int WorkoutNum { get; set; }

    // Total points for the day
    public int DayTotal =>
        (NoBreakfast ? 1 : 0) +
        (NoSnacks ? 1 : 0) +
        (Portions ? 1 : 0) +
        (NoAlcohol ? 1 : 0) +
        (Exercise ? 1 : 0) +
        (Gtbh ? 1 : 0);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
