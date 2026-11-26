using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Ben10Launcher.Views;

public partial class PageHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PageHeader, string>(nameof(Title), "");

    public static readonly StyledProperty<ICommand?> BackCommandProperty =
        AvaloniaProperty.Register<PageHeader, ICommand?>(nameof(BackCommand));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public ICommand? BackCommand
    {
        get => GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    public PageHeader() => InitializeComponent();
}
