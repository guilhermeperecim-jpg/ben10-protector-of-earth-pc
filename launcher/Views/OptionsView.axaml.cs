using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ben10Launcher.Services;
using Ben10Launcher.ViewModels;

namespace Ben10Launcher.Views;

public partial class OptionsView : UserControl
{
    public OptionsView() => InitializeComponent();

    /// <summary>The game's shortcut picture: any image on the player's PC.</summary>
    private async void OnChoosePicture(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage || DataContext is not SettingsViewModel model)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("A picture for the game's shortcut"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(L.T("Pictures")) { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" } },
            },
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            model.SetShortcutPicture(path);
    }
}
