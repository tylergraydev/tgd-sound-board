using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TgdSoundboard.Models;
using TgdSoundboard.Services;

namespace TgdSoundboard.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Category> _categories = new();

    [ObservableProperty]
    private Category? _selectedCategory;

    [ObservableProperty]
    private ObservableCollection<AudioDevice> _outputDevices = new();

    [ObservableProperty]
    private ObservableCollection<AudioDevice> _inputDevices = new();

    [ObservableProperty]
    private AudioDevice? _selectedOutputDevice;

    [ObservableProperty]
    private AudioDevice? _selectedVirtualCableDevice;

    [ObservableProperty]
    private AudioDevice? _selectedInputDevice;

    [ObservableProperty]
    private float _masterVolume = 1.0f;

    [ObservableProperty]
    private bool _passSystemAudio;

    [ObservableProperty]
    private bool _passMicrophone;

    [ObservableProperty]
    private bool _isRoutingActive;

    [ObservableProperty]
    private bool _isVirtualCableAvailable;

    [ObservableProperty]
    private AppSettings _settings = new();

    [ObservableProperty]
    private bool _isClipEditorOpen;

    [ObservableProperty]
    private string? _clipEditorFilePath;

    public MainViewModel()
    {
        App.AudioPlayback.ClipStarted += OnClipStarted;
        App.AudioPlayback.ClipStopped += OnClipStopped;
        App.AudioRouter.RoutingStatusChanged += OnRoutingStatusChanged;
    }

    public async Task InitializeAsync()
    {
        // Load settings
        Settings = await App.Database.GetAppSettingsAsync();
        MasterVolume = Settings.MasterVolume;
        PassSystemAudio = Settings.PassSystemAudio;
        PassMicrophone = Settings.PassMicrophone;

        // Load audio devices
        RefreshAudioDevices();

        // Load categories
        var categories = await App.Database.GetCategoriesAsync();
        Categories = new ObservableCollection<Category>(categories);

        if (Categories.Count > 0)
        {
            SelectedCategory = Categories[0];
            SelectedCategory.IsSelected = true;
        }

        // Check for virtual cable
        IsVirtualCableAvailable = AudioRouterService.FindVirtualCableDevice() != null;
    }

    private void RefreshAudioDevices()
    {
        var outputs = AudioPlaybackService.GetOutputDevices();
        OutputDevices = new ObservableCollection<AudioDevice>(outputs);

        var inputs = AudioPlaybackService.GetInputDevices();
        InputDevices = new ObservableCollection<AudioDevice>(inputs);

        // Select saved devices
        SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == Settings.OutputDeviceId)
            ?? OutputDevices.FirstOrDefault(d => d.IsDefault);

        SelectedVirtualCableDevice = OutputDevices.FirstOrDefault(d => d.Id == Settings.VirtualCableDeviceId)
            ?? OutputDevices.FirstOrDefault(d => d.IsVirtualCable);

        SelectedInputDevice = InputDevices.FirstOrDefault(d => d.Id == Settings.InputDeviceId)
            ?? InputDevices.FirstOrDefault(d => d.IsDefault);
    }

    partial void OnMasterVolumeChanged(float value)
    {
        App.AudioPlayback.MasterVolume = value;
        Settings.MasterVolume = value;
        _ = App.Database.SaveAppSettingsAsync(Settings);
    }

    partial void OnSelectedCategoryChanged(Category? value)
    {
        foreach (var category in Categories)
        {
            category.IsSelected = category == value;
        }
    }

    [RelayCommand]
    private void PlayClip(SoundClip clip)
    {
        if (clip.IsPlaying)
        {
            App.AudioPlayback.StopClip(clip.Id);
        }
        else
        {
            App.AudioPlayback.PlayClip(clip);
        }
    }

    [RelayCommand]
    private void StopAll()
    {
        App.AudioPlayback.StopAll();
    }

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        var category = await App.Database.AddCategoryAsync("New Category");
        Categories.Add(category);
        SelectedCategory = category;
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync(Category category)
    {
        if (Categories.Count <= 1) return;

        await App.Database.DeleteCategoryAsync(category.Id);
        Categories.Remove(category);

        if (SelectedCategory == category)
        {
            SelectedCategory = Categories.FirstOrDefault();
        }
    }

    [RelayCommand]
    private async Task AddClipAsync(string filePath)
    {
        if (SelectedCategory == null) return;

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var storedPath = await App.ClipStorage.ImportAudioFileAsync(filePath, fileName);
        var duration = AudioPlaybackService.GetAudioDuration(storedPath);

        var clip = new SoundClip
        {
            Name = fileName,
            FilePath = storedPath,
            CategoryId = SelectedCategory.Id,
            Duration = duration
        };

        await App.Database.AddClipAsync(clip);
        SelectedCategory.Clips.Add(clip);
    }

    [RelayCommand]
    private async Task DeleteClipAsync(SoundClip clip)
    {
        App.AudioPlayback.StopClip(clip.Id);
        App.ClipStorage.DeleteClipFile(clip.FilePath);
        await App.Database.DeleteClipAsync(clip.Id);

        SelectedCategory?.Clips.Remove(clip);
    }

    [RelayCommand]
    private void OpenClipEditor(string filePath)
    {
        ClipEditorFilePath = filePath;
        IsClipEditorOpen = true;
    }

    [RelayCommand]
    private void CloseClipEditor()
    {
        IsClipEditorOpen = false;
        ClipEditorFilePath = null;
    }

    [RelayCommand]
    private async Task SaveTrimmedClipAsync((string FilePath, string Name, TimeSpan Start, TimeSpan End) args)
    {
        if (SelectedCategory == null) return;

        var storedPath = await App.ClipStorage.SaveTrimmedClipAsync(args.FilePath, args.Name, args.Start, args.End);
        var duration = args.End - args.Start;

        var clip = new SoundClip
        {
            Name = args.Name,
            FilePath = storedPath,
            CategoryId = SelectedCategory.Id,
            Duration = duration
        };

        await App.Database.AddClipAsync(clip);
        SelectedCategory.Clips.Add(clip);

        CloseClipEditor();
    }

    [RelayCommand]
    private void ToggleRouting()
    {
        if (IsRoutingActive)
        {
            App.AudioRouter.StopRouting();
        }
        else
        {
            App.AudioRouter.StartRouting(
                SelectedVirtualCableDevice?.Id,
                PassSystemAudio,
                PassMicrophone,
                null, // Use default loopback
                SelectedInputDevice?.Id);
        }
    }

    private void OnClipStarted(object? sender, int clipId)
    {
        var clip = FindClipById(clipId);
        if (clip != null)
        {
            clip.IsPlaying = true;
        }
    }

    private void OnClipStopped(object? sender, int clipId)
    {
        var clip = FindClipById(clipId);
        if (clip != null)
        {
            clip.IsPlaying = false;
        }
    }

    private void OnRoutingStatusChanged(object? sender, bool isActive)
    {
        IsRoutingActive = isActive;
    }

    private SoundClip? FindClipById(int clipId)
    {
        foreach (var category in Categories)
        {
            var clip = category.Clips.FirstOrDefault(c => c.Id == clipId);
            if (clip != null) return clip;
        }
        return null;
    }
}
