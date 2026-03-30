---
title: Home
layout: default
nav_order: 1
---

# MeshVault

Place any game prop in your Schedule I mod with one line of code.

```csharp
var desk = MeshVaultAPI.Spawn("desk_counter_l", position, Quaternion.identity);
```

No mesh extraction. No asset bundles. No hunting for materials at runtime. MeshVault ships a database of pre-extracted game meshes and handles everything for you: geometry, materials, colliders, child objects. You just pick an ID and a position.

Want a gold-tinted version? Add a `colorTint` to your JSON. Want it to glow? Add `emissiveColor`. Want to ship your own custom meshes? Call `RegisterMeshes()` and every mod can spawn them.

---

## Getting Started

### 1. Install MeshVault in your project

Add a reference to `MeshVault.Il2Cpp.dll` (IL2CPP) or `MeshVault.Mono.dll` (Mono) in your `.csproj`:

```xml
<Reference Include="MeshVault">
  <HintPath>path\to\MeshVault.Il2Cpp.dll</HintPath>
</Reference>
```

You can find the DLL in your r2modman profile's `Mods` folder after installing any mod that depends on MeshVault.

### 2. Spawn your first mesh

```csharp
using MeshVault;

var go = MeshVaultAPI.Spawn("dumpster",
    new Vector3(10f, 0f, 5f),
    Quaternion.Euler(0f, 90f, 0f));
```

That's a dumpster in your world. MeshVault finds the right materials from the game scene automatically.

### 3. See what meshes are available

```csharp
string[] allIds = MeshVaultAPI.ListMeshes();
foreach (var id in allIds)
    MelonLogger.Msg(id);
```

This prints every mesh ID you can spawn. IDs look like `dumpster`, `sofa_double`, `desk_counter_l`, `tv_stand`, etc.

---

## Common Tasks

### Change the size

`Spawn()` always creates objects at scale `(1,1,1)`. Set `localScale` after spawning:

```csharp
var go = MeshVaultAPI.Spawn("dumpster", position, rotation);
go.transform.localScale = new Vector3(2f, 2f, 2f); // double size
```

### Change the color

Pass `colorOverrides` to tint individual material slots. Use `null` for slots you want to keep as-is:

```csharp
var go = MeshVaultAPI.Spawn("sofa_double", position, rotation,
    colorOverrides: new Color?[] { new Color(1f, 0f, 0f), null });
```

### Swap a material

Pass `materialOverrides` with the name of a material that exists in the game scene:

```csharp
var go = MeshVaultAPI.Spawn("sofa_double", position, rotation,
    materialOverrides: new[] { null, "leather_black" });
```

### Attach to a parent

```csharp
var go = MeshVaultAPI.Spawn("desk_lamp", position, rotation,
    parent: myBuildingTransform);
```

### Check if a mesh exists before spawning

```csharp
if (MeshVaultAPI.HasMesh("streetlight_01"))
{
    var go = MeshVaultAPI.Spawn("streetlight_01", position, rotation);
}
```

---

## What's Next

| Page | Description |
|:-----|:------------|
| [API Reference](api-reference) | Full method documentation for advanced use (decals, custom mesh registration, material helpers) |
| [JSON Schema](json-schema) | How to author mesh JSON for `RegisterMeshes()` if you want to ship your own meshes |

---

## Installation (for players)

Players don't need to install MeshVault manually. It's pulled in automatically as a dependency when you install a mod that uses it via **r2modman**, **Vortex**, or **Gale**.

For manual installs, drop `MeshVault.Il2Cpp.dll` or `MeshVault.Mono.dll` into your `Mods` folder.

**Requirements:** [MelonLoader](https://melonwiki.xyz/) v0.7.0+, Schedule I by TVGS

---

## License

[CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/). You may share and adapt this work for non-commercial purposes with attribution and share-alike.
