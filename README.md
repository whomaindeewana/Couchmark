<p align="center">
  <img src="assets/screenshots/logo.png" alt="Couchmark Logo" width="120"/>
</p>

<h1 align="center">Couchmark</h1>

<p align="center">
  <strong>The ultimate living room experience for your local media collection.</strong><br/>
  A Windows media center that remembers your exact watch progress — down to the second.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet" alt=".NET 9"/>
  <img src="https://img.shields.io/badge/platform-Windows-0078D4?logo=windows" alt="Windows"/>
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License"/>
  <img src="https://img.shields.io/badge/PRs-welcome-brightgreen" alt="PRs Welcome"/>
</p>

---

## ✨ Features

- **Jellyfin-like UI** — dark, modern Fluent design built with WPF-UI
- **Auto-scan library** — point it at your folders; movies and TV shows are detected automatically
- **Rich metadata** — posters, backdrops, ratings, synopsis, and cast from TMDB + IMDB (via OMDB)
- **Watch progress tracking** — saves your position every 5 seconds, just like a streaming service
- **Continue Watching** — resume right where you left off
- **TV show support** — season/episode grid with watched badges; detects only the seasons you have
- **Real-time file watcher** — add a new season folder and it appears instantly, no restart needed
- **Next episode auto-play** — 10-second countdown at episode end
- **External subtitles** — auto-loads `.srt` / `.ass` files alongside your video
- **Audio & subtitle track switcher** — switch embedded MKV tracks mid-playback
- **Keyboard shortcuts** — `Space`, `← →` seek, `F` fullscreen, `M` mute, `↑ ↓` volume
- **Right-click context menu** — mark watched/unwatched, open file location
- **Open source & MIT licensed**

## 🖥️ Screenshots

> _Screenshots coming soon after initial release._

## 🚀 Getting Started

### Prerequisites

- Windows 10 or later
- [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- A free [TMDB API key](https://www.themoviedb.org/settings/api)
- A free [OMDB API key](https://www.omdbapi.com/apikey.aspx) _(for IMDB ratings)_

### Installation

1. Download the latest release from the [Releases](../../releases) page.
2. Extract and run `Couchmark.exe`.
3. On first launch, go to **Settings** and enter your TMDB and OMDB API keys.
4. Add your Movies and TV Shows library folders.
5. Click **Scan Now** — Couchmark will do the rest.

### TV Show Folder Structure

Couchmark expects this naming convention:

```
Suits Season 3\
    Suits S03E01.mkv
    Suits S03E02.mkv
Suits Season 4\
    Suits S04E01.mkv
```

Movies can simply be:
```
The Dark Knight (2008).mkv
```

## 🤝 Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) for setup instructions and guidelines.

## 📄 License

This project is licensed under the [MIT License](LICENSE).
