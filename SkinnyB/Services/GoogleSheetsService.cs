using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Util.Store;
using SkinnyB.Models;

namespace SkinnyB.Services;

public class GoogleSheetsService
{
    private const string SpreadsheetId = "1Q8l64kCdxKRciHk5669kNKs3s6UJl7ctChmh-iO-_dw";
    private const string DataRange = "Sheet1!A2:F";
    private const string AppName = "SkinnyB";
    private int workoutVideoCount = 41;

    private static readonly string[] Scopes =
    [
        SheetsService.Scope.Spreadsheets
    ];

    private static string TokenPath =>
        Path.Combine(FileSystem.AppDataDirectory, "google_token_cache");

    private SheetsService? _service;

    // Initialisation

    public async Task InitialiseAsync(string credentialsJson)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(credentialsJson));

        UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            GoogleClientSecrets.FromStream(stream).Secrets,
            Scopes,
            user: "user",
            CancellationToken.None,
            new FileDataStore(TokenPath, fullPath: true));

        _service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = AppName
        });
    }

    // Public API

    public async Task<List<WeekEntry>> GetWeekEntriesAsync(int maxRows = 15)
    {
        await EnsureInitializedAsync();

        var countRequest = _service!.Spreadsheets.Values.Get(SpreadsheetId, "Sheet1!A2:A");
        var countResponse = await countRequest.ExecuteAsync();
        int totalRows = countResponse.Values?.Count ?? 0;
        int startRow = Math.Max(2, totalRows - maxRows + 2);
        string range = $"Sheet1!A{startRow}:F";

        var request = _service!.Spreadsheets.Values.Get(SpreadsheetId, range);
        ValueRange response = await request.ExecuteAsync();
        IList<IList<object>> rows = response.Values ?? [];

        var entries = new List<WeekEntry>(rows.Count);

        foreach (IList<object> row in rows)
        {
            if (row.Count < 4) continue;
            if (!TryParseDate(row[0]?.ToString(), out DateTime weekDate)) continue;

            var entry = new WeekEntry
            {
                WeekStartDate = weekDate,
                EatHealthy = ParseInt(row, 1),
                Exercise = ParseInt(row, 2),
                AvoidAlcohol = ParseInt(row, 3),
            };
            entry.SetWeightSilently(ParseDouble(row, 5));  // ← initialises both fields
            entries.Add(entry);
        }

        entries.Sort((a, b) => a.WeekStartDate.CompareTo(b.WeekStartDate));

        DateTime thisMonday = GetWeekStartOf(DateTime.Today);
        bool currentWeekExists = entries.Any(e => e.WeekStartDate.Date == thisMonday.Date);

        if (!currentWeekExists)
        {
            entries.Add(new WeekEntry
            {
                WeekStartDate = thisMonday,
                EatHealthy = 0,
                Exercise = 0,
                AvoidAlcohol = 0,
                Weight = null,
                IsNew = true
            });
        }

        return entries;
    }

    public async Task<MetaEntry> GetMetaAsync()
    {
        await EnsureInitializedAsync();

        // Row 2: mode, current weight, all-time average
        const string MetaRange = "Sheet1!Q2:S2";
        var metaRequest = _service!.Spreadsheets.Values.Get(SpreadsheetId, MetaRange);
        var metaResponse = await metaRequest.ExecuteAsync();
        var metaRow = metaResponse.Values?.FirstOrDefault();

        // Row 4: nutrition goal, exercise goal, alcohol goal
        const string GoalsRange = "Sheet1!Q4:S4";
        var goalsRequest = _service!.Spreadsheets.Values.Get(SpreadsheetId, GoalsRange);
        var goalsResponse = await goalsRequest.ExecuteAsync();
        var goalsRow = goalsResponse.Values?.FirstOrDefault();

        var entry = new MetaEntry();

        if (metaRow is not null)
        {
            entry.CurrentMode = metaRow.Count > 0 ? metaRow[0]?.ToString() ?? "Weekly" : "Weekly";
            entry.CurrentWeight = metaRow.Count > 1 ? ParseDouble(metaRow, 1) : null;
            entry.AllTimeAverageScore = metaRow.Count > 2 ? ParseDouble(metaRow, 2) : null;
        }

        if (goalsRow is not null)
        {
            entry.NutritionGoal = goalsRow.Count > 0 ? ParseIntObj(goalsRow, 0, 5) : 5;
            entry.ExerciseGoal = goalsRow.Count > 1 ? ParseIntObj(goalsRow, 1, 3) : 3;
            entry.AlcoholGoal = goalsRow.Count > 2 ? ParseIntObj(goalsRow, 2, 5) : 5;
        }

        return entry;
    }

    public async Task<List<BurnEntry>> GetBurnsAsync()
    {
        await EnsureInitializedAsync();

        const string BurnRange = "Sheet1!G1:P";
        var request = _service!.Spreadsheets.Values.Get(SpreadsheetId, BurnRange);
        ValueRange response = await request.ExecuteAsync();
        IList<IList<object>> rows = response.Values ?? [];

        var burns = new List<BurnEntry>();
        BurnEntry? current = null;
        bool skipNext = false;
        int sheetRow = 1;

        foreach (var row in rows)
        {
            sheetRow++;

            string col0 = row.Count > 0 ? row[0]?.ToString()?.Trim() ?? "" : "";

            if (col0.StartsWith("BURN", StringComparison.OrdinalIgnoreCase) && col0.Length > 4)
            {
                current = new BurnEntry
                {
                    Name = col0,
                    Number = int.TryParse(col0[4..], out int n) ? n : burns.Count + 1,
                    SheetColIndex = 6
                };
                burns.Add(current);

                string dateRaw = row.Count > 1 ? row[1]?.ToString() ?? "" : "";
                if (TryParseDate(dateRaw, out DateTime date))
                {
                    current.Days.Add(new BurnDayEntry
                    {
                        Date = date,
                        SheetRowIndex = sheetRow,
                        NoBreakfast = ParseBool(row, 2),
                        NoSnacks = ParseBool(row, 3),
                        Portions = ParseBool(row, 4),
                        NoAlcohol = ParseBool(row, 5),
                        Exercise = ParseBool(row, 6),
                        Gtbh = ParseBool(row, 7),
                        Weight = ParseDouble(row, 8),
                        WorkoutNum = ParseInt(row, 9)
                    });
                }
                else
                {
                    skipNext = true;
                }
                continue;
            }

            if (skipNext) { skipNext = false; continue; }
            if (current is null) continue;

            string dateRaw2 = row.Count > 1 ? row[1]?.ToString() ?? "" : "";
            if (!TryParseDate(dateRaw2, out DateTime date2)) continue;

            current.Days.Add(new BurnDayEntry
            {
                Date = date2,
                SheetRowIndex = sheetRow,
                NoBreakfast = ParseBool(row, 2),
                NoSnacks = ParseBool(row, 3),
                Portions = ParseBool(row, 4),
                NoAlcohol = ParseBool(row, 5),
                Exercise = ParseBool(row, 6),
                Gtbh = ParseBool(row, 7),
                Weight = ParseDouble(row, 8),
                WorkoutNum = ParseInt(row, 9)
            });
        }

        return burns;
    }

    public async Task UpdateRowAsync(WeekEntry entry)
    {
        await EnsureInitializedAsync();

        if (entry.IsNew)
        {
            await AppendRowAsync(entry);
            entry.IsNew = false;
            return;
        }

        int rowIndex = await FindSheetRowAsync(entry.WeekStartDate);
        if (rowIndex == -1) return;

        string range = $"Sheet1!B{rowIndex}:F{rowIndex}";
        var valueRange = new ValueRange
        {
            Values = new List<IList<object>>
            {
                new List<object>
                {
                    entry.EatHealthy,
                    entry.Exercise,
                    entry.AvoidAlcohol,
                    entry.Total,
                    entry.Weight.HasValue ? entry.Weight : ""
                }
            }
        };

        var updateRequest = _service!.Spreadsheets.Values.Update(
            valueRange, SpreadsheetId, range);
        updateRequest.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await updateRequest.ExecuteAsync();
    }

    public async Task UpdateAllRowsAsync(List<WeekEntry> entries)
    {
        await EnsureInitializedAsync();

        System.Diagnostics.Debug.WriteLine(
            $"[Sheets] UpdateAllRowsAsync called with {entries.Count} entries");

        foreach (var entry in entries.Where(e => e.IsNew).OrderBy(e => e.WeekStartDate))
        {
            await AppendRowAsync(entry);
            entry.IsNew = false;
        }

        var existing = entries.Where(e => !e.IsNew).OrderBy(e => e.WeekStartDate).ToList();
        if (existing.Count == 0) return;

        var dateRequest = _service!.Spreadsheets.Values.Get(SpreadsheetId, "Sheet1!A2:A");
        var dateResponse = await dateRequest.ExecuteAsync();
        var dateRows = dateResponse.Values ?? [];

        var rowLookup = new Dictionary<DateTime, int>();
        for (int i = 0; i < dateRows.Count; i++)
        {
            if (dateRows[i].Count > 0 &&
                TryParseDate(dateRows[i][0]?.ToString(), out DateTime d))
                rowLookup.TryAdd(d.Date, i + 2);
        }

        int totalUpdated = 0;
        foreach (var e in existing)
        {
            if (!rowLookup.TryGetValue(e.WeekStartDate.Date, out int rowIdx))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Sheets] WARN: No sheet row for {e.WeekStartDate:MMM d}, skipping.");
                continue;
            }

            string rowRange = $"Sheet1!B{rowIdx}:F{rowIdx}";
            var rowValueRange = new ValueRange
            {
                Values = new List<IList<object>>
                {
                    new List<object>
                    {
                        e.EatHealthy,
                        e.Exercise,
                        e.AvoidAlcohol,
                        e.Total,
                        e.Weight.HasValue ? e.Weight : ""
                    }
                }
            };

            var rowUpdate = _service.Spreadsheets.Values.Update(
                rowValueRange, SpreadsheetId, rowRange);
            rowUpdate.ValueInputOption =
                SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;

            var rowResult = await rowUpdate.ExecuteAsync();
            totalUpdated += rowResult.UpdatedCells ?? 0;
        }

        System.Diagnostics.Debug.WriteLine(
            $"[Sheets] Update complete. Total cells updated: {totalUpdated}");
    }

    public async Task UpdateBurnDaysAsync(BurnEntry burn, List<BurnDayEntry> days)
    {
        await EnsureInitializedAsync();

        foreach (var day in days)
        {
            if (day.SheetRowIndex <= 0) continue;

            string range = $"Sheet1!I{day.SheetRowIndex}:P{day.SheetRowIndex}";
            var valueRange = new ValueRange
            {
                Values = new List<IList<object>>
                {
                    new List<object>
                    {
                        day.NoBreakfast ? 1 : 0,
                        day.NoSnacks    ? 1 : 0,
                        day.Portions    ? 1 : 0,
                        day.NoAlcohol   ? 1 : 0,
                        day.Exercise    ? 1 : 0,
                        day.Gtbh        ? 1 : 0,
                        day.Weight.HasValue ? day.Weight.Value : (object)"",
                        day.WorkoutNum
                    }
                }
            };

            var updateRequest = _service!.Spreadsheets.Values.Update(
                valueRange, SpreadsheetId, range);
            updateRequest.ValueInputOption =
                SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;

            var result = await updateRequest.ExecuteAsync();
            System.Diagnostics.Debug.WriteLine(
                $"[Sheets] Burn day {day.Date:MMM d} → row {day.SheetRowIndex}, " +
                $"cells updated: {result.UpdatedCells}");
        }
    }

    public async Task<BurnEntry?> CreateNewBurnAsync()
    {
        await EnsureInitializedAsync();

        var existingBurns = await GetBurnsAsync();
        int nextBurnNumber = existingBurns.Count > 0
            ? existingBurns.Max(b => b.Number) + 1
            : 1;

        string burnLabel = $"BURN{nextBurnNumber}";
        DateTime startDate = DateTime.Today;
        int numDays = 30;

        const string DateColumnRange = "Sheet1!H2:H";
        var dateRequest = _service!.Spreadsheets.Values.Get(SpreadsheetId, DateColumnRange);
        var dateResponse = await dateRequest.ExecuteAsync();
        var dateRows = dateResponse.Values ?? [];

        int lastDataRow = 1;
        for (int i = dateRows.Count - 1; i >= 0; i--)
        {
            if (dateRows[i].Count > 0 && !string.IsNullOrWhiteSpace(dateRows[i][0]?.ToString()))
            {
                lastDataRow = i + 2;
                break;
            }
        }

        int burnStartRow = lastDataRow + 1;

        var rng = new Random();
        var allNums = Enumerable.Range(1, workoutVideoCount).OrderBy(_ => rng.Next()).Take(30).ToList();

        var burnDays = new List<IList<object>>();
        burnDays.Add(new List<object>
        {
            burnLabel, startDate.ToString("M/d"),
            0, 0, 0, 0, 0, 0, "", allNums[0]
        });

        for (int i = 1; i < numDays; i++)
        {
            burnDays.Add(new List<object>
            {
                "", startDate.AddDays(i).ToString("M/d"),
                0, 0, 0, 0, 0, 0, "", allNums[i]
            });
        }

        string burnDataRange = $"Sheet1!G{burnStartRow}:P{burnStartRow + numDays - 1}";
        var burnValueRange = new ValueRange { Values = burnDays };
        var burnWriteRequest = _service.Spreadsheets.Values.Update(
            burnValueRange, SpreadsheetId, burnDataRange);
        burnWriteRequest.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        await burnWriteRequest.ExecuteAsync();

        var newBurn = new BurnEntry
        {
            Name = burnLabel,
            Number = nextBurnNumber,
            SheetColIndex = 6
        };

        for (int i = 0; i < numDays; i++)
        {
            newBurn.Days.Add(new BurnDayEntry
            {
                Date = startDate.AddDays(i),
                SheetRowIndex = burnStartRow + i,
                WorkoutNum = allNums[i]
            });
        }

        return newBurn;
    }

    public async Task UpdateMetaAsync(string currentMode, double? currentWeight = null)
    {
        await EnsureInitializedAsync();

        const string MetaRange = "Sheet1!Q2:S2";
        var valueRange = new ValueRange
        {
            Values = new List<IList<object>>
            {
                new List<object>
                {
                    currentMode,
                    currentWeight?.ToString() ?? "",
                    ""
                }
            }
        };

        var updateRequest = _service!.Spreadsheets.Values.Update(
            valueRange, SpreadsheetId, MetaRange);
        updateRequest.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await updateRequest.ExecuteAsync();
    }
    public async Task UpdateGoalsAsync(int nutritionGoal, int exerciseGoal, int alcoholGoal)
    {
        await EnsureInitializedAsync();

        const string GoalsRange = "Sheet1!Q4:S4";
        var valueRange = new ValueRange
        {
            Values = new List<IList<object>>
            {
                new List<object> { nutritionGoal, exerciseGoal, alcoholGoal }
            }
        };

        var updateRequest = _service!.Spreadsheets.Values.Update(
            valueRange, SpreadsheetId, GoalsRange);
        updateRequest.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await updateRequest.ExecuteAsync();

        System.Diagnostics.Debug.WriteLine(
            $"[Sheets] Goals saved: N={nutritionGoal}, E={exerciseGoal}, A={alcoholGoal}");
    }

    // Weight history (for Stats graph)

    public async Task<List<(DateTime Date, double Weight)>> GetWeightHistoryAsync()
    {
        await EnsureInitializedAsync();

        var request = _service!.Spreadsheets.Values.Get(SpreadsheetId, "Sheet1!A2:F");
        var response = await request.ExecuteAsync();
        var rows = response.Values ?? [];

        var result = new List<(DateTime, double)>();
        foreach (var row in rows)
        {
            if (row.Count < 6) continue;
            if (!TryParseDate(row[0]?.ToString(), out DateTime d)) continue;
            var w = ParseDouble(row, 5);
            if (w.HasValue) result.Add((d, w.Value));
        }

        result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return result;
    }

    // Existing helpers (unchanged)

    private async Task<int> FindSheetRowAsync(DateTime weekStart)
    {
        var dateRequest = _service!.Spreadsheets.Values.Get(SpreadsheetId, "Sheet1!A2:A");
        var dateResponse = await dateRequest.ExecuteAsync();
        var dateRows = dateResponse.Values ?? [];

        for (int i = 0; i < dateRows.Count; i++)
        {
            if (dateRows[i].Count > 0 &&
                TryParseDate(dateRows[i][0]?.ToString(), out DateTime d) &&
                d.Date == weekStart.Date)
                return i + 2;
        }
        return -1;
    }

    private async Task AppendRowAsync(WeekEntry entry)
    {
        var valueRange = new ValueRange
        {
            Values = new List<IList<object>>
            {
                new List<object>
                {
                    entry.WeekStartDate.ToString("M/d/yyyy"),
                    entry.EatHealthy,
                    entry.Exercise,
                    entry.AvoidAlcohol,
                    entry.Total,
                    entry.Weight.HasValue ? entry.Weight : ""
                }
            }
        };

        var colARequest = _service!.Spreadsheets.Values.Get(SpreadsheetId, "Sheet1!A2:A");
        var colAResponse = await colARequest.ExecuteAsync();
        var colARows = colAResponse.Values ?? [];

        int lastWeeklyRow = 1;
        for (int i = colARows.Count - 1; i >= 0; i--)
        {
            if (colARows[i].Count > 0 && !string.IsNullOrWhiteSpace(colARows[i][0]?.ToString()))
            {
                lastWeeklyRow = i + 2;
                break;
            }
        }

        int targetRow = lastWeeklyRow + 1;
        string writeRange = $"Sheet1!A{targetRow}:F{targetRow}";

        var writeRequest = _service.Spreadsheets.Values.Update(valueRange, SpreadsheetId, writeRange);
        writeRequest.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await writeRequest.ExecuteAsync();
    }

    public async Task<List<WorkoutEntry>> GetWorkoutsAsync()
    {
        await EnsureInitializedAsync();

        const string WorkoutRange = "Sheet1!U2:W42";
        var request = _service!.Spreadsheets.Values.Get(SpreadsheetId, WorkoutRange);
        request.ValueRenderOption =
            SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.FORMULA;
        var response = await request.ExecuteAsync();
        var rows = response.Values ?? [];

        var workouts = new List<WorkoutEntry>();
        foreach (var row in rows)
        {
            if (row.Count < 2) continue;
            if (!int.TryParse(row[0]?.ToString(), out int num)) continue;

            string raw = row[1]?.ToString() ?? "";
            string url = "";
            string title = raw;

            if (raw.StartsWith("=HYPERLINK(", StringComparison.OrdinalIgnoreCase))
            {
                var parts = raw[11..^1].Split(',', 2);
                if (parts.Length == 2)
                {
                    url = parts[0].Trim('"');
                    title = parts[1].Trim('"');
                }
            }

            workouts.Add(new WorkoutEntry
            {
                Number = num,
                Title = title,
                Url = url,
                PlayCount = ParseInt(row, 2)
            });
        }

        return workouts;
    }

    public async Task<List<WorkoutEntry>> GetWeeklyWorkoutSuggestionsAsync()
    {
        var all = await GetWorkoutsAsync();
        return all.OrderBy(w => w.PlayCount).Take(3).ToList();
    }

    public async Task IncrementWorkoutCountAsync(int workoutNumber)
    {
        await EnsureInitializedAsync();

        const string NumRange = "Sheet1!U2:U42";
        var request = _service!.Spreadsheets.Values.Get(SpreadsheetId, NumRange);
        var response = await request.ExecuteAsync();
        var rows = response.Values ?? [];

        int rowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Count > 0 &&
                int.TryParse(rows[i][0]?.ToString(), out int n) &&
                n == workoutNumber)
            {
                rowIndex = i + 2;
                break;
            }
        }

        if (rowIndex == -1) return;

        string countRange = $"Sheet1!W{rowIndex}";
        var countRequest = _service!.Spreadsheets.Values.Get(SpreadsheetId, countRange);
        var countResponse = await countRequest.ExecuteAsync();
        var countRow = countResponse.Values?.FirstOrDefault();
        int current = countRow is not null ? ParseInt(countRow, 0) : 0;

        var valueRange = new ValueRange
        {
            Values = new List<IList<object>> { new List<object> { current + 1 } }
        };
        var update = _service.Spreadsheets.Values.Update(
            valueRange, SpreadsheetId, countRange);
        update.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await update.ExecuteAsync();
    }

    public async Task UpdateWeeklyBurnColumnsAsync(
        int rowIndex, int eatHealthy, int exercise, int avoidAlcohol)
    {
        await EnsureInitializedAsync();

        string range = $"Sheet1!B{rowIndex}:D{rowIndex}";
        var valueRange = new ValueRange
        {
            Values = new List<IList<object>>
            {
                new List<object> { eatHealthy, exercise, avoidAlcohol }
            }
        };

        var updateRequest = _service!.Spreadsheets.Values.Update(
            valueRange, SpreadsheetId, range);
        updateRequest.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await updateRequest.ExecuteAsync();
    }

    public static DateTime GetWeekStartOf(DateTime date)
    {
        int daysFromSunday = ((int)date.DayOfWeek - (int)DayOfWeek.Sunday + 7) % 7;
        return date.AddDays(-daysFromSunday).Date;
    }

    public async Task<int> FindWeeklyRowAsync(DateTime weekStart)
    {
        await EnsureInitializedAsync();

        var request = _service!.Spreadsheets.Values.Get(SpreadsheetId, "Sheet1!A2:A");
        var response = await request.ExecuteAsync();
        var rows = response.Values ?? [];

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Count == 0) continue;
            if (!TryParseDate(rows[i][0]?.ToString(), out DateTime d)) continue;
            if (d.Date == weekStart.Date) return i + 2;
        }

        return -1;
    }

    public async Task<(int eatHealthy, int exercise, int avoidAlcohol)> ReadWeeklyBurnColumnsAsync(
        int rowIndex)
    {
        await EnsureInitializedAsync();

        string range = $"Sheet1!B{rowIndex}:D{rowIndex}";
        var request = _service!.Spreadsheets.Values.Get(SpreadsheetId, range);
        var response = await request.ExecuteAsync();

        var row = response.Values?.FirstOrDefault();
        if (row is null) return (0, 0, 0);

        return (ParseInt(row, 0), ParseInt(row, 1), ParseInt(row, 2));
    }

    // Private helpers

    private async Task EnsureInitializedAsync()
    {
        if (_service is not null) return;

#if ANDROID
        await InitialiseWithRefreshTokenAsync();
#else
        using var stream = await FileSystem.OpenAppPackageFileAsync("credentials.json");
        using var reader = new StreamReader(stream);
        string credentialsJson = await reader.ReadToEndAsync();

        using var jsonStream =
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(credentialsJson));

        UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            GoogleClientSecrets.FromStream(jsonStream).Secrets,
            Scopes,
            user: "user",
            CancellationToken.None,
            new FileDataStore(TokenPath, fullPath: true));

        _service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = AppName
        });
