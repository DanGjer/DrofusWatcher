# DrofusWatcher

DrofusWatcher is a minimal Assistant Revit extension framework.

## Scope

- Project identity and metadata are aligned with DrofusWatcher
- Copied business logic removed
- Framework command, args, and build configuration retained

## Project Structure

- `DrofusWatcher.cs` - main extension command scaffold
- `UserArgs.cs` - user argument scaffold
- `GlobalUsings.cs` - shared global usings
- `Configurations.targets` - multi-version Revit build configuration
- `DrofusWatcher.csproj` - project metadata and packaging settings

## Build

```powershell
dotnet build -c "Debug 2025"
```

## Next Step

Implement your new extension logic inside `DrofusWatcherCommand.Run(...)`.