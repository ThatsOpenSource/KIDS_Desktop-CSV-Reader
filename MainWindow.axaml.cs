using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using KidsXmlToCsv.Models;
using KidsXmlToCsv.Services;
using KidsXmlToCsv.ViewModels;

namespace KidsXmlToCsv;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext!;
    private AppState _state = new();
    private bool _dialogOpen;

    public MainWindow()
    {
        InitializeComponent();
        TryLoadIcon();

        _state = AppStateStore.Load();
        if (_state.HasValidSize)
        {
            Width = _state.Width;
            Height = _state.Height;
        }
        if (_state.HasValidPosition)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)_state.X!.Value, (int)_state.Y!.Value);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if (_state.IsMaximized) WindowState = WindowState.Maximized;

        var vm = new MainViewModel();
        DataContext = vm;

        DropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);

        vm.DownloadCommand = new AsyncRelayCommand(
            execute: () => vm.DownloadAsync(StorageProvider),
            canExecute: () => vm.CanDownloadNow);

        vm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ShowDuplicateDialog)
                && vm.ShowDuplicateDialog
                && !_dialogOpen)
            {
                await ShowDuplicateDialogAsync();
            }
        };
    }

    private void TryLoadIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://KIDS/Assets/icon.png"));
            Icon = new WindowIcon(stream);
        }
        catch { /* icon optional */ }
    }

    private async Task ShowDuplicateDialogAsync()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var dlg = new DuplicateWindow(Vm)
            {
                Width = Math.Max(900, Width * 0.85),
                Height = Math.Max(650, Height * 0.85),
            };
            await dlg.ShowDialog(this);
        }
        catch { /* user closed */ }
        finally
        {
            _dialogOpen = false;
            Vm.ShowDuplicateDialog = false;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        try
        {
            AppStateStore.Save(new AppState
            {
                Width = Width,
                Height = Height,
                X = Position.X,
                Y = Position.Y,
                IsMaximized = WindowState == WindowState.Maximized,
            });
        }
        catch { /* best effort */ }
        base.OnClosing(e);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.GetFiles() is not { } files) return;

        var paths = files.Select(f => f.TryGetLocalPath())
            .Where(p => p is not null &&
                       (p.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            .Cast<string>().ToArray();

        if (paths.Length == 0)
        {
            Vm.Status = "Nur CSV- oder ZIP-Dateien werden unterstützt.";
            Vm.IsError = true;
            return;
        }
        Vm.AddFiles(paths);
    }

    private async void PickFiles_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "CSV-Dateien auswählen",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("CSV / ZIP") { Patterns = new[] { "*.csv", "*.zip" } },
            },
        });

        var paths = files.Select(f => f.TryGetLocalPath())
                         .Where(p => p is not null).Cast<string>().ToArray();
        if (paths.Length > 0) Vm.AddFiles(paths);
    }
}
