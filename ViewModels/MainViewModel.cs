using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KidsXmlToCsv.Models;
using KidsXmlToCsv.Services;

namespace KidsXmlToCsv.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private sealed class RefEq : IEqualityComparer<PersonRecord>
    {
        public static readonly RefEq Instance = new();
        public bool Equals(PersonRecord? a, PersonRecord? b) => ReferenceEquals(a, b);
        public int GetHashCode(PersonRecord obj) => RuntimeHelpers.GetHashCode(obj);
    }

  private enum DecisionKind { Keep, Generate, Drop }
  private readonly record struct Decision(DecisionKind Kind, string? Value);

  private readonly ExportPipeline _pipeline = new();
  private bool _suppressGroupHandlers;
  private bool _inRecompute;

    public ObservableCollection<string> SelectedZips { get; } = new();

    public StageState PersonIdentityStage { get; } = new() { Kind = DuplicateKind.PersonIdentity, Label = "Doppel-Personen" };
    public StageState LkidStage           { get; } = new() { Kind = DuplicateKind.Lkid,           Label = "LKID" };
    public StageState UsernameStage       { get; } = new() { Kind = DuplicateKind.Username,       Label = "Benutzername" };
    public StageState AppleIdStage        { get; } = new() { Kind = DuplicateKind.AppleId,        Label = "Apple ID" };
    public StageState SystemEmailStage    { get; } = new() { Kind = DuplicateKind.SystemEmail,    Label = "System-E-Mail" };

    public StageState[] AllStages { get; }

    // Pager aliases for XAML
    public PaginatedGroupCollection PersonIdentityPager => PersonIdentityStage.Pager;
    public PaginatedGroupCollection LkidPager           => LkidStage.Pager;
    public PaginatedGroupCollection UsernamePager       => UsernameStage.Pager;
    public PaginatedGroupCollection AppleIdPager        => AppleIdStage.Pager;
    public PaginatedGroupCollection SystemEmailPager    => SystemEmailStage.Pager;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private IReadOnlyList<PersonRow>? _previewRows;

    [ObservableProperty] private IAsyncRelayCommand? _downloadCommand;
    [ObservableProperty] private int _currentTabIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private bool _canProcess = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    [NotifyPropertyChangedFor(nameof(CanDownloadNow))]
    private bool _hasResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadNow))]
    [NotifyPropertyChangedFor(nameof(HasEmptyUsernames))]
    [NotifyPropertyChangedFor(nameof(HasEmptyAppleIds))]
    [NotifyPropertyChangedFor(nameof(HasEmptySystemEmails))]
    [NotifyPropertyChangedFor(nameof(EmptyUsernameCount))]
    [NotifyPropertyChangedFor(nameof(EmptyAppleIdCount))]
    [NotifyPropertyChangedFor(nameof(EmptySystemEmailCount))]
    private ExportPackage? _result;

    [ObservableProperty] private string _status = "Bereit.";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _showDuplicateDialog;
    [ObservableProperty] private bool _generateEmptyUsernames;
    [ObservableProperty] private bool _generateEmptyAppleIds;
    [ObservableProperty] private bool _generateEmptySystemEmails;

    public bool HasFiles => SelectedZips.Count > 0;
    public bool HasPreview => HasResult && PreviewRows is { Count: > 0 };

    public int EmptyUsernameCount => Result?.Persons.Count(p => string.IsNullOrWhiteSpace(p.Username)) ?? 0;
    public int EmptyAppleIdCount => Result?.Persons.Count(p => string.IsNullOrWhiteSpace(p.AppleId)) ?? 0;
    public int EmptySystemEmailCount => Result?.Persons.Count(p => string.IsNullOrWhiteSpace(p.SystemEmail)) ?? 0;
    public bool HasEmptyUsernames => EmptyUsernameCount > 0;
    public bool HasEmptyAppleIds => EmptyAppleIdCount > 0;
    public bool HasEmptySystemEmails => EmptySystemEmailCount > 0;

    public int PersonIdentityDuplicateCount => PersonIdentityStage.Count;
    public int LkidDuplicateCount => LkidStage.Count;
    public int UsernameDuplicateCount => UsernameStage.Count;
    public int AppleIdDuplicateCount => AppleIdStage.Count;
    public int SystemEmailDuplicateCount => SystemEmailStage.Count;
    public int TotalDuplicateGroups => PersonIdentityDuplicateCount + LkidDuplicateCount
                                     + UsernameDuplicateCount + AppleIdDuplicateCount + SystemEmailDuplicateCount;
    public int ResolvedGroups => AllStages.Sum(s => s.Pager.AllGroups.Count(g => g.IsResolved));
    public int UnresolvedGroups => AllStages.Sum(s => s.UnresolvedCount);
    public bool AllStagesComplete => AllStages.All(s => s.IsComplete);
    public bool HasDuplicates => TotalDuplicateGroups > 0;
    public bool NeedsResolution => HasDuplicates && !AllStagesComplete;
    public bool CanDownloadNow => HasResult && Result is not null && !NeedsResolution;

    public string DownloadButtonText =>
        NeedsResolution
            ? $"Erst Konflikte klären ({UnresolvedGroups} offen)"
            : "KIDS-CSV herunterladen";

    public string DuplicateButtonText =>
        $"Konflikte prüfen ({ResolvedGroups}/{TotalDuplicateGroups})";

    public string StageStatusText
    {
        get
        {
            if (!HasDuplicates && AllStagesComplete) return "Keine Konflikte.";
            if (AllStagesComplete) return "Alle Konflikte geklärt — bereit zum Export.";
            for (int i = 0; i < AllStages.Length; i++)
            {
                var s = AllStages[i];
                if (s.IsUnlocked && !s.IsComplete)
                    return $"Schritt {i + 1} von {AllStages.Length}: {s.Label} klären ({s.UnresolvedCount} offen)";
            }
            return "";
        }
    }

    public MainViewModel()
    {
        AllStages = new[] { PersonIdentityStage, LkidStage, UsernameStage, AppleIdStage, SystemEmailStage };
        SelectedZips.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFiles));
            ProcessCommand.NotifyCanExecuteChanged();
            ClearCommand.NotifyCanExecuteChanged();
        };
    }

    partial void OnGenerateEmptyUsernamesChanged(bool value) => RecomputeFrom(Array.IndexOf(AllStages, UsernameStage));
    partial void OnGenerateEmptyAppleIdsChanged(bool value) => RecomputeFrom(Array.IndexOf(AllStages, AppleIdStage));
    partial void OnGenerateEmptySystemEmailsChanged(bool value) => RecomputeFrom(Array.IndexOf(AllStages, SystemEmailStage));

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
            if (!SelectedZips.Contains(p) && File.Exists(p))
                SelectedZips.Add(p);

        ClearPreview();
        IsError = false;
        Status = $"{SelectedZips.Count} Datei(en) ausgewählt. Klicke „Verarbeiten“.";
        Progress = 0;
        DownloadCommand?.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanProcessInternal))]
    private async Task ProcessAsync()
    {
        if (SelectedZips.Count == 0) return;

        CanProcess = false;
        ClearPreview();
        IsError = false;
        Progress = 0;
        Status = "Verarbeite …";

        var zips = SelectedZips.ToList();
        var prog = new Progress<ProgressUpdate>(u =>
        {
            Status = u.Message;
            Progress = u.Total <= 0 ? 0 : Math.Min(100, u.Done / (double)u.Total * 100);
        });

        try
        {
            var result = await Task.Run(() => _pipeline.Run(zips, prog));
            Result = result;

            // Build the preview grid snapshot (one property change → fast on 10K rows).
            var rows = new List<PersonRow>(result.Persons.Count);
            foreach (var p in result.Persons)
            {
                rows.Add(PersonRow.From(p,
                    isDupLkid: false, isDupUsername: false,
                    isDupAppleId: false, isDupSystemEmail: false));
            }
            PreviewRows = rows;

            HasResult = true;
            Progress = 100;
            IsError = false;
            RecomputeFrom(0);

            if (HasDuplicates)
            {
                for (int i = 0; i < AllStages.Length; i++)
                    if (AllStages[i].IsUnlocked && !AllStages[i].IsComplete)
                    {
                        CurrentTabIndex = i;
                        break;
                    }
                ShowDuplicateDialog = true;
            }

            var conflictCount = AllStages.Sum(s => s.UnresolvedCount);
            Status = conflictCount > 0
                ? $"Fertig. {result.Persons.Count} Zeilen · {conflictCount} Konflikt(e) zu klären."
                : $"Fertig. {result.Persons.Count} Zeilen.";
            DownloadCommand?.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            IsError = true;
            Status = "Fehler: " + ex.Message;
        }
        finally
        {
            CanProcess = true;
        }
    }

    private bool CanProcessInternal() => CanProcess && SelectedZips.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        SelectedZips.Clear();
        ClearPreview();
        Progress = 0;
        IsError = false;
        Status = "Bereit.";
        DownloadCommand?.NotifyCanExecuteChanged();
    }

    private bool CanClear() => SelectedZips.Count > 0 || HasResult;

    [RelayCommand] private void OpenDuplicateDialog() { if (HasDuplicates) ShowDuplicateDialog = true; }
    [RelayCommand] private void CloseDuplicateDialog() => ShowDuplicateDialog = false;

    [RelayCommand]
    private void AcceptAllDefaults()
    {
        var snapshot = AllStages
            .Where(s => s.IsUnlocked)
            .SelectMany(s => s.Pager.AllGroups)
            .ToList();

        _suppressGroupHandlers = true;
        try
        {
            foreach (var g in snapshot)
                g.AcceptDefault();
        }
        finally
        {
            _suppressGroupHandlers = false;
        }

        RecomputeFrom(0);
    }

    [RelayCommand]
    private void DiscardAllRemaining()
    {
        var snapshot = AllStages
            .Where(s => s.IsUnlocked)
            .SelectMany(s => s.Pager.AllGroups.Select(g => (Stage: s, Group: g)))
            .ToList();

        _suppressGroupHandlers = true;
        try
        {
            foreach (var (stage, g) in snapshot)
            {
                if (g.IsResolved) continue;
                if (stage.Kind is DuplicateKind.PersonIdentity)
                    g.AcceptDefault();   // must keep one record
                else
                    g.Mode = ResolutionMode.DiscardAll;
            }
        }
        finally
        {
            _suppressGroupHandlers = false;
        }

        RecomputeFrom(0);
    }

    private void ClearPreview()
    {
        PreviewRows = null;
        foreach (var s in AllStages)
        {
            s.Pager.Clear();
            s.InputRecords = new();
            s.OutputRecords = new();
            s.IsUnlocked = false;
            s.IsComplete = false;
        }
        ShowDuplicateDialog = false;
        Result = null;
        HasResult = false;
        GenerateEmptyUsernames = false;
        GenerateEmptyAppleIds = false;
        GenerateEmptySystemEmails = false;
        RaiseAllCounts();
        OnPropertyChanged(nameof(HasPreview));
    }

    // ---------------- Sequential pipeline ----------------

    private void RecomputeFrom(int startIdx)
    {
      if (_inRecompute) return;
      _inRecompute = true;

    try {
        if (Result is null || startIdx < 0) return;

        for (int i = startIdx; i < AllStages.Length; i++)
        {
            var stage = AllStages[i];
            var prevComplete = i == 0 || AllStages[i - 1].IsComplete;

            if (!prevComplete)
            {
                // Lock this and every downstream stage.
                stage.InputRecords = new();
                stage.OutputRecords = new();
                stage.Pager.Clear();
                stage.IsUnlocked = false;
                stage.IsComplete = false;
                continue;
            }

            stage.IsUnlocked = true;
            stage.InputRecords = i == 0 ? Result.Persons.ToList() : AllStages[i - 1].OutputRecords;

            var dups = DetectForKind(stage.Kind, stage.InputRecords);
            var newGroups = CreateGroups(dups, stage.Kind);
            ReconcileGroups(stage.Pager.AllGroups.ToList(), newGroups);
            stage.Pager.SetGroups(newGroups);
            AttachGroupHandlers(stage);

            stage.OutputRecords = ApplyStageResolution(stage);
            stage.IsComplete = stage.Pager.AllGroups.Count == 0 || stage.Pager.AllGroups.All(g => g.IsResolved);
        }

        RaiseAllCounts();
        RefreshPreviews();
      } finally {
        _inRecompute = false;
      }
    }

    private void AttachGroupHandlers(StageState stage)
    {
        var stageIdx = Array.IndexOf(AllStages, stage);
        foreach (var g in stage.Pager.AllGroups)
        {
            g.PropertyChanged += (_, e) =>
            {
                if (_suppressGroupHandlers) return;
                if (e.PropertyName is nameof(DuplicateGroup.Mode)
                    or nameof(DuplicateGroup.SelectedCandidate)
                    or nameof(DuplicateGroup.IsResolved))
                {
                    RecomputeFrom(stageIdx);
                }
            };
        }
    }

    private static Dictionary<string, List<PersonRecord>> DetectForKind(DuplicateKind kind, List<PersonRecord> input) =>
        kind switch
        {
            DuplicateKind.PersonIdentity => DuplicateDetector.DetectPersonIdentity(input),
            DuplicateKind.Lkid           => DuplicateDetector.DetectLkid(input),
            DuplicateKind.Username       => DuplicateDetector.DetectUsername(input),
            DuplicateKind.AppleId        => DuplicateDetector.DetectAppleId(input),
            DuplicateKind.SystemEmail    => DuplicateDetector.DetectSystemEmail(input),
            _ => new Dictionary<string, List<PersonRecord>>(),
        };

    private static List<DuplicateGroup> CreateGroups(
        Dictionary<string, List<PersonRecord>> dups, DuplicateKind kind)
    {
        var groups = new List<DuplicateGroup>(dups.Count);
        foreach (var kv in dups.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            groups.Add(new DuplicateGroup(kv.Key, kind, kv.Value));
        return groups;
    }

    private static void ReconcileGroups(List<DuplicateGroup> oldGroups, List<DuplicateGroup> newGroups)
    {
        var oldByKey = oldGroups.ToDictionary(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var newG in newGroups)
        {
            if (!oldByKey.TryGetValue(newG.Key, out var oldG)) continue;
            if (!oldG.IsResolved) continue;

            var oldSelLkid = oldG.SelectedCandidate?.Record.Lkid;
            DuplicateCandidate? newSel = null;
            if (!string.IsNullOrEmpty(oldSelLkid))
                newSel = newG.Candidates.FirstOrDefault(c =>
                    c.Record.Lkid.Equals(oldSelLkid, StringComparison.OrdinalIgnoreCase));

            if (newSel is not null)
            {
                newG.Mode = oldG.Mode;
                newG.SuppressSelectionUpdate = true;
                newG.SelectedCandidate = newSel;
                newG.SuppressSelectionUpdate = false;
                newSel.SetSelectedSilent(true);
            }
            else if (oldG.Mode is ResolutionMode.GenerateAll
                              or ResolutionMode.DiscardAll
                              or ResolutionMode.KeepAll)
            {
                newG.Mode = oldG.Mode;
            }
        }
    }

    // ---------------- Per-stage resolution ----------------

    private List<PersonRecord> ApplyStageResolution(StageState stage) => stage.Kind switch
    {
        DuplicateKind.PersonIdentity => ApplyDropResolution(stage, keepAllAllowed: true),
        DuplicateKind.Lkid           => ApplyDropResolution(stage, keepAllAllowed: false),
        DuplicateKind.Username       => ApplyFieldResolution(stage, p => p.Username,
                                       (r, v) => r with { Username = v }, isUsername: true),
        DuplicateKind.AppleId        => ApplyFieldResolution(stage, p => p.AppleId,
                                       (r, v) => r with { AppleId = v }, isUsername: false),
        DuplicateKind.SystemEmail    => ApplyFieldResolution(stage, p => p.SystemEmail,
                                       (r, v) => r with { SystemEmail = v }, isUsername: false),
        _ => stage.InputRecords,
    };

    private List<PersonRecord> ApplyDropResolution(StageState stage, bool keepAllAllowed)
    {
        var groupByRecord = new Dictionary<PersonRecord, DuplicateGroup>(RefEq.Instance);
        foreach (var g in stage.Pager.AllGroups)
            foreach (var c in g.Candidates)
                groupByRecord[c.Record] = g;

        var output = new List<PersonRecord>(stage.InputRecords.Count);
        foreach (var p in stage.InputRecords)
        {
            if (!groupByRecord.TryGetValue(p, out var g) || !g.IsResolved)
            {
                output.Add(p);
                continue;
            }
            if (keepAllAllowed && g.Mode == ResolutionMode.KeepAll) { output.Add(p); continue; }
            if (g.Mode == ResolutionMode.DiscardAll) continue;
            if (ReferenceEquals(g.SelectedCandidate?.Record, p)) output.Add(p);
        }
        return output;
    }

    private List<PersonRecord> ApplyFieldResolution(
        StageState stage,
        Func<PersonRecord, string?> getField,
        Func<PersonRecord, string?, PersonRecord> setField,
        bool isUsername)
    {
        var groupByRecord = new Dictionary<PersonRecord, DuplicateGroup>(RefEq.Instance);
        foreach (var g in stage.Pager.AllGroups)
            foreach (var c in g.Candidates)
                groupByRecord[c.Record] = g;

        var generateEmpty = stage.Kind switch
        {
            DuplicateKind.Username    => GenerateEmptyUsernames,
            DuplicateKind.AppleId     => GenerateEmptyAppleIds,
            DuplicateKind.SystemEmail => GenerateEmptySystemEmails,
            _ => false,
        };

        var decisions = new Dictionary<PersonRecord, Decision>(RefEq.Instance);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usernameTaken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!isUsername)
            foreach (var r in stage.InputRecords)
                if (!string.IsNullOrEmpty(r.Username)) usernameTaken.Add(r.Username);

        // Phase 1 — decide per record (Keep / Generate / Drop), and reserve kept values.
        foreach (var p in stage.InputRecords)
        {
            var v = getField(p);
            var hasValue = !string.IsNullOrWhiteSpace(v);

            if (groupByRecord.TryGetValue(p, out var g) && g.IsResolved)
            {
                var selected = ReferenceEquals(g.SelectedCandidate?.Record, p);
                switch (g.Mode)
                {
                    case ResolutionMode.DiscardAll:
                        decisions[p] = new Decision(DecisionKind.Keep, null);
                        break;
                    case ResolutionMode.GenerateAll:
                        decisions[p] = new Decision(DecisionKind.Generate, null);
                        break;
                    case ResolutionMode.KeepAndBlank:
                        if (selected)
                        {
                            decisions[p] = new Decision(DecisionKind.Keep, v);
                            if (hasValue) { taken.Add(v!); if (isUsername) taken.Add(UsernameGenerator.SanitizeUsername(v!)); }
                        }
                        else decisions[p] = new Decision(DecisionKind.Keep, null);
                        break;
                    case ResolutionMode.KeepAndGenerate:
                        if (selected)
                        {
                            decisions[p] = new Decision(DecisionKind.Keep, v);
                            if (hasValue) { taken.Add(v!); if (isUsername) taken.Add(UsernameGenerator.SanitizeUsername(v!)); }
                        }
                        else decisions[p] = new Decision(DecisionKind.Generate, null);
                        break;
                    case ResolutionMode.KeepAndDisregard:
                        if (selected)
                        {
                            decisions[p] = new Decision(DecisionKind.Keep, v);
                            if (hasValue) { taken.Add(v!); if (isUsername) taken.Add(UsernameGenerator.SanitizeUsername(v!)); }
                        }
                        else decisions[p] = new Decision(DecisionKind.Drop, null);
                        break;
                }
            }
            else if (hasValue)
            {
                decisions[p] = new Decision(DecisionKind.Keep, v);
                taken.Add(v!);
                if (isUsername) taken.Add(UsernameGenerator.SanitizeUsername(v!));
            }
            else if (generateEmpty)
            {
                decisions[p] = new Decision(DecisionKind.Generate, null);
            }
            else
            {
                decisions[p] = new Decision(DecisionKind.Keep, null);
            }
        }

        // Phase 2 — generate for records flagged Generate.
        foreach (var p in stage.InputRecords)
        {
            if (decisions[p].Kind != DecisionKind.Generate) continue;
            var gen = GenerateFieldValue(p, taken, usernameTaken, stage.Kind);
            taken.Add(gen);
            if (isUsername) taken.Add(UsernameGenerator.SanitizeUsername(gen));
            decisions[p] = new Decision(DecisionKind.Keep, gen);
        }

        // Phase 3 — emit survivors with updated field.
        var output = new List<PersonRecord>(stage.InputRecords.Count);
        foreach (var p in stage.InputRecords)
        {
            var d = decisions[p];
            if (d.Kind == DecisionKind.Drop) continue;
            output.Add(setField(p, d.Value));
        }
        return output;
    }

    private static string GenerateFieldValue(
        PersonRecord p, HashSet<string> taken, HashSet<string> usernameTaken, DuplicateKind kind)
    {
        if (kind == DuplicateKind.Username)
            return UsernameGenerator.GenerateUnique(p.FirstName, p.LastName, taken);

        var user = p.Username;
        if (string.IsNullOrEmpty(user))
        {
            user = UsernameGenerator.GenerateUnique(p.FirstName, p.LastName, usernameTaken);
            usernameTaken.Add(user);
        }

        if (kind == DuplicateKind.AppleId)
            return UsernameGenerator.GenerateAppleId(user);

        if (kind == DuplicateKind.SystemEmail)
        {
            var soid = p.SoId;
            if (string.IsNullOrWhiteSpace(soid) && p.SchoolId is { Length: >= 2 } sid)
                soid = sid[^2..];
            return UsernameGenerator.GenerateSystemEmail(user, soid);
        }

        return "";
    }

    // ---------------- Previews ----------------

    private void RefreshPreviews()
    {
        foreach (var s in AllStages) UpdateStagePreviews(s);
    }

    private void UpdateStagePreviews(StageState stage)
    {
        if (!stage.IsUnlocked) return;

        var isDropStage = stage.Kind is DuplicateKind.PersonIdentity or DuplicateKind.Lkid;
        HashSet<PersonRecord>? outputSet = isDropStage
            ? new HashSet<PersonRecord>(stage.OutputRecords, RefEq.Instance) : null;
        Dictionary<string, PersonRecord>? outputByLkid = isDropStage
            ? null
            : stage.OutputRecords.ToDictionary(r => r.Lkid, StringComparer.OrdinalIgnoreCase);

        foreach (var g in stage.Pager.AllGroups)
        {
            g.ApplyPreviews(
                currentValueFn: r => GetValue(g.Kind, r) ?? "—",
                previewFn: r =>
                {
                    if (!g.IsResolved)
                        return ("→ offen", DuplicateGroup.BrushMuted());

                    if (isDropStage)
                    {
                        return outputSet!.Contains(r)
                            ? ("→ behalten", DuplicateGroup.BrushKeep())
                            : ("→ verworfen (Zeile entfernt)", DuplicateGroup.BrushDrop());
                    }

                    if (!outputByLkid!.TryGetValue(r.Lkid, out var outRec))
                        return ("→ verworfen (Zeile entfernt)", DuplicateGroup.BrushDrop());

                    var original = GetValue(g.Kind, r);
                    var final = GetValue(g.Kind, outRec);

                    if (string.Equals(original, final, StringComparison.OrdinalIgnoreCase))
                        return ($"→ behalten: {original ?? "—"}", DuplicateGroup.BrushKeep());
                    if (string.IsNullOrEmpty(final))
                        return ("→ geleert", DuplicateGroup.BrushMuted());
                    return ($"→ generiert: {final}", DuplicateGroup.BrushGenerate());
                });
        }
    }

    private static string? GetValue(DuplicateKind kind, PersonRecord r) => kind switch
    {
        DuplicateKind.Username    => r.Username,
        DuplicateKind.AppleId     => r.AppleId,
        DuplicateKind.SystemEmail => r.SystemEmail,
        _ => null,
    };

    // ---------------- Output & download ----------------

    private List<PersonRecord> BuildResolvedOutput()
        => AllStages[^1].OutputRecords;

        public async Task DownloadAsync(IStorageProvider storageProvider)
        {
            if (Result is null) return;

            if (NeedsResolution)
            {
                ShowDuplicateDialog = true;
                Status = $"Bitte zuerst alle Konflikte klären ({UnresolvedGroups} offen).";
                return;
            }

            var resolved = BuildResolvedOutput();

            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "KIDS-Export speichern",
                SuggestedFileName = $"kids-export-{DateTime.Now:yyyy-MM-dd}",
                DefaultExtension = "xlsx",
                ShowOverwritePrompt = true,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Excel-Arbeitsmappe (*.xlsx)") { Patterns = new[] { "*.xlsx" } },
                    new FilePickerFileType("CSV, UTF-8, Semikolon (*.csv)") { Patterns = new[] { "*.csv" } },
                },
            });
            if (file is null) return;

            var fileName = string.IsNullOrWhiteSpace(file.Name) ? "kids-export" : file.Name;
            var isCsv = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

            try
            {
                await using (var stream = await file.OpenWriteAsync())
                {
                    stream.SetLength(0);
                    if (isCsv)
                    {
                        // CSV path (UTF-8 mit BOM). Kept as a fallback for users
                        // whose Excel opens semicolon-delimited UTF-8 correctly.
                        var csv = CsvWriter.Build(resolved);
                        var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                        await using var writer = new StreamWriter(stream, utf8WithBom);
                        await writer.WriteAsync(csv);
                    }
                    else
                    {
                        // XLSX path — encoding- and delimiter-agnostic; opens
                        // correctly in every Excel / Numbers / LibreOffice install.
                        var bytes = XlsxWriter.Build(resolved);
                        await stream.WriteAsync(bytes);
                    }
                }

                var warnLines = new List<string>(Result.Warnings);
                if (HasDuplicates)
                    warnLines.Add($"Konflikte aufgelöst: {ResolvedGroups}/{TotalDuplicateGroups} Gruppen.");
                if (GenerateEmptyUsernames || GenerateEmptyAppleIds || GenerateEmptySystemEmails)
                {
                    var flags = new List<string>();
                    if (GenerateEmptyUsernames) flags.Add($"Benutzername ({EmptyUsernameCount})");
                    if (GenerateEmptyAppleIds) flags.Add($"Apple ID ({EmptyAppleIdCount})");
                    if (GenerateEmptySystemEmails) flags.Add($"System-E-Mail ({EmptySystemEmailCount})");
                    warnLines.Add("Leere Felder generiert: " + string.Join(", ", flags));
                }
                if (warnLines.Count > 0)
                {
                    var dir = Path.GetDirectoryName(file.Path.LocalPath) ?? ".";
                    var warnPath = Path.Combine(dir,
                        Path.GetFileNameWithoutExtension(file.Name) + "-warnings.txt");
                    await File.WriteAllLinesAsync(warnPath, warnLines);
                }

                Status = $"Gespeichert: {fileName} ({resolved.Count} Zeilen)";
                IsError = false;
            }
            catch (Exception ex)
            {
                IsError = true;
                Status = "Speichern fehlgeschlagen: " + ex.Message;
            }
        }

    private void RaiseAllCounts()
    {
        OnPropertyChanged(nameof(PersonIdentityDuplicateCount));
        OnPropertyChanged(nameof(LkidDuplicateCount));
        OnPropertyChanged(nameof(UsernameDuplicateCount));
        OnPropertyChanged(nameof(AppleIdDuplicateCount));
        OnPropertyChanged(nameof(SystemEmailDuplicateCount));
        OnPropertyChanged(nameof(TotalDuplicateGroups));
        OnPropertyChanged(nameof(ResolvedGroups));
        OnPropertyChanged(nameof(UnresolvedGroups));
        OnPropertyChanged(nameof(AllStagesComplete));
        OnPropertyChanged(nameof(HasDuplicates));
        OnPropertyChanged(nameof(NeedsResolution));
        OnPropertyChanged(nameof(CanDownloadNow));
        OnPropertyChanged(nameof(DownloadButtonText));
        OnPropertyChanged(nameof(DuplicateButtonText));
        OnPropertyChanged(nameof(StageStatusText));
        DownloadCommand?.NotifyCanExecuteChanged();
    }
}
