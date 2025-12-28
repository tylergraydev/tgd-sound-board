# TGD Soundboard

A modern soundboard application for streamers and content creators. Play sound clips, route audio through virtual cables, and manage audio levels - all in one sleek interface.

![TGD Soundboard Screenshot](docs/images/screenshot.png)

## Features

- **Sound Clip Playback** - Organize and play sound clips with a single click
- **Category Organization** - Group clips into categories for easy access
- **Virtual Audio Cable Integration** - Route soundboard audio to OBS, Discord, or any application via VB-Cable
- **Audio Mixer** - Control levels for microphone, system audio, and individual apps
- **Monitor Mode** - Preview what your stream hears through your speakers
- **Per-App Audio Routing** - Capture audio from specific applications
- **Clip Editor** - Import songs and trim specific sections with waveform visualization
- **Multiple Themes** - Choose from Purple, Cyan, or Neon themes
- **Auto-Updates** - Automatically checks for and installs updates

## Installation

1. Download the latest installer from [Releases](https://github.com/tylergraydev/tgd-sound-board/releases)
2. Run `TgdSoundboard_Setup_x.x.x.exe`
3. The installer will optionally install VB-Cable virtual audio driver

## Usage

### Adding Sound Clips
- Drag and drop audio files onto the window
- Click **Import** to browse for files
- Use **Clip from Song** to trim sections from longer audio files

### Audio Routing
1. Select your virtual cable device in the bottom bar
2. Toggle **Play** to route soundboard audio to the virtual cable
3. In OBS/Discord, select "CABLE Output" as your microphone

### Mixing Audio
- Click **Mixer** to open the audio mixer
- Adjust volume for each audio source
- Enable **Monitor** to hear what goes to the virtual cable

### Themes
Use the dropdown in the header to switch between:
- **Purple** - Clean dark theme with purple accents
- **Cyan** - Modern dark theme with teal accents
- **Neon** - Cyberpunk style with pink/cyan gradients

## Requirements

- Windows 10/11 (64-bit)
- .NET 8.0 Runtime (included in installer)
- VB-Cable (included in installer)

## Building from Source

```bash
git clone https://github.com/tylergraydev/tgd-sound-board.git
cd tgd-sound-board
dotnet build src/TgdSoundboard/TgdSoundboard.csproj
```

## Tech Stack

- .NET 8 + WPF
- NAudio for audio playback and routing
- Material Design in XAML for UI components
- SQLite for data persistence

## License

MIT License
