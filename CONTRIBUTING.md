# Contributing to Couchmark

Thank you for your interest in contributing! This document explains how to set up the project locally and submit changes.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) (Community edition is free) or [JetBrains Rider](https://www.jetbrains.com/rider/)
- A free [TMDB API key](https://www.themoviedb.org/settings/api)
- A free [OMDB API key](https://www.omdbapi.com/apikey.aspx)

## Getting Started

1. Fork the repository and clone your fork:
   ```bash
   git clone https://github.com/YOUR_USERNAME/Couchmark.git
   cd Couchmark
   ```

2. Create a local settings file (this is git-ignored and will hold your personal API keys):
   ```bash
   copy src\Couchmark\appsettings.json src\Couchmark\appsettings.local.json
   ```
   Then fill in your TMDB and OMDB API keys in `appsettings.local.json`.

3. Build the solution:
   ```bash
   dotnet build
   ```

4. Run the app:
   ```bash
   dotnet run --project src/Couchmark
   ```

5. Run the tests:
   ```bash
   dotnet test
   ```

## Code Style

This project uses `.editorconfig` to enforce consistent formatting. Your editor should pick this up automatically. Key rules:
- **4 spaces** for indentation (no tabs)
- **Allman brace style** — opening `{` on its own line
- **XML doc comments** (`///`) on all public members
- **One class per file**

## Branching & PRs

- Branch from `main`
- Name your branch: `feature/my-feature` or `fix/my-bug-fix`
- Keep PRs focused — one feature or fix per PR
- Fill in the pull request template when submitting

## Reporting Issues

Use the GitHub issue templates for bug reports and feature requests.
