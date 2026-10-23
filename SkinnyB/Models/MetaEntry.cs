namespace SkinnyB.Models;

public class MetaEntry
{
    public string CurrentMode { get; set; } = "Weekly";
    public double? CurrentWeight { get; set; }
    public double? AllTimeAverageScore { get; set; }

    // Goals are stored in the spreadsheet's metadata row, defaulting to 5/3/5.
    public int NutritionGoal { get; set; } = 5;
    public int ExerciseGoal { get; set; } = 3;
    public int AlcoholGoal { get; set; } = 5;

    public int TotalGoal => NutritionGoal + ExerciseGoal + AlcoholGoal;
}