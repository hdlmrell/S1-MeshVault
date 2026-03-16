# MeshVault Coding Standards
Adapted from the OverTheCounter mod's coding standards for the MeshVault plugin codebase.
These keep the project consistent and predictable without being overly strict.

## General Best Practice
* Review the codebase thoroughly before doing a PR.

## File and Namespace Structure
* All classes must exist in a logical namespace matching the folder structure.
```C#
namespace MeshVault { ... }
```

## Naming Conventions
* In general, naming follows the default Jetbrains Rider suggestions.
* **PascalCase** for class names, methods, properties, and non-private fields.
* **camelCase** for local variables and private fields.
* Prefix private fields and internal fields with `_`.
```C#
private int _myInteger;
internal string _name;
private static bool _loaded;
```
* Private static readonly fields follow the `_` prefix rule like other private fields.
```C#
private static readonly Dictionary<string, Material> _materialCache = new();
```
* Non-private static readonly fields use PascalCase.
```C#
public static readonly float[] DefaultColor = { 1f, 1f, 1f, 1f };
```
* Enums do not need to be prefixed with `E`.
* Utilize existing common naming conventions from the codebase.

## Access Modifiers
* Explicit usage of access modifiers at all times.
* Use `static` on utility classes that hold no instance state.
```C#
public static class MeshVaultAPI { ... }
```
* Arrow functions (`=>`) are used for simple single-expression methods and properties.
```C#
// property example
private static string DiskPath =>
    Path.Combine(Application.dataPath, "..", "UserData", "MeshVault", "MeshDatabase.json");
```
* Use `readonly` or `const` for immutable values.
```C#
private const float DefaultSmoothness = 0.1f;
```
* Nullable variables should be declared using `?`.

## Documentation
* All `public` methods and properties should have XML summaries.
```C#
/// <summary>
/// Spawns a mesh as a GameObject from the database.
/// </summary>
/// <param name="id">The mesh entry ID.</param>
public static GameObject Spawn(string id, Vector3 position, Quaternion rotation, ...) { ... }
```
* Internal/private members should have summaries when the logic isn't self-evident.

## Build Configurations & Debug/Release Split
* Three configurations: `MonoDebug` (Mono, development), `Release` (IL2CPP, distribution), `MonoRelease` (Mono, distribution).
* All extraction tools, debug UI, and write/CRUD API are gated behind `#if DEBUG`. Release builds are **read-only**.
* Release builds embed `MeshDatabase.json` as a resource. A pre-build MSBuild target (`UpdateMeshDatabase`) copies the latest database from `MeshDatabaseSourcePath` (defined in `LocalPaths.targets`) into `Resources/MeshDatabase.json` automatically.
* Post-build copy targets for local deployment are configured in `LocalPaths.targets` (gitignored, machine-specific).
* Build from the **project directory** (`S1-MeshVault/S1-MeshVault/`), not the solution root.

## Conditional Build Compilation
* MeshVault targets MelonLoader only (IL2CPP + Mono). Use `#if IL2CPP` / `#else` for platform-specific logic when needed.
* MeshVault has zero game-specific types — conditional compilation should only be needed for Unity/MelonLoader API differences between runtimes.

## Logging
* Use `Melon<MeshVaultPlugin>.Logger` for all logging. Do **not** use `MelonLogger` directly.
```C#
Melon<MeshVaultPlugin>.Logger.Msg($"Loaded {_cache.Count} mesh entries from disk");
Melon<MeshVaultPlugin>.Logger.Warning($"Entry \"{id}\" not found");
Melon<MeshVaultPlugin>.Logger.Error($"Failed to load: {ex.Message}");
```
* **Release builds should have no verbose logging** — only warnings and errors. Use `Melon<MeshVaultPlugin>.Logger.Msg()` only in `#if DEBUG` blocks or for essential one-time startup messages (e.g. "Loaded N entries"). Routine per-operation messages belong behind `#if DEBUG`.

## Code Organization
* Group related members together using comment separators.
* Organize classes with: internal/static fields first, then public API, then private implementation.
* Keep the public API surface minimal — only expose what consuming mods need.

## Mesh Database Standards

### Entry IDs
* Every entry must have a **unique ID**. IDs are the primary key — duplicates silently overwrite.
* Use `snake_case` for all IDs. Lowercase letters, digits, and underscores only.
* IDs follow the pattern: `object_variant` or `object_location_variant` when context matters.
  ```
  dumpster_blue
  dumpster_green
  bench_park_wood
  shelf_wall_small
  ```
* **Color and material variations** must be described in the ID. Never store two visually different meshes under a generic name like `dumpster` — always qualify: `dumpster_blue`, `dumpster_rust`.
* **Size or type variations** use a suffix: `_small`, `_large`, `_tall`, `_wide`.
* Numbered suffixes (`_01`, `_02`) are acceptable when objects are genuinely interchangeable variants of the same thing (e.g. `trash_bag_01`, `trash_bag_02`), but prefer descriptive names when the variants differ visually.

### Avoiding Duplicates
* Before extracting a mesh, check the existing database for entries covering the same object.
* Do not store the same geometry under multiple IDs. If a mesh is reused in different contexts, store it once and let consuming mods position it.
* Combined meshes (multi-submesh) should be stored as a single entry, not split into separate entries per submesh.

### Data Quality
* Every entry must have valid `boundsCenter` and `boundsSize` — these are used for placement, collision, and UI previews.
* `materialName` and `shaderName` should reflect the actual in-game material, not a fallback. The spawner uses these to find scene materials at runtime.
* Child meshes are for structurally separate geometry attached to the same parent object (e.g. table legs under a table top). Do not use child meshes for submesh splits of a single mesh — use `subMeshTriCounts` instead.

### Naming Guidance
| Pattern | Example | When to use |
|---|---|---|
| `object_color` | `mailbox_red`, `awning_green` | Color is the primary distinguishing trait |
| `object_material` | `fence_chain`, `fence_wood` | Material/texture is the distinguishing trait |
| `object_location` | `sign_motel`, `sign_highway` | Object is specific to a location or context |
| `object_size` | `planter_small`, `planter_large` | Same object in different scales |
| `object_NN` | `rock_01`, `rock_02` | Interchangeable variants with no meaningful visual difference |

## What **NOT** to Do
* Do not add S1API dependencies. Schedule1 namespaces are acceptable when needed.
* Write/CRUD API is gated behind `#if DEBUG`. Release builds are read-only.
* Do not leave unused fields or dead code after refactoring.
* Do not use reflection on IL2CPP types — use direct property/field access instead.
* Do not add Newtonsoft.Json or other third-party dependencies — the hand-rolled JSON parser is intentional.
