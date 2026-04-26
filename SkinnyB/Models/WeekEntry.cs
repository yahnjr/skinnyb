using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SkinnyB.Models;

public class WeekEntry : INotifyPropertyChanged
{
    public static int GoalNutrition { get; set; } = 5;
    public static int GoalExercise { get; set; } = 3;
    public static int GoalAlcohol { get; set; } = 5;
    public static int GoalTotal => GoalNutrition + GoalExercise + GoalAlcohol;

    public DateTime WeekStartDate { get; set; }
    public DateTime WeekEndDate => WeekStartDate.AddDays(6);

    public bool EatHealthyMet => EatHealthy >= GoalNutrition;
    public bool ExerciseMet => Exercise >= GoalExercise;
    public bool AlcoholMet => AvoidAlcohol >= GoalAlcohol;

    public bool IsNew { get; set; }

    private int _eatHealthy;
    public int EatHealthy
    {
        get => _eatHealthy;
        set
        {
            _eatHealthy = Math.Clamp(value, 0, 7);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(RawScorePercent));
            OnPropertyChanged(nameof(GoalScorePercent));
            OnPropertyChanged(nameof(EatHealthyMet));
        }
    }

    private int _exercise;
    public int Exercise
    {
        get => _exercise;
        set
        {
            _exercise = Math.Clamp(value, 0, 7);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(RawScorePercent));
            OnPropertyChanged(nameof(GoalScorePercent));
            OnPropertyChanged(nameof(ExerciseMet));
        }
    }

    private int _avoidAlcohol;
    public int AvoidAlcohol
    {
        get => _avoidAlcohol;
        set
        {
            _avoidAlcohol = Math.Clamp(value, 0, 7);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(RawScorePercent));
            OnPropertyChanged(nameof(GoalScorePercent));
            OnPropertyChanged(nameof(AlcoholMet));
        }
    }

    private double? _weight;
    public double? Weight
    {
        get => _weight;
        set
        {
            _weight = value;
            OnPropertyChanged();
        }
    }
    public void SetWeightSilently(double? value)
    {
        _weight = value;
        _weightDisplayText = value.HasValue
            ? value.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
            : "";
        OnPropertyChanged(nameof(Weight));
        OnPropertyChanged(nameof(WeightDisplay));
    }

    // Holds whatever the user has typed — not reformatted mid-keystroke.
    private string _weightDisplayText = "";

    public string WeightDisplay
    {
        get => _weightDisplayText;
        set
        {
            _weightDisplayText = value ?? "";

            if (string.IsNullOrWhiteSpace(value))
            {
                _weight = null;
            }
            else
            {
                string normalised = value.Replace(',', '.');
                if (double.TryParse(normalised,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double d))
                    _weight = d;
            }

            OnPropertyChanged(nameof(Weight));
        }
    }

    public int Total => EatHealthy + Exercise + AvoidAlcohol;
    public double RawScorePercent => Math.Round(Total / 21.0 * 100, 1);
    public double GoalScorePercent => Math.Min(Math.Round(Total / (double)GoalTotal * 100, 1), 100);

    public string WeekRangeLabel => $"{WeekStartDate:MMM d} - {WeekEndDate:MMM d}";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}