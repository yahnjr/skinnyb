namespace SkinnyB.Models;

public class BurnEntry
{
    // Label from the sheet, e.g. "BURN1"
    public string Name { get; set; } = string.Empty;

    // Burn number parsed from <see cref="Name"/> (1, 2, …)
    public int Number { get; set; }

    public int SheetColIndex { get; set; }

    public List<BurnDayEntry> Days { get; set; } = [];

    // Computed summaries 

    public DateTime? StartDate => Days.FirstOrDefault()?.Date;
    public DateTime? EndDate => Days.LastOrDefault()?.Date;

    public int TotalScore => Days.Sum(d => d.DayTotal);

    // Score for burn session as a whole
    public int MaxScore => Days.Count * 6;

    public double ScorePercent => MaxScore == 0 ? 0
        : Math.Round(TotalScore / (double)MaxScore * 100, 1);

    // Computed weight change for burn session
    public double? WeightChange
    {
        get
        {
            var weights = Days.Where(d => d.Weight.HasValue).ToList();
            if (weights.Count < 2) return null;
            return Math.Round(
                weights.Last().Weight!.Value - weights.First().Weight!.Value, 1);
        }
    }

    public string WeightChangeDisplay
    {
        get
        {
            if (WeightChange is null) return "—";
            double d = WeightChange.Value;
            string sign = d > 0 ? "+" : d < 0 ? "-" : "";
            return $"{sign}{Math.Abs(d):0.#} lbs";
        }
    }

    public int DaysTracked => Days.Count;

    public string DateRangeDisplay => StartDate.HasValue && EndDate.HasValue
        ? $"{StartDate:MMM d} – {EndDate:MMM d, yyyy}"
        : "No data";

    // Make sure burn should still be active based on date
    public bool IsActive =>
        StartDate.HasValue &&
        DateTime.Today <= (EndDate ?? StartDate.Value.AddDays(29));
}
