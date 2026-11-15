using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using KidsXmlToCsv.ViewModels;

namespace KidsXmlToCsv;

public partial class DuplicateWindow : Window
{
    public DuplicateWindow()
    {
        InitializeComponent();
        TryLoadIcon();
    }

    public DuplicateWindow(MainViewModel vm) : this()
    {
        DataContext = vm;
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

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
