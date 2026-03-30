---
title: JSON Schema
layout: default
nav_order: 3
---

# JSON Schema
{: .no_toc }

How to write mesh JSON for `RegisterMeshes()`. If you're only spawning meshes from the built-in database, you don't need this page.

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## When Do You Need This?

You need mesh JSON when you want to **ship your own geometry** with your mod. The typical workflow:

1. Extract meshes in-game using MeshPlacer (MeshVault's debug tool, `F9` in a MonoDebug build)
2. Extractions are saved to `UserData/MeshVault/MeshDatabase.json`
3. Copy your entries out of that file into a separate JSON file for your mod
4. Embed that file in your mod as a resource
5. Call `RegisterMeshes()` at startup

If you're placing built-in meshes, just use `Spawn()` and skip this page entirely.

---

## Basic Structure

The JSON is an object where each key is a mesh ID and each value is the mesh data:

```json
{
  "mymod_table": {
    "vertices": [[0, 0, 0], [1, 0, 0], [0, 1, 0]],
    "normals": [[0, 0, 1], [0, 0, 1], [0, 0, 1]],
    "uvs": [[0, 0], [1, 0], [0, 1]],
    "triangles": [0, 1, 2],
    "materialName": "wood_dark",
    "shaderName": "Universal Render Pipeline/Lit",
    "color": [1, 1, 1, 1],
    "boundsCenter": [0.33, 0.33, 0],
    "boundsSize": [1, 1, 0]
  }
}
```

If you're using `RegisterMeshes()`, every ID must start with your prefix (e.g. `"mymod_"`).

---

## Required Fields

These fields must be present on every entry:

| Field | Type | What it is |
|:------|:-----|:-----------|
| `vertices` | `float[N][3]` | Vertex positions `[x, y, z]` |
| `normals` | `float[N][3]` | Vertex normals `[x, y, z]` (same length as vertices) |
| `uvs` | `float[N][2]` | Texture coordinates `[u, v]` (same length as vertices) |
| `triangles` | `int[]` | Triangle indices into the vertex array |
| `materialName` | `string` | Name of a game material to use (e.g. `"wood_dark"`, `"metal_lightgrey_mat"`) |
| `shaderName` | `string` | Fallback shader if the material can't be found (usually `"Universal Render Pipeline/Lit"`) |
| `color` | `float[4]` | RGBA color for fallback material. Set to `[1, 1, 1, 1]` if unsure |
| `boundsCenter` | `float[3]` | Center of the bounding box `[x, y, z]` |
| `boundsSize` | `float[3]` | Size of the bounding box `[x, y, z]` |

{: .note }
> You don't write vertex data by hand. When you extract meshes in-game with MeshPlacer, they're saved to `UserData/MeshVault/MeshDatabase.json`. Copy your entries out of that file into a separate JSON file to ship with your mod.

---

## Material Property Overrides

These optional fields let you customize how the material looks without creating multiple entries for the same geometry. They clone the scene material and modify the clone, so the original is never changed.

### colorTint

RGBA array. Multiplied with the material's existing color. Use this to tint things (e.g. make grey metal look gold).

```json
"colorTint": [0.83, 0.68, 0.21, 1.0]
```

{: .note }
> `colorTint` is different from `color`. The `color` field is the original extracted color used for fallback materials. `colorTint` is applied on top of the actual scene material. If you want to tint something, use `colorTint`.

### metallic

Float, `0.0` to `1.0`. How metallic the surface looks.

```json
"metallic": 0.8
```

### smoothness

Float, `0.0` to `1.0`. How shiny/reflective the surface is.

```json
"smoothness": 0.6
```

### emissiveColor

RGB array. Makes the mesh glow. MeshVault automatically enables emission on the material.

```json
"emissiveColor": [1.0, 0.5, 0.0]
```

### emissiveIntensity

Float multiplier for `emissiveColor`. Defaults to `1.0`. Higher values glow brighter.

```json
"emissiveIntensity": 2.5
```

---

## Multi-Submesh Support

Some game objects use multiple materials on a single mesh (e.g. a desk with wood and metal parts). These fields handle that. If your mesh only has one material, skip this section.

| Field | Type | What it is |
|:------|:-----|:-----------|
| `subMeshTriCounts` | `int[]` | Number of triangles in each submesh (the `triangles` array is split by these counts) |
| `subMeshMaterialNames` | `string[]` | Material name for each submesh |
| `subMeshShaderNames` | `string[]` | Fallback shader for each submesh |

```json
"subMeshTriCounts": [102, 30],
"subMeshMaterialNames": ["wood_dark", "metal_handle"],
"subMeshShaderNames": ["Universal Render Pipeline/Lit", "Universal Render Pipeline/Lit"]
```

---

## Child Meshes

Some objects have structurally separate parts (e.g. table legs as a separate mesh attached to the table top). Each child has its own geometry and material.

```json
"childMeshes": [
  {
    "vertices": [...],
    "normals": [...],
    "uvs": [...],
    "triangles": [...],
    "materialName": "metal_dark",
    "shaderName": "Universal Render Pipeline/Lit",
    "color": [0.2, 0.2, 0.2, 1.0],
    "colorTint": [1.0, 0.8, 0.0, 1.0],
    "metallic": 0.9
  }
]
```

Child meshes support all the same material property overrides (`colorTint`, `metallic`, `smoothness`, `emissiveColor`, `emissiveIntensity`), applied independently from the parent.

---

## Full Example

A mesh entry using material overrides, submeshes, and a child mesh:

```json
{
  "mymod_fancy_desk": {
    "vertices": [...],
    "normals": [...],
    "uvs": [...],
    "triangles": [...],
    "materialName": "metal_lightgrey_mat",
    "shaderName": "Universal Render Pipeline/Lit",
    "color": [1, 1, 1, 1],
    "boundsCenter": [0, 0, 0],
    "boundsSize": [1.2, 0.8, 0.6],
    "subMeshTriCounts": [102, 30],
    "subMeshMaterialNames": ["metal_lightgrey_mat", "wood_dark"],
    "subMeshShaderNames": ["Universal Render Pipeline/Lit", "Universal Render Pipeline/Lit"],
    "colorTint": [0.83, 0.68, 0.21, 1.0],
    "metallic": 0.8,
    "smoothness": 0.6,
    "childMeshes": [
      {
        "vertices": [...],
        "normals": [...],
        "uvs": [...],
        "triangles": [...],
        "materialName": "metal_dark",
        "shaderName": "Universal Render Pipeline/Lit",
        "color": [0.2, 0.2, 0.2, 1.0]
      }
    ]
  }
}
```

Register it in your mod:

```csharp
string json = LoadEmbeddedResource("meshes.json");
int count = MeshVaultAPI.RegisterMeshes("mymod", "My Mod", json);
// MeshVaultAPI.Spawn("mymod_fancy_desk", ...) now works
```

---

## Advanced Fields

These fields are used by MeshVault's extraction tools for baked texture support. You'll see them in the built-in database but you probably won't need to set them yourself.

| Field | Type | What it is |
|:------|:-----|:-----------|
| `textureName` | `string` | Baked texture asset name |
| `bakedColorTint` | `float[4]` | RGBA tint for baked texture path |
| `subMeshTextureNames` | `string[]` | Per-submesh baked texture names |
| `subMeshColorTints` | `float[N][4]` | Per-submesh RGBA tints for baked textures |
