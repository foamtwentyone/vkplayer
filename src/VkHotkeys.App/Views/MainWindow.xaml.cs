using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using VkHotkeys.App.Interop;
using VkHotkeys.App.ViewModels;

namespace VkHotkeys.App.Views;

public partial class MainWindow : Window
{
    private readonly Func<bool> _closeToTray;

    /// <summary>true — окно действительно закрывается (выход из программы), а не прячется в трей.</summary>
    public bool AllowClose { get; set; }

    public MainWindow(MainViewModel viewModel, Func<bool> closeToTray)
    {
        InitializeComponent();
        DataContext = viewModel;
        _closeToTray = closeToTray;
        SourceInitialized += (_, _) => NativeMethods.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            // Свернуть = убрать в трей; программа продолжает работать в фоне.
            Hide();
            WindowState = WindowState.Normal;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose && _closeToTray())
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        // Клик по пустому месту снимает фокус с поля ввода комбинации.
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, this);
    }

    public void BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }
}
