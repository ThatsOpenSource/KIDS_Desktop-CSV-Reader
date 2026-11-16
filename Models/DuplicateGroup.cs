using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KidsXmlToCsv.Models;

public enum DuplicateKind { Lkid, Username, AppleId, SystemEmail, PersonIdentity }

public enum ResolutionMode
{
    Unresolved,
    KeepAndBlank,
    KeepAndGenerate,
    KeepAndDisregard,
    GenerateAll,
    DiscardAll,
    KeepAll,
}

public sealed partial class DuplicateCandidate : ObservableObject
{
    public DuplicateCandidate(DuplicateGroup group, PersonRecord record)
    {
        Record = record;
    }
    public DuplicateCandidate() { }

    public PersonRecord Record { get; init; } = new();

    public string Lkid => Record.Lkid;
    public string DisplayName => $"{Record.LastName}, {Record.FirstName}";
    public string DobDisplay => string.IsNullOrWhiteSpace(Record.DateOfBirth) ? "—" : Record.DateOfBirth!;
    public string SchoolDisplay => string.IsNullOrWhiteSpace(Record.SchoolName) ? "—" : Record.SchoolName!;
    public string StufeDisplay => string.IsNullOrWhiteSpace(Record.Grade) ? "—" : Record.Grade!;

    public string ClassDisplay =>
        string.IsNullOrWhiteSpace(Record.ClassName)
            ? "—"
            : Record.ClassName!.Replace('|', ',');

    public string ClassDisplayShort =>
        ClassDisplay.Length > 32 ? ClassDisplay[..29] + "…" : ClassDisplay;

    public string SourceFileDisplay =>
        string.IsNullOrWhiteSpace(Record.SourceFile) ? "—" : Record.SourceFile!;

    [ObservableProperty] private string _currentValue = "";
    [ObservableProperty] private string _previewText = "→ noch nicht entschieden";
    [ObservableProperty] private IBrush? _previewBrush;
}

public sealed partial class DuplicateGroup : ObservableObject
{
    private static readonly IBrush KeepBrush = new SolidColorBrush(Color.Parse("#1a7f37"));
    private static readonly IBrush GenerateBrush = new SolidColorBrush(Color.Parse("#b8860b"));
    private static readonly IBrush DropBrush = new SolidColorBrush(Color.Parse("#b30000"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#6b6b6b"));

    public string Key { get; init; } = "";
    public DuplicateKind Kind { get; init; }
    public ObservableCollection<DuplicateCandidate> Candidates { get; } = new();

    internal bool SuppressSelectionUpdate;

    public bool IsLkidOnly => Kind == DuplicateKind.Lkid;
    public bool IsPersonIdentity => Kind == DuplicateKind.PersonIdentity;
    public bool IsValueType => Kind is DuplicateKind.Username or DuplicateKind.AppleId or DuplicateKind.SystemEmail;

    public string TitleText => Kind switch
    {
        DuplicateKind.Lkid           => $"LKID {Key} — {Candidates.Count} Einträge",
        DuplicateKind.Username       => $"Benutzername „{Key}“ — {Candidates.Count} Einträge",
        DuplicateKind.AppleId        => $"Apple ID „{Key}“ — {Candidates.Count} Einträge",
        DuplicateKind.SystemEmail    => $"System-E-Mail „{Key}“ — {Candidates.Count} Einträge",
        DuplicateKind.PersonIdentity => $"{Key} — {Candidates.Count} Einträge",
        _                            => $"{Candidates.Count} Einträge",
    };

    public string HelpText => Kind switch
    {
        DuplicateKind.Lkid           => "Mehrere Einträge mit derselben LKID. Wähle eine Zeile zum Behalten; alle anderen Zeilen mit dieser LKID werden verworfen.",
        DuplicateKind.PersonIdentity => "Gleicher Name und Geburtsdatum, aber verschiedene LKIDs. Wähle eine Zeile zum Zusammenführen, oder behalte alle als verschiedene Personen.",
        _                            => "Wähle eine Zeile zum Behalten. Für die übrigen Einträge kannst du anschließend festlegen, ob das Feld geleert, neu generiert oder die ganze Zeile verworfen werden soll.",
    };

    // ---- Mode (manual property, bypasses [ObservableProperty] so we control notifications) ----
    private ResolutionMode _mode = ResolutionMode.Unresolved;
    public ResolutionMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            OnPropertyChanged(nameof(Mode));

            if (value is ResolutionMode.GenerateAll or ResolutionMode.DiscardAll or ResolutionMode.KeepAll
                && _selectedCandidate is not null)
            {
                SuppressSelectionUpdate = true;
                try { SelectedCandidate = null; }
                finally { SuppressSelectionUpdate = false; }
            }

            RaiseModeDependents();
        }
    }

    // ---- SelectedCandidate (manual property, sets default Mode when user picks) ----
    private DuplicateCandidate? _selectedCandidate;
    public DuplicateCandidate? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (ReferenceEquals(_selectedCandidate, value)) return;
            _selectedCandidate = value;
            OnPropertyChanged(nameof(SelectedCandidate));

