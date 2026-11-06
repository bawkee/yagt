# Repository layout checks

Run `dotnet run --project tests/LayoutChecks/LayoutChecks.csproj` from the repository root. This opens a temporary test window and uses an isolated repository and preferences directory, which are removed afterwards.

The checks cover all three history layouts, Light and Dark themes, 100% and 150% zoom, narrow windows, saved preferences, sidebar resizing and scrolling, search focus and selection, details collapse, matching subject highlighting, and repository page switching. The fixture includes 151 branches to exercise the virtualized branch list.

Add `-- --render` to save screenshots alongside the check executable. These are ignored build outputs.
