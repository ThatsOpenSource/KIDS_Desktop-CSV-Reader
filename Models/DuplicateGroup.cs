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
    private readonly DuplicateGroup? _group;
    private bool _isSelected;

    public DuplicateCandidate(DuplicateGroup group, PersonRecord record)
    {
        _group = group;
        Record = record;
    }

    public DuplicateCandidate() { }

    public PersonRecord Record { get; init; } = new();

    public string Lkid => Record.Lkid;
    public string DisplayName => $"{Record.LastName}, {Record.FirstName}";
    public string DobDisplay => string.IsNullOrWhiteSpace(Record.DateOfBirth) ? "—" : Record.DateOfBirth!;
    public string SchoolDisplay => string.IsNullOrWhiteSpace(Record.SchoolName) ? "—" : Record.SchoolName!;
    public string ClassDisplay => string.IsNullOrWhiteSpace(Record.ClassName) ? "—" : Record.ClassName!;
    public string RoleDisplay => Record.Roles.Count == 0 ? "—" : string.Join(", ", Record.Roles);
    public string SourceFileDisplay => string.IsNullOrWhiteSpace(Record.SourceFile) ? "—" : Record.SourceFile!;
    public string RadioGroupName => _group?.RadioGroupName ?? "";

    [ObservableProperty] private string _currentValue = "";
    [ObservableProperty] private string _previewText = "→ noch nicht entschieden";
    [ObservableProperty] private IBrush? _previewBrush;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
            if (value && _group is not null && !_group.SuppressSelectionUpdate)
                _group.SelectByUser(this);
        }
    }

    internal void SetSelectedSilent(bool value)
    {
        if (_isSelected == value) return;
        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
    }
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

    public string RadioGroupName => $"{Kind}_{Key}";

    internal bool SuppressSelectionUpdate;

    public bool IsValueType => Kind is DuplicateKind.Username
                                      or DuplicateKind.AppleId
                                      or DuplicateKind.SystemEmail;

    public bool IsLkidOnly => Kind == DuplicateKind.Lkid;
    public bool IsPersonIdentity => Kind == DuplicateKind.PersonIdentity;

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
        DuplicateKind.Lkid           => "Mehrere Einträge mit derselben LKID. Wähle eine Zeile zum Behalten oder verwirf alle.",
        DuplicateKind.Username       => "Mehrere Einträge verwenden denselben Benutzernamen. Wähle einen zum Behalten; für die anderen kannst du generieren, leeren oder die Zeilen ganz verwerfen.",
        DuplicateKind.AppleId        => "Mehrere Einträge verwenden dieselbe Apple ID. Wähle einen zum Behalten; für die anderen kannst du generieren, leeren oder die Zeilen ganz verwerfen.",
        DuplicateKind.SystemEmail    => "Mehrere Einträge verwenden dieselbe System-E-Mail. Wähle einen zum Behalten; für die anderen kannst du generieren, leeren oder die Zeilen ganz verwerfen.",
        DuplicateKind.PersonIdentity => "Gleicher Name und Geburtsdatum, aber verschiedene LKIDs. Wähle eine Zeile zum Zusammenführen, oder behalte alle (verschiedene Personen).",
        _                            => "",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResolved))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(NeedsCandidate))]
    [NotifyPropertyChangedFor(nameof(IsKeepMode))]
    [NotifyPropertyChangedFor(nameof(IsBulkMode))]
    private ResolutionMode _mode = ResolutionMode.Unresolved;

    [ObservableProperty] private DuplicateCandidate? _selectedCandidate;

    public bool NeedsCandidate =>
        Mode is ResolutionMode.KeepAndBlank
             or ResolutionMode.KeepAndGenerate
             or ResolutionMode.KeepAndDisregard;

    public bool IsKeepMode =>
        Mode is ResolutionMode.KeepAndBlank
             or ResolutionMode.KeepAndGenerate
             or ResolutionMode.KeepAndDisregard;

    public bool IsBulkMode =>
        Mode is ResolutionMode.GenerateAll or ResolutionMode.DiscardAll or ResolutionMode.KeepAll;

    public bool IsResolved => Mode switch
    {
        ResolutionMode.Unresolved => false,
        ResolutionMode.KeepAndBlank
            or ResolutionMode.KeepAndGenerate
            or ResolutionMode.KeepAndDisregard => SelectedCandidate is not null,
        _ => true,
    };

    public PersonRecord? KeepRecord =>
        Mode is ResolutionMode.KeepAndBlank
             or ResolutionMode.KeepAndGenerate
             or ResolutionMode.KeepAndDisregard
            ? SelectedCandidate?.Record : null;

    public string StatusText => Mode switch
    {
        ResolutionMode.Unresolved       => "offen",
        ResolutionMode.KeepAndBlank     => IsLkidOnly || IsPersonIdentity
                                             ? "ausgewählte Zeile bleibt, andere werden verworfen"
                                             : "ausgewählte Zeile bleibt, andere Felder werden geleert",
        ResolutionMode.KeepAndGenerate  => "ausgewählter bleibt, andere werden generiert",
        ResolutionMode.KeepAndDisregard => "ausgewählter bleibt, andere Zeilen werden verworfen",
        ResolutionMode.GenerateAll      => "alle werden neu generiert",
        ResolutionMode.DiscardAll       => IsLkidOnly
                                             ? "alle Zeilen werden verworfen"
                                             : "Feld wird bei allen geleert",
        ResolutionMode.KeepAll          => "alle bleiben (verschiedene Personen)",
        _ => "",
    };

    public bool IsModeKeepAndBlank
    {
        get => Mode == ResolutionMode.KeepAndBlank;
        set { if (value) Mode = ResolutionMode.KeepAndBlank; }
    }
    public bool IsModeKeepAndGenerate
    {
        get => Mode == ResolutionMode.KeepAndGenerate;
        set { if (value) Mode = ResolutionMode.KeepAndGenerate; }
    }
    public bool IsModeKeepAndDisregard
    {
        get => Mode == ResolutionMode.KeepAndDisregard;
        set { if (value) Mode = ResolutionMode.KeepAndDisregard; }
    }

    partial void OnModeChanged(ResolutionMode value)
    {
        if (value is ResolutionMode.GenerateAll or ResolutionMode.DiscardAll or ResolutionMode.KeepAll)
        {
            var old = SelectedCandidate;
            if (old is not null)
            {
                SuppressSelectionUpdate = true;
                SelectedCandidate = null;
                SuppressSelectionUpdate = false;
                old.SetSelectedSilent(false);
            }
        }
        OnPropertyChanged(nameof(IsModeKeepAndBlank));
        OnPropertyChanged(nameof(IsModeKeepAndGenerate));
        OnPropertyChanged(nameof(IsModeKeepAndDisregard));
    }

    public void SelectByUser(DuplicateCandidate c)
    {
        if (SelectedCandidate == c)
        {
            if (!IsKeepMode) Mode = ResolutionMode.KeepAndGenerate;
            return;
        }
        var old = SelectedCandidate;
        SuppressSelectionUpdate = true;
        SelectedCandidate = c;
        SuppressSelectionUpdate = false;
        old?.SetSelectedSilent(false);
        c.SetSelectedSilent(true);

        if (!IsKeepMode)
            Mode = ResolutionMode.KeepAndGenerate;
        else
        {
            OnPropertyChanged(nameof(IsResolved));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public DuplicateGroup() { }

    public DuplicateGroup(string key, DuplicateKind kind, IEnumerable<PersonRecord> records)
    {
        Key = key;
        Kind = kind;
        foreach (var r in records)
            Candidates.Add(new DuplicateCandidate(this, r));
    }

    public void AcceptDefault()
    {
        if (Candidates.Count == 0) return;
        SelectByUser(Candidates[0]);
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
