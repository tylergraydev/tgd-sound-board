# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build the project
dotnet build src/TgdSoundboard/TgdSoundboard.csproj

# Run tests
dotnet test tests/TgdSoundboard.Tests/TgdSoundboard.Tests.csproj

# Run a specific test
dotnet test tests/TgdSoundboard.Tests/TgdSoundboard.Tests.csproj --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Build and run
dotnet run --project src/TgdSoundboard/TgdSoundboard.csproj
```

## Architecture Overview

This is a WPF soundboard application built with .NET 8, using MVVM pattern with CommunityToolkit.Mvvm.

### Service Layer (`src/TgdSoundboard/Services/`)

Services are instantiated as static singletons in `App.xaml.cs` and accessed via `App.ServiceName`:

- **DatabaseService** - SQLite persistence using Dapper. Stores categories, sound clips, and app settings. Data stored in `%LocalAppData%\TgdSoundboard\soundboard.db`
- **AudioPlaybackService** - NAudio-based audio playback with mixing, volume control, fade effects, speed/pitch adjustment. Manages a clip queue and active playback instances
- **ClipStorageService** - File management for audio clips, including import and trimming. Clips stored in `%LocalAppData%\TgdSoundboard\Clips`
- **GlobalHotkeyService** - Windows global hotkey registration for triggering clips from any application
- **StreamlabsService** - WebSocket integration with Streamlabs for instant replay functionality
- **ThemeService** - Runtime theme switching (Purple, Cyan, Neon themes)

### Key Patterns

- **MVVM**: `MainViewModel` is the primary ViewModel, bound to `MainWindow`. Uses `[ObservableProperty]` and `[RelayCommand]` source generators from CommunityToolkit.Mvvm
- **Async initialization**: Services load settings and data asynchronously in `App.OnStartup` before showing the main window
- **Audio pipeline**: NAudio's `MixingSampleProvider` allows multiple clips to play simultaneously. Custom `SpeedControlSampleProvider` and `PitchShiftSampleProvider` implement audio effects

### Data Model

- **Category** - Groups sound clips with name, color, and sort order
- **SoundClip** - Individual clip with audio file path, hotkey binding, volume, playback settings (loop, fade, speed, pitch), and favorite status
- **AppSettings** - Persisted user preferences (output device, volume, theme, Streamlabs config)

### UI Structure

- `MainWindow.xaml` - Single-window app with category tabs, clip grid, favorites bar, queue panel, and settings drawer
- `Controls/` - Custom WPF controls (clip editor, waveform display)
- `Themes/` - XAML resource dictionaries for theming
- Uses MaterialDesignThemes for UI components

## Testing

Tests use xUnit with FluentAssertions and Moq. Test project references the main project and can test services directly.
