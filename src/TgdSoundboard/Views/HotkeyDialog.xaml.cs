using System.Windows;
using System.Windows.Input;
using TgdSoundboard.Services;

namespace TgdSoundboard.Views;

public partial class HotkeyDialog : Window
{
    public string Hotkey { get; private set; } = string.Empty;

    public HotkeyDialog(string currentHotkey = "")
    {
        InitializeComponent();
        Hotkey = currentHotkey;
        HotkeyDisplay.Text = string.IsNullOrEmpty(currentHotkey) ? "None" : currentHotkey;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Ignore modifier-only keypresses
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
            e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
            e.Key == Key.LeftShift || e.Key == Key.RightShift ||
            e.Key == Key.LWin || e.Key == Key.RWin ||
            e.Key == Key.System)
        {
            return;
        }

        // Get the actual key (handle system key for Alt combinations)
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Escape cancels
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            DialogResult = false;
            Close();
            return;
        }

        // Build hotkey string
        var modifiers = Keyboard.Modifiers;
        Hotkey = GlobalHotkeyService.FormatHotkey(modifiers, key);
        HotkeyDisplay.Text = Hotkey;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Hotkey = string.Empty;
        HotkeyDisplay.Text = "None";
    }
}
