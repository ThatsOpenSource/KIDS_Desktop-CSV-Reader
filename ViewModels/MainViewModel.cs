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
    private enum DecisionKind { Keep, Generate, Drop }
    private readonly record struct Decision(DecisionKind Kind, string? Value);

    private sealed class RefEq : IEqualityComparer<PersonRecord>
    {
        public static readonly RefEq Instance = new();
        public bool Equals(PersonRecord? a, PersonRecord? b) => ReferenceEquals(a, b);
        public int GetHashCode(PersonRecord obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private readonly ExportPipeline _pipeline = new();
    private bool _suppressGroupHandlers;
    private bool _inRecompute;

    // Snapshot of *original* duplicates, used by RebuildPreview so the
    // preview grid keeps the ⚠ markers even after Apple ID / System E-Mail
    // are filled in post-pipeline.
    private HashSet<string> _originalLkidDups     = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _originalUserDups     = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _originalAppleDups    = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _originalSysEmailDups = new(StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- Stage setup

    public ObservableCollection<string> SelectedZips { get; } = new();

    public StageState PersonIdentityStage { get; } = new() { Kind = DuplicateKind.PersonIdentity, Label = "Doppel-Personen" };
    public StageState LkidStage           { get; } = new() { Kind = DuplicateKind.Lkid,           Label = "LKID" };
    public StageState UsernameStage       { get; } = new() { Kind = DuplicateKind.Username,       Label = "Benutzername" };
    public StageState AppleIdStage        { get; } = new() { Kind = DuplicateKind.AppleId,        Label = "Apple ID" };
    public StageState SystemEmailStage    { get; } = new() { Kind = DuplicateKind.SystemEmail,    Label = "System-E-Mail" };

    public StageState[] AllStages { get; }

    public PaginatedGroupCollection PersonIdentityPager => PersonIdentityStage.Pager;
    public PaginatedGroupCollection LkidPager           => LkidStage.Pager;
    public PaginatedGroupCollection UsernamePager       => UsernameStage.Pager;
    public PaginatedGroupCollection AppleIdPager        => AppleIdStage.Pager;
    public PaginatedGroupCollection SystemEmailPager    => SystemEmailStage.Pager;

    // ---------------------------------------------------------------- Observable state

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

    // ---------------------------------------------------------------- Computed state

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
            : "KIDS-Export herunterladen";

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

    // ---------------------------------------------------------------- Constructor

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

    // ---------------------------------------------------------------- Input

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

            _originalLkidDups     = result.LkidDuplicates.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _originalUserDups     = result.UsernameDuplicates.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _originalAppleDups    = result.AppleIdDuplicates.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _originalSysEmailDups = result.SystemEmailDuplicates.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            HasResult = true;
            Progress = 100;
            IsError = false;

            // RecomputeFrom runs the stage pipeline. When every stage completes
            // (no duplicates, or user already resolved on a re-run), it calls
            // RebuildPreview → PreviewRows is populated from BuildResolvedOutput
            // so the grid matches the export byte-for-byte.
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

    // ---------------------------------------------------------------- Dialog

    [RelayCommand] private void OpenDuplicateDialog() { if (HasDuplicates) ShowDuplicateDialog = true; }
    [RelayCommand] private void CloseDuplicateDialog() => ShowDuplicateDialog = false;

    [RelayCommand]
    private void AcceptAllDefaults()
    {
        for (int pass = 0; pass < 6; pass++)
        {
            var snapshot = AllStages
                .Where(s => s.IsUnlocked)
                .SelectMany(s => s.Pager.AllGroups)
                .Where(g => !g.IsResolved)
                .ToList();

            if (snapshot.Count == 0) break;

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
    }

    [RelayCommand]
    private void DiscardAllRemaining()
    {
        for (int pass = 0; pass < 6; pass++)
        {
            var snapshot = AllStages
                .Where(s => s.IsUnlocked)
                .SelectMany(s => s.Pager.AllGroups.Select(g => (Stage: s, Group: g)))
                .Where(x => !x.Group.IsResolved)
                .ToList();

            if (snapshot.Count == 0) break;

            _suppressGroupHandlers = true;
            try
            {
                foreach (var (stage, g) in snapshot)
                {
                    if (stage.Kind is DuplicateKind.PersonIdentity)
                        g.AcceptDefault();  // must keep one
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

        _originalLkidDups     = new(StringComparer.OrdinalIgnoreCase);
        _originalUserDups     = new(StringComparer.OrdinalIgnoreCase);
        _originalAppleDups    = new(StringComparer.OrdinalIgnoreCase);
        _originalSysEmailDups = new(StringComparer.OrdinalIgnoreCase);

        RaiseAllCounts();
        OnPropertyChanged(nameof(HasPreview));
    }

    // ---------------------------------------------------------------- Pipeline

    private void RecomputeFrom(int startIdx)
    {
        if (Result is null || startIdx < 0) return;
        if (_inRecompute) return;
        _inRecompute = true;
        try
        {
            for (int i = startIdx; i < AllStages.Length; i++)
            {
                var stage = AllStages[i];
                var prevComplete = i == 0 || AllStages[i - 1].IsComplete;

                if (!prevComplete)
                {
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
                stage.IsComplete = stage.Pager.AllGroups.Count == 0
                                || stage.Pager.AllGroups.All(g => g.IsResolved);
            }

            // Preview reflects the export: rebuild only when the pipeline is
            // fully settled, so the grid shows the same Apple ID / System
            // E-Mail that will be written to disk.
            if (AllStagesComplete)
                RebuildPreview();

            RaiseAllCounts();
            RefreshPreviews();
        }
        finally
        {
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
                if (e.PropertyName == nameof(DuplicateGroup.IsResolved))
                    RecomputeFrom(stageIdx);
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

    /// <summary>
    /// Re-attaches user state to freshly rebuilt groups. Matches candidates
    /// by <see cref="PersonRecord.RecordId"/> (stable across every
    /// <c>with</c> copy) so selection survives recomputes even in LKID /
    /// PersonIdentity groups where multiple candidates share the same LKID.
    /// </summary>
    private static void ReconcileGroups(List<DuplicateGroup> oldGroups, List<DuplicateGroup> newGroups)
    {
        var oldByKey = oldGroups.ToDictionary(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var newG in newGroups)
        {
            if (!oldByKey.TryGetValue(newG.Key, out var oldG)) continue;

            var oldSel = oldG.SelectedCandidate;
            DuplicateCandidate? newSel = null;
            if (oldSel is not null)
                newSel = newG.Candidates.FirstOrDefault(c =>
                    c.Record.RecordId == oldSel.Record.RecordId);

            newG.SuppressSelectionUpdate = true;
            try
            {
                if (newSel is not null)
                    newG.SelectedCandidate = newSel;
                if (oldG.Mode != ResolutionMode.Unresolved)
                    newG.Mode = oldG.Mode;
            }
            finally
            {
                newG.SuppressSelectionUpdate = false;
            }
        }
    }

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

        // Phase 1 — decide per record
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
                            if (hasValue)
                            {
                                taken.Add(v!);
                                if (isUsername) taken.Add(UsernameGenerator.SanitizeUsername(v!));
                            }
                        }
                        else decisions[p] = new Decision(DecisionKind.Keep, null);
                        break;
                    case ResolutionMode.KeepAndGenerate:
                        if (selected)
                        {
                            decisions[p] = new Decision(DecisionKind.Keep, v);
                            if (hasValue)
                            {
                                taken.Add(v!);
                                if (isUsername) taken.Add(UsernameGenerator.SanitizeUsername(v!));
                            }
                        }
                        else decisions[p] = new Decision(DecisionKind.Generate, null);
                        break;
                    case ResolutionMode.KeepAndDisregard:
                        if (selected)
                        {
                            decisions[p] = new Decision(DecisionKind.Keep, v);
                            if (hasValue)
                            {
                                taken.Add(v!);
                                if (isUsername) taken.Add(UsernameGenerator.SanitizeUsername(v!));
                            }
                        }
                        else decisions[p] = new Decision(DecisionKind.Drop, null);
                        break;
                    default:
                        decisions[p] = new Decision(DecisionKind.Keep, v);
                        if (hasValue)
                        {
                            taken.Add(v!);
                            if (isUsername) taken.Add(UsernameGenerator.SanitizeUsername(v!));
                        }
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

        // Phase 2 — generate
        foreach (var p in stage.InputRecords)
        {
            if (decisions[p].Kind != DecisionKind.Generate) continue;
            var gen = GenerateFieldValue(p, taken, usernameTaken, stage.Kind);
            taken.Add(gen);
            if (isUsername) taken.Add(UsernameGenerator.SanitizeUsername(gen));
            decisions[p] = new Decision(DecisionKind.Keep, gen);
        }

        // Phase 3 — emit
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

    // ---------------------------------------------------------------- Previews

    private void RefreshPreviews()
    {
        foreach (var s in AllStages) UpdateStagePreviews(s);
    }

    private void UpdateStagePreviews(StageState stage)
    {
        if (!stage.IsUnlocked) return;

        var isDropStage = stage.Kind is DuplicateKind.PersonIdentity or DuplicateKind.Lkid;
        HashSet<Guid>? outputIds = isDropStage
            ? new HashSet<Guid>(stage.OutputRecords.Select(r => r.RecordId))
            : null;
        Dictionary<Guid, PersonRecord>? outputById = isDropStage
            ? null
            : stage.OutputRecords.ToDictionary(r => r.RecordId);

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
                        return outputIds!.Contains(r.RecordId)
                            ? ("→ behalten", DuplicateGroup.BrushKeep())
                            : ("→ verworfen (Zeile entfernt)", DuplicateGroup.BrushDrop());
                    }

                    if (!outputById!.TryGetValue(r.RecordId, out var outRec))
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

    // ---------------------------------------------------------------- Output

    /// <summary>
    /// Final record list. Fills missing Apple ID / System E-Mail from the
    /// (now unique) username so preview and export stay in sync.
    /// </summary>
    private List<PersonRecord> BuildResolvedOutput()
    {
        var final = AllStages[^1].OutputRecords;

        var takenApple = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var takenSysEmail = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in final)
        {
            if (!string.IsNullOrEmpty(p.AppleId)) takenApple.Add(p.AppleId);
            if (!string.IsNullOrEmpty(p.SystemEmail)) takenSysEmail.Add(p.SystemEmail);
        }

        var output = new List<PersonRecord>(final.Count);
        foreach (var p in final)
        {
            var appleId = p.AppleId;
            var sysEmail = p.SystemEmail;

            if (string.IsNullOrEmpty(appleId) && !string.IsNullOrEmpty(p.Username))
            {
                var candidate = UsernameGenerator.GenerateAppleId(p.Username);
                if (takenApple.Add(candidate)) appleId = candidate;
                else
                {
                    for (int i = 1; i < 1000; i++)
                    {
                        var alt = p.Username + i + "@appleid.ds-greiz.de";
                        if (takenApple.Add(alt)) { appleId = alt; break; }
                    }
                }
            }

            if (string.IsNullOrEmpty(sysEmail) && !string.IsNullOrEmpty(p.Username))
            {
                var soid = p.SoId;
                if (string.IsNullOrWhiteSpace(soid) && p.SchoolId is { Length: >= 2 } sid)
                    soid = sid[^2..];
                var candidate = UsernameGenerator.GenerateSystemEmail(p.Username, soid);
                if (takenSysEmail.Add(candidate)) sysEmail = candidate;
                else
                {
                    for (int i = 1; i < 1000; i++)
                    {
                        var alt = p.Username + i + "@" + (soid ?? "00") + ".schulen-greiz.de";
                        if (takenSysEmail.Add(alt)) { sysEmail = alt; break; }
                    }
                }
            }

            output.Add(p with { AppleId = appleId, SystemEmail = sysEmail });
        }

        return SortRecords(output);
    }

    /// <summary>
    /// Sort order shared by preview and export: school → role → grade (numeric)
    /// → class → name. "Role" places students first, then teachers, then others.
    /// Grade is parsed as an integer so "10" sorts after "9" rather than before
    /// (default string sort would do the wrong thing).
    /// </summary>
    private static List<PersonRecord> SortRecords(IEnumerable<PersonRecord> records) =>
        records
            .OrderBy(p => p.SchoolId ?? "", StringComparer.Ordinal)
            .ThenBy(p => p.IsStudent ? 0 : (p.IsTeacher ? 1 : 2))
            .ThenBy(p => ParseGrade(p.Grade))
            .ThenBy(p => p.ClassName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.FirstName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int ParseGrade(string? grade)
    {
        if (string.IsNullOrWhiteSpace(grade)) return 999;
        var m = System.Text.RegularExpressions.Regex.Match(grade, @"^\d+");
        return m.Success && int.TryParse(m.Value, out var n) ? n : 999;
    }

    /// <summary>
    /// Rebuilds <see cref="PreviewRows"/> from <see cref="BuildResolvedOutput"/>
    /// so the grid displays exactly what the export will contain (including
    /// generated Apple ID / System E-Mail). Duplicate markers are attached
    /// from the original import snapshot, so ⚠ columns still flag the rows
    /// whose identifiers were duplicated in the input.
    /// </summary>
    private void RebuildPreview()
    {
        if (Result is null) return;
        var resolved = BuildResolvedOutput();

        var rows = new List<PersonRow>(resolved.Count);
        foreach (var p in resolved)
        {
            rows.Add(PersonRow.From(
                p,
                isDupLkid:        _originalLkidDups.Contains(p.Lkid),
                isDupUsername:    p.Username is not null && _originalUserDups.Contains(p.Username),
                isDupAppleId:     p.AppleId is not null && _originalAppleDups.Contains(p.AppleId),
                isDupSystemEmail: p.SystemEmail is not null && _originalSysEmailDups.Contains(p.SystemEmail)));
        }
        PreviewRows = rows;
    }

    // ---------------------------------------------------------------- Download

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
        var config = ConfigStore.Current;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "KIDS-Export speichern",
            SuggestedFileName = $"kids-export-{DateTime.Now:yyyy-MM-dd}",
            DefaultExtension = "csv",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CSV (ANSI, Semikolon) (*.csv)") { Patterns = new[] { "*.csv" } },
                new FilePickerFileType("Excel-Arbeitsmappe (*.xlsx)") { Patterns = new[] { "*.xlsx" } },
            },
        });
        if (file is null) return;

        var fileName = string.IsNullOrWhiteSpace(file.Name) ? "kids-export.csv" : file.Name;
        var isCsv = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

        try
        {
            await using (var stream = await file.OpenWriteAsync())
            {
                stream.SetLength(0);
                if (isCsv)
                {
                    var csv = CsvWriter.Build(resolved, config);
                    var encoding = ResolveEncoding(config.Output.Encoding);
                    await using var writer = new StreamWriter(stream, encoding);
                    await writer.WriteAsync(csv);
                }
                else
                {
                    var bytes = XlsxWriter.Build(resolved, config);
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

    private static Encoding ResolveEncoding(string name) => (name ?? "").Trim().ToLowerInvariant() switch
    {
        "utf-8" or "utf8"                    => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        "utf-8-bom" or "utf8-bom"            => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        "latin1" or "iso-8859-1"             => Encoding.Latin1,
        "ansi" or "windows-1252" or "cp1252" => GetWindows1252(),
        _                                    => GetWindows1252(),
    };

    private static Encoding GetWindows1252()
    {
        try { return Encoding.GetEncoding(1252); }
        catch { return Encoding.Latin1; }
    }

    // ---------------------------------------------------------------- Notifications

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