            if (!SuppressSelectionUpdate && value is not null)
            {
                if (!IsKeepMode)
                {
                    _mode = ResolutionMode.KeepAndBlank;
                    OnPropertyChanged(nameof(Mode));
                }
                RaiseModeDependents();
            }
        }
    }

    private void RaiseModeDependents()
    {
        OnPropertyChanged(nameof(IsModeKeepAndBlank));
        OnPropertyChanged(nameof(IsModeKeepAndGenerate));
        OnPropertyChanged(nameof(IsModeKeepAndDisregard));
        OnPropertyChanged(nameof(IsKeepMode));
        OnPropertyChanged(nameof(IsBulkMode));
        OnPropertyChanged(nameof(NeedsCandidate));
        OnPropertyChanged(nameof(IsResolved));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(KeepRecord));
    }

    public bool NeedsCandidate =>
        _mode is ResolutionMode.KeepAndBlank
               or ResolutionMode.KeepAndGenerate
               or ResolutionMode.KeepAndDisregard;

    public bool IsKeepMode =>
        _mode is ResolutionMode.KeepAndBlank
               or ResolutionMode.KeepAndGenerate
               or ResolutionMode.KeepAndDisregard;

    public bool IsBulkMode =>
        _mode is ResolutionMode.GenerateAll or ResolutionMode.DiscardAll or ResolutionMode.KeepAll;

    public bool IsResolved => _mode switch
    {
        ResolutionMode.Unresolved => false,
        ResolutionMode.KeepAndBlank
            or ResolutionMode.KeepAndGenerate
            or ResolutionMode.KeepAndDisregard => _selectedCandidate is not null,
        _ => true,
    };

    public PersonRecord? KeepRecord =>
        IsKeepMode ? _selectedCandidate?.Record : null;

    public string StatusText => _mode switch
    {
        ResolutionMode.Unresolved => "offen",

        ResolutionMode.KeepAndBlank => (IsLkidOnly || IsPersonIdentity)
            ? "ausgewählte Zeile bleibt, andere werden verworfen"
            : "ausgewählte Zeile bleibt, andere Felder werden geleert",

        ResolutionMode.KeepAndGenerate => (IsLkidOnly || IsPersonIdentity)
            ? "ausgewählte Zeile bleibt, andere werden verworfen"
            : "ausgewählter bleibt, andere werden generiert",

        ResolutionMode.KeepAndDisregard => "ausgewählter bleibt, andere Zeilen werden verworfen",

        ResolutionMode.GenerateAll => (IsLkidOnly || IsPersonIdentity)
            ? "alle Zeilen werden verworfen"
            : "alle werden neu generiert",

        ResolutionMode.DiscardAll => (IsLkidOnly || IsPersonIdentity)
            ? "alle Zeilen werden verworfen"
            : "Feld wird bei allen geleert",

        ResolutionMode.KeepAll => "alle bleiben (verschiedene Personen)",
        _ => "",
    };

    // ---- Bool bridges for RadioButton GroupName bindings on the sub-options ----
    public bool IsModeKeepAndBlank
    {
        get => _mode == ResolutionMode.KeepAndBlank;
        set { if (value) Mode = ResolutionMode.KeepAndBlank; }
    }
    public bool IsModeKeepAndGenerate
    {
        get => _mode == ResolutionMode.KeepAndGenerate;
        set { if (value) Mode = ResolutionMode.KeepAndGenerate; }
    }
    public bool IsModeKeepAndDisregard
    {
        get => _mode == ResolutionMode.KeepAndDisregard;
        set { if (value) Mode = ResolutionMode.KeepAndDisregard; }
    }

    public DuplicateGroup() { }

    public DuplicateGroup(string key, DuplicateKind kind, IEnumerable<PersonRecord> records) : this()
    {
        Key = key;
        Kind = kind;
        foreach (var r in records)
            Candidates.Add(new DuplicateCandidate(this, r));
    }

    public void AcceptDefault()
    {
        if (Candidates.Count == 0) return;
        SelectedCandidate = Candidates[0];
    }

    [RelayCommand] private void SetGenerateAll() => Mode = ResolutionMode.GenerateAll;
    [RelayCommand] private void SetDiscardAll() => Mode = ResolutionMode.DiscardAll;
    [RelayCommand] private void SetKeepAll() => Mode = ResolutionMode.KeepAll;

    public void ApplyPreviews(Func<PersonRecord, string> currentValueFn,
                              Func<PersonRecord, (string text, IBrush? brush)> previewFn)
    {
        foreach (var c in Candidates)
        {
            c.CurrentValue = currentValueFn(c.Record);
            var (text, brush) = previewFn(c.Record);
            c.PreviewText = text;
            c.PreviewBrush = brush;
        }
    }

    public static IBrush BrushKeep() => KeepBrush;
    public static IBrush BrushGenerate() => GenerateBrush;
    public static IBrush BrushDrop() => DropBrush;
    public static IBrush BrushMuted() => MutedBrush;
}
