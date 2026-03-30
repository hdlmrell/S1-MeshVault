# MeshVault

A shared mesh spawning library for Schedule I mods. If you're installing this, a mod you downloaded depends on it.

> [!WARNING]
> **Do not remove MeshVault** while mods that depend on it are installed. Those mods will break without it.

## For Players

**You probably don't need to do anything.** If a mod you download depends on MeshVault, your mod manager (r2modman, Vortex, or Gale) will install it automatically as a dependency.

For manual installs, drop `MeshVault.Il2Cpp.dll` or `MeshVault.Mono.dll` into your `Plugins` folder depending on your game branch. This library ships both IL2CPP and Mono builds.

**Requirements:** MelonLoader v0.7.0+, Schedule I by TVGS

### What does MeshVault actually do?

MeshVault is a behind-the-scenes library that other mods use to place furniture, props, and decorations in the game world. It contains a database of pre-extracted game meshes (and growing) that mods can spawn with full geometry, materials, colliders, and child objects. You won't interact with it directly, but the mods you love depend on it to build their content.

## For Mod Developers

MeshVault gives you access to a growing library of pre-extracted game meshes. No asset bundles, no hunting for materials at runtime. Pick an ID, call Spawn, and you get a fully configured GameObject.

```csharp
var desk = MeshVaultAPI.Spawn("desk_counter_l", position, Quaternion.identity);
```

### What you can do

- **Spawn any game prop by ID** with full geometry, materials, colliders, and child objects
- **Swap materials per-slot** at spawn time with `materialOverrides`
- **Tint colors per-slot** at spawn time with `colorOverrides`
- **Customize material properties** in JSON: `colorTint`, `metallic`, `smoothness`, `emissiveColor`
- **Project decal textures** onto surfaces with `SpawnDecal()`
- **Register your own meshes** with `RegisterMeshes()` so other mods can use them too
- **Register custom decals** from raw bytes or embedded assembly resources

### Getting started

Add a reference to `MeshVault.Il2Cpp.dll` or `MeshVault.Mono.dll` in your project. Then call `MeshVaultAPI.Init()` to load the database and start spawning.

Full documentation with examples, API reference, and JSON format: **[hdlmrell.github.io/S1-MeshVault](https://hdlmrell.github.io/S1-MeshVault/)**

- [Quick Start](https://hdlmrell.github.io/S1-MeshVault/) - Installation, first spawn, common tasks
- [API Reference](https://hdlmrell.github.io/S1-MeshVault/api-reference) - Every public method with parameters and examples
- [JSON Schema](https://hdlmrell.github.io/S1-MeshVault/json-schema) - How to author mesh JSON for shipping your own geometry

## Security

Both release DLLs are scanned and attested through [MLVScan](https://mlvscan.com) on every release. MLVScan performs automated static analysis to verify that mod binaries are clean and free of malicious code.

Verify the latest scans yourself:
- [IL2CPP Attestation](https://mlvscan.com/attestations/att_z1JhRFwt4GyEmvJWmv9TENNE)
- [Mono Attestation](https://mlvscan.com/attestations/att__rRN4uPLpZw9CGFgWMOFXXY3)
- [GitHub Source](https://github.com/hdlmrell/S1-MeshVault)

## License

**Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0)**

You are free to:
- **Share** - copy and redistribute the material in any medium or format.
- **Adapt** - remix, transform, and build upon the material.

Under the following terms:
- **Attribution** - You must give appropriate credit to the original author (hdlmrell) and indicate if changes were made.
- **NonCommercial** - You may not use the material for commercial purposes.
- **ShareAlike** - If you remix, transform, or build upon the material, you must distribute your contributions under the same license.
