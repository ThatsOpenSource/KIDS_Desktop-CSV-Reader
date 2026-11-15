using CommunityToolkit.Mvvm.ComponentModel;

namespace KidsXmlToCsv.Models;

/// <summary>
/// State for one stage of the sequential duplicate-resolution pipeline.
/// Holds its own pager (with current-page groups) and the input/output record
/// sets for that stage. Output of stage N becomes input of stage N+1.
/// </summary>
public sealed partial class StageState : ObservableObject
{
    public DuplicateKind Kind { get; init; }
    public string Label { get; init; } = "";
    public PaginatedGroupCollection Pager { get; } = new();

    public List<PersonRecord> InputRecords { get; set; } = new();
    public List<PersonRecord> OutputRecords { get; set; } = new();

    [ObservableProperty] private bool _isUnlocked;
    [ObservableProperty] private bool _isComplete;

    public int Count => Pager.TotalCount;
    public int UnresolvedCount => Pager.AllGroups.Count(g => !g.IsResolved);
    public bool IsEmpty => Count == 0;
    public bool HasGroups => Count > 0;
}
