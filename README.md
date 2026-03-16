# MeshVault

**MeshVault** is a MelonLoader plugin for Schedule I that provides a shared mesh database and spawning API. Mods can query, spawn, and render game meshes by ID without needing to locate or extract geometry at runtime.

> **This is a library mod.** MeshVault does nothing on its own — it only provides functionality for other mods that depend on it. Install it only if a mod you use lists it as a dependency.

> **DUAL BUILD:** This plugin ships both `MeshVault.Il2Cpp.dll` and `MeshVault.Mono.dll`.
> Install both alongside **OTC Loader** (or any branch-detection plugin) and the correct DLL is loaded automatically.

## How it works

MeshVault stores mesh geometry, materials, submesh data, baked textures, and child meshes in a single `MeshDatabase.json` file. At runtime, the database is loaded once and meshes are spawned on demand via `MeshVaultAPI.Spawn()`.

**Loading priority:**
1. Disk file at `UserData/MeshVault/MeshDatabase.json` (if present)
2. Embedded resource baked into the DLL (release builds)

End users never need a disk file — the release DLL contains the full database. Developers and collaborators use the disk file so they can add and iterate on entries without rebuilding.

## For mod authors

Add a reference to `MeshVault.Il2Cpp.dll` or `MeshVault.Mono.dll` and call the API:

```csharp
using MeshVault;

// Check if a mesh exists
if (MeshVaultAPI.HasMesh("streetlight_01"))
{
    // Spawn it
    var go = MeshVaultAPI.Spawn("streetlight_01", position, rotation);
}

// List all available mesh IDs
string[] ids = MeshVaultAPI.ListMeshes();

// Get raw mesh data
MeshEntry entry = MeshVaultAPI.GetMesh("streetlight_01");
```

The API handles mesh construction, multi-submesh materials, baked texture lookups, scene material matching, and child mesh attachment automatically.

## Build configurations

MeshVault has three build configurations:

| Configuration | Runtime | Purpose |
|---|---|---|
| `MonoDebug` | Mono (netstandard2.1) | Development — includes extraction tools and write API |
| `Release` | IL2CPP (net6.0) | Distribution — read-only, embedded database |
| `MonoRelease` | Mono (netstandard2.1) | Distribution — read-only, embedded database |

### Debug builds (`MonoDebug`)

Debug builds include the full extraction toolset gated behind `#if DEBUG`:

* **MeshPlacer** — In-game tool (toggle with `F9`) for browsing scene hierarchies, extracting meshes, managing the database, and test-spawning entries.
* **Write/CRUD API** — `SaveMesh()`, `RemoveEntry()`, `RenameEntry()`, `WriteDatabase()` for modifying the database at runtime.

Debug builds write to `UserData/MeshVault/MeshDatabase.json` on disk. Post-build copy targets for local deployment can be configured in `LocalPaths.targets`.

### Release builds

Release builds are **read-only** — no extraction tools, no write API, no debug UI. The public surface is limited to `Init()`, `HasMesh()`, `GetMesh()`, `ListMeshes()`, `GetAllEntries()`, `Spawn()`, and the material/texture lookup helpers.

Release builds embed `MeshDatabase.json` directly into the DLL as a resource. A pre-build step automatically copies the latest database from your game's `UserData` folder into the project before compilation.

## Developer workflow

### Adding meshes to the database

1. Build and deploy `MonoDebug`.
2. Launch the game. Press `F9` to open MeshPlacer.
3. Use the **Hierarchy Picker** to browse scene objects and extract meshes.
4. Use the **Combined Mesh Browser** to manage, rename, delete, and test-spawn entries.
5. Extracted meshes are saved to `UserData/MeshVault/MeshDatabase.json` automatically.

### Building a release

1. Make sure your `MeshDatabase.json` is up to date (step 5 above).
2. Build a release configuration:
   ```
   dotnet build -c MonoRelease
   dotnet build -c Release
   ```
3. The pre-build target (`UpdateMeshDatabase`) automatically copies `MeshDatabase.json` from the path defined by `MeshDatabaseSourcePath` in `LocalPaths.targets` into the project's `Resources/` folder, where it is embedded into the DLL.
4. If the source file doesn't exist, the build fails with a clear error message telling you to extract meshes first.

### Project setup

Copy `LocalPaths.targets.example` (or create `LocalPaths.targets` manually) in the project directory and fill in your local paths:

* `R2ProfileRoot` — r2modman profile root (IL2CPP)
* `MonoR2ProfileRoot` — r2modman profile root (Mono)
* `MeshDatabaseSourcePath` — full path to your authoritative `MeshDatabase.json`
* Game DLL paths for each build configuration

`LocalPaths.targets` is gitignored — each developer maintains their own.

## Installation

MeshVault is installed automatically when you install a mod that depends on it. You do not need to install it separately unless a mod's instructions tell you to.

### Using a mod manager
When you install a dependent mod via **r2modman**, **Vortex**, or **Gale**, MeshVault will be pulled in as a dependency automatically.

### Manual installation
If a mod requires MeshVault and you are not using a mod manager, drop `MeshVault.Il2Cpp.dll` or `MeshVault.Mono.dll` (whichever matches your game branch) into your `Mods` folder.

## Requirements
- [MelonLoader](https://melonwiki.xyz/) v0.7.0+
- Schedule I by TVGS

## License
**Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0)**

You are free to:
* **Share** — copy and redistribute the material in any medium or format.
* **Adapt** — remix, transform, and build upon the material.

Under the following terms:
* **Attribution** — You must give appropriate credit to the original author (hdlmrell) and indicate if changes were made. You may not suggest the author endorses you or your use.
* **NonCommercial** — You may not use the material for commercial purposes.
* **ShareAlike** — If you remix, transform, or build upon the material, you must distribute your contributions under the same license as the original.
