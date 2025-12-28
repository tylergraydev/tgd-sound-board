using System.Windows;
using System.Windows.Input;

namespace TgdSoundboard.Views;

public partial class RenameDialog : Window
{
    public string ClipName
    {
        get => NameTextBox.Text;
        set => NameTextBox.Text = value;
    }

    public RenameDialog(string currentName)
    {
        InitializeComponent();
        ClipName = currentName;

        Loaded += (s, e) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ClipName))
        {
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void NameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Save_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            Cancel_Click(sender, e);
        }
    }
}
