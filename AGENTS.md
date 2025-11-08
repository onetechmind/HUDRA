# Repository Guidelines

## Project Structure & Module Organization
HUDRA is a WinUI 3/.NET 8 desktop app. Core source lives in `HUDRA/`: `Controls/` holds reusable XAML controls, `Pages/` defines UI screens, `Services/` encapsulates hardware and OS integrations, and `Helpers/`/`Utils/` host cross-cutting helpers. Runtime assets (icons, sounds) reside in `Assets/`, while hardware binaries and scripts live under `Tools/ryzenadj/`—keep that directory intact for TDP control. Configuration defaults are in `Configuration/Settings.xml`; design specs and research live in `Specs/` and `techdocs/` for reference.

## Build, Test, and Development Commands
Run `dotnet restore` at the repository root before first build. Use `dotnet build HUDRA/HUDRA.csproj -c Debug` for local development or switch to `Release` when packaging. `dotnet run --project HUDRA/HUDRA.csproj` will launch the WinUI shell for quick smoke checks; for full debugging attach via Visual Studio 2022 with Windows App SDK workloads installed. When modifying native tooling, rebuild once more to ensure all content files copy to `bin/`.

## Coding Style & Naming Conventions
Follow standard C# conventions: 4-space indentation, PascalCase for public types/members, and `_camelCase` for private fields (as seen in `MainWindow.xaml.cs`). Nullable reference types are enabled—address warnings rather than suppressing them. Keep XAML tidy with compact attribute ordering and place view models or services in matching namespaces. Prefer explicit async suffixes (`LoadAsync`) and guard hardware calls with capability checks.

## Testing Guidelines
No automated test project ships yet; manual validation is expected. Exercise critical flows after changes: adjust TDP, toggle performance profiles, confirm overlays on an AMD Ryzen handheld, and verify resume-from-hibernate scenarios using the specs in `Specs/`. Always rebuild before verification to catch binding errors. Document edge cases or reproduction steps in the PR description if manual steps are required.

## Commit & Pull Request Guidelines
Commit summaries should be concise (<=72 chars) and describe intent (e.g., "Refine expander state handling"). Group related changes per commit. Pull requests need: a brief narrative of the change, linked issues when available, test notes or manual validation evidence, and screenshots/GIFs for UI updates. Highlight any impacts to `Tools/` binaries or configuration so reviewers can re-validate elevated operations.

## Security & Configuration Tips
RyzenAdj tooling requires administrator rights—avoid committing modified binaries without provenance. Never store personal hardware IDs or secrets in `Configuration/`. When sharing logs, scrub paths that could reveal user data, and confirm new scripts respect least-privilege execution.
Hello, World!
