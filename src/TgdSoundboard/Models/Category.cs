using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace TgdSoundboard.Models;

public partial class Category : ObservableObject
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _color = "#4CAF50";

    [ObservableProperty]
    private int _sortOrder;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ObservableCollection<SoundClip> _clips = new();
}