#endif
    }

#if ANDROID
    private async Task InitialiseWithRefreshTokenAsync()
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync("credentials.json");
        using var jsonStream = new MemoryStream();
        await stream.CopyToAsync(jsonStream);
        jsonStream.Position = 0;

        var secrets = GoogleClientSecrets.FromStream(jsonStream).Secrets;

        var token = new Google.Apis.Auth.OAuth2.Responses.TokenResponse
        {
            // Read RefreshToken from environment variable or configuration
            // This should be set in appsettings.Development.json or environment
            RefreshToken = Environment.GetEnvironmentVariable("GOOGLE_REFRESH_TOKEN") 
                ?? throw new InvalidOperationException("GOOGLE_REFRESH_TOKEN environment variable not set. Set it in your appsettings.Development.json or environment.")
        };

        var credential = new UserCredential(
            new Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow(
                new Google.Apis.Auth.OAuth2.Flows.GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = secrets,
                    Scopes = Scopes
                }),
            "user",
            token);

        _service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = AppName
        });
    }
#endif

    private static bool TryParseDate(string? raw, out DateTime result)
    {
        if (string.IsNullOrWhiteSpace(raw)) { result = default; return false; }

        string trimmed = raw.Trim();

        string[] formatsWithYear = ["M/d/yy", "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd"];
        if (DateTime.TryParseExact(trimmed, formatsWithYear,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out result))
            return true;

        if (DateTime.TryParseExact(trimmed, ["M/d", "MM/dd"],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime md))
        {
            for (int offset = 0; offset <= 10; offset++)
            {
                var candidate = new DateTime(DateTime.Today.Year - offset, md.Month, md.Day);
                if (candidate.Date <= DateTime.Today.Date)
                {
                    result = candidate;
                    return true;
                }
            }
        }

        return DateTime.TryParse(trimmed, out result);
    }

    private static int ParseInt(IList<object> row, int index)
    {
        if (index >= row.Count) return 0;
        return int.TryParse(row[index]?.ToString(), out int v) ? v : 0;
    }

    private static int ParseIntObj(IList<object> row, int index, int fallback)
    {
        if (index >= row.Count) return fallback;
        string? raw = row[index]?.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return int.TryParse(raw, out int v) ? v : fallback;
    }

    private static double? ParseDouble(IList<object> row, int index)
    {
        if (index >= row.Count) return null;
        string? raw = row[index]?.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return double.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : null;
    }

    private static bool ParseBool(IList<object> row, int index)
    {
        if (index >= row.Count) return false;
        string? v = row[index]?.ToString();
        return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }
}