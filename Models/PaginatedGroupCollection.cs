using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KidsXmlToCsv.Models;

public sealed partial class PaginatedGroupCollection : ObservableObject
{
    public List<DuplicateGroup> AllGroups { get; } = new();
    public ObservableCollection<DuplicateGroup> VisibleGroups { get; } = new();

    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _pageSize = 25;

    public int TotalCount => AllGroups.Count;
    public int TotalPages => TotalCount == 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;
    public int StartIndex => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int EndIndex => Math.Min(Page * PageSize, TotalCount);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
    public bool IsEmpty => TotalCount == 0;
    public bool IsNonEmpty => TotalCount > 0;
    public string PageLabel => IsEmpty ? "—" : $"{StartIndex}–{EndIndex} von {TotalCount}";

    public void SetGroups(IEnumerable<DuplicateGroup> groups)
    {
        var oldPage = Page;
        AllGroups.Clear();
        AllGroups.AddRange(groups);
        var maxPage = TotalPages;
        Page = Math.Clamp(oldPage, 1, Math.Max(1, maxPage));
        RefreshVisible();
    }

    public void Clear()
    {
        AllGroups.Clear();
        VisibleGroups.Clear();
        Page = 1;
        RaiseAllChanged();
    }

    private void RefreshVisible()
    {
        VisibleGroups.Clear();
        foreach (var g in AllGroups.Skip((Page - 1) * PageSize).Take(PageSize))
            VisibleGroups.Add(g);
        RaiseAllChanged();
    }

    private void RaiseAllChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(StartIndex));
        OnPropertyChanged(nameof(EndIndex));
        OnPropertyChanged(nameof(HasPrevious));
        OnPropertyChanged(nameof(HasNext));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsNonEmpty));
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(Page));
        OnPropertyChanged(nameof(PageSize));
    }

    [RelayCommand] private void PreviousPage() { if (HasPrevious) { Page--; RefreshVisible(); } }
    [RelayCommand] private void NextPage()     { if (HasNext)     { Page++; RefreshVisible(); } }
}
