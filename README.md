# MeshVault
**IL2CPP:** [![MLVScan IL2CPP Attestation](https://api.mlvscan.com/public/attestations/att_q6ywm356Uyol8y5fXDHvYg-j/badge.svg?style=split-pill)](https://mlvscan.com/attestations/att_q6ywm356Uyol8y5fXDHvYg-j)
**Mono:** [![MLVScan Mono Attestation](https://api.mlvscan.com/public/attestations/att_IHEwtJnEYdRj0oQRcZYkQpBw/badge.svg?style=split-pill)](https://mlvscan.com/attestations/att_IHEwtJnEYdRj0oQRcZYkQpBw)

A shared mesh spawning library for Schedule I mods. Place any game prop with one line of code.

```csharp
var desk = MeshVaultAPI.Spawn("desk_counter_l", position, Quaternion.identity);
```

Both release DLLs are scanned and attested through [MLVScan](https://mlvscan.com) on every release. Click the badges above to verify.

---

## For Players

**You don't need to install MeshVault yourself.** If a mod you download depends on it, your mod manager (r2modman, Vortex, or Gale) will install it automatically.

For manual installs, drop `MeshVault.Il2Cpp.dll` or `MeshVault.Mono.dll` into your `Plugins` folder. This plugin ships both IL2CPP and Mono builds.

**Requirements:** [MelonLoader](https://melonwiki.xyz/) v0.7.0+, Schedule I by TVGS

---

## For Mod Developers

MeshVault gives you access to a growing library of pre-extracted game meshes. No asset bundles, no hunting for materials at runtime. Pick an ID, call Spawn, and you get a fully configured GameObject with geometry, materials, colliders, and child objects.

**What you can do:**
- Spawn any game prop by ID: `MeshVaultAPI.Spawn("dumpster", pos, rot)`
- Swap materials or tint colors per-slot at spawn time
- Project textures onto surfaces with `SpawnDecal()`
- Register your own custom meshes with `RegisterMeshes()` so other mods can use them too
- Customize materials in JSON with `colorTint`, `metallic`, `smoothness`, and `emissiveColor`

**Get started:** Full documentation with examples, API reference, and JSON format is at **[hdlmrell.github.io/S1-MeshVault](https://hdlmrell.github.io/S1-MeshVault/)**.

| Page | What you'll find |
|:-----|:-----------------|
| [Quick Start](https://hdlmrell.github.io/S1-MeshVault/) | Installation, first spawn, common tasks |
| [API Reference](https://hdlmrell.github.io/S1-MeshVault/api-reference) | Every public method with parameters and examples |
| [JSON Schema](https://hdlmrell.github.io/S1-MeshVault/json-schema) | How to author mesh JSON for shipping your own geometry |

---

## Contributing

### Build configurations

| Configuration | Runtime | Purpose |
|:--|:--|:--|
| `MonoDebug` | Mono (netstandard2.1) | Development. Includes extraction tools and write API |
| `Release` | IL2CPP (net6.0) | Distribution. Read-only, embedded database |
| `MonoRelease` | Mono (netstandard2.1) | Distribution. Read-only, embedded database |

### Adding meshes to the database

1. Build and deploy `MonoDebug`.
2. Launch the game. Press `F9` to open MeshPlacer.
3. Use the Hierarchy Picker to browse scene objects and extract meshes.
4. Extracted meshes are saved to `UserData/MeshVault/MeshDatabase.json`.

### Project setup

Copy `LocalPaths.targets.example` to `LocalPaths.targets` in the project directory and fill in your local paths. This file is gitignored. Each developer maintains their own.

### Releases

Releases are automated. Push your changes to `stable`, then create a GitHub release with a version tag (e.g. `v1.0.7`). The CI pipeline builds both IL2CPP and Mono DLLs, attaches them to the release, and publishes MLVScan attestations automatically.

---

## License

[CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/). You may share and adapt this work for non-commercial purposes with attribution and share-alike.
