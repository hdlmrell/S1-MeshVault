---
title: API Reference
layout: default
nav_order: 2
---

# API Reference
{: .no_toc }

Everything you can do with `MeshVaultAPI`. Add `using MeshVault;` to your mod.

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Spawning Meshes

### Spawn

```csharp
public static GameObject Spawn(
    string id,
    Vector3 position,
    Quaternion rotation,
    Transform parent = null,
    string namePrefix = "MeshVault",
    string[] materialOverrides = null,
    Color?[] colorOverrides = null
)
```

Creates a GameObject from a mesh database entry. The object comes with a `MeshFilter`, `MeshRenderer`, `MeshCollider`, and any child meshes already attached.

Returns `null` if the mesh ID doesn't exist. Always check with `HasMesh()` first if you're not sure.

**Parameters:**

| Parameter | Type | Default | What it does |
|:----------|:-----|:--------|:-------------|
| `id` | `string` | | The mesh ID (e.g. `"dumpster"`, `"sofa_double"`) |
| `position` | `Vector3` | | Where to place it in the world |
| `rotation` | `Quaternion` | | Which way it faces |
| `parent` | `Transform` | `null` | Attach to a parent object (e.g. a building) |
| `namePrefix` | `string` | `"MeshVault"` | Prefix for the GameObject's name in the hierarchy |
| `materialOverrides` | `string[]` | `null` | Swap out materials by slot (see below) |
| `colorOverrides` | `Color?[]` | `null` | Tint materials by slot (see below) |

#### How material/color overrides work

Both arrays use the same slot ordering: submeshes first, then child meshes. For a mesh with 2 submeshes and 1 child mesh, slot 0 is submesh 0, slot 1 is submesh 1, slot 2 is the child mesh. Use `null` in any slot to keep the default.

```csharp
// Mesh has 2 submeshes. Override slot 1 material, tint slot 0 red.
var go = MeshVaultAPI.Spawn("sofa_double", pos, rot,
    materialOverrides: new[] { null, "leather_black" },
    colorOverrides: new Color?[] { new Color(1f, 0f, 0f), null });
```

---

### SpawnDecal

```csharp
public static GameObject SpawnDecal(
    string textureName,
    Vector3 position,
    Quaternion rotation,
    Color? tintColor = null,
    Vector3? scale = null,
    Transform parent = null
)
```

Projects a texture onto nearby surfaces using a `DecalProjector`. The projector's forward direction (+Z) should point into the surface.

Looks up the texture in the decal registry first (see `RegisterDecals`), then searches game assets as a fallback. Returns `null` if the texture or decal shader can't be found.

```csharp
var decal = MeshVaultAPI.SpawnDecal("Graffiti_01",
    wallPosition,
    Quaternion.LookRotation(wallNormal),
    tintColor: Color.red,
    scale: new Vector3(2f, 2f, 1f));
```

| Parameter | Type | Default | What it does |
|:----------|:-----|:--------|:-------------|
| `textureName` | `string` | | Texture name or registered decal ID |
| `position` | `Vector3` | | Where to place the projector |
| `rotation` | `Quaternion` | | Direction the projector faces (forward = into the surface) |
| `tintColor` | `Color?` | white | Color multiplied onto the texture |
| `scale` | `Vector3?` | `(1,1,1)` | Width and height of the projection (Z is ignored) |
| `parent` | `Transform` | `null` | Parent object to attach to |

---

## Looking Up Meshes

### HasMesh

```csharp
public static bool HasMesh(string id)
```

Returns `true` if the ID exists in the database. Use this before `Spawn()` if you're not sure the mesh is available.

### GetMesh

```csharp
public static MeshEntry GetMesh(string id)
```

Returns the raw `MeshEntry` data for an ID, or `null` if not found. You can inspect bounds, material names, child meshes, etc. before spawning.

### ListMeshes

```csharp
public static string[] ListMeshes()
```

Returns every mesh ID in the database, including entries registered by other mods.

---

## Registering Your Own Meshes

If the built-in database doesn't have what you need, you can ship your own mesh data as JSON and register it at runtime. See [JSON Schema](json-schema) for the JSON format.

### RegisterMeshes

```csharp
public static int RegisterMeshes(
    string prefix,
    string modName,
    string json
)
```

Parses your JSON and adds the entries to the shared database. Once registered, any mod can spawn them with `Spawn()`.

Returns the number of entries registered, or `-1` if something went wrong (check the MelonLoader log for details).

**Prefix rules:**
- Pick a short prefix for your mod (e.g. `"otc"`, `"mymod"`). Lowercase letters and numbers only, no underscores.
- Every mesh ID in your JSON must start with `prefix_` (e.g. `"otc_custom_desk"`)
- First mod to claim a prefix owns it for the session. No other mod can use it.

```csharp
// Load your mesh JSON (embedded resource, file, string literal, etc.)
string json = LoadMyMeshJson();

int count = MeshVaultAPI.RegisterMeshes("mymod", "My Mod Name", json);
if (count > 0)
{
    // Your meshes are now available to everyone
    var go = MeshVaultAPI.Spawn("mymod_custom_desk", position, rotation);
}
```

### IsPrefixRegistered

```csharp
public static bool IsPrefixRegistered(string prefix)
```

Check if a prefix is already claimed before trying to register.

---

## Registering Decals

Custom textures for use with `SpawnDecal()`. Same prefix system as meshes.

### RegisterDecals (from byte data)

```csharp
public static int RegisterDecals(
    string prefix,
    string modName,
    Dictionary<string, byte[]> textures
)
```

Pass a dictionary of texture names to raw PNG/JPG bytes. Keys that don't already start with `prefix_` are auto-prefixed (e.g. key `"logo"` with prefix `"mymod"` becomes `"mymod_logo"`).

### RegisterDecals (from assembly resources)

```csharp
public static int RegisterDecals(
    string prefix,
    string modName,
    Assembly assembly,
    string resourcePrefix = null
)
```

Automatically finds and registers all embedded PNG/JPG resources in your assembly. Texture names are extracted from the resource path (e.g. `MyMod.Decals.logo.png` becomes `"logo"`, stored as `"mymod_logo"`).

```csharp
// Register all PNGs in MyMod.Decals namespace
int count = MeshVaultAPI.RegisterDecals("mymod", "My Mod",
    Assembly.GetExecutingAssembly(), "MyMod.Decals.");
```

### ListRegisteredDecals / GetRegisteredDecal / IsDecalPrefixRegistered

```csharp
public static string[] ListRegisteredDecals()
public static Texture2D GetRegisteredDecal(string id)
public static bool IsDecalPrefixRegistered(string prefix)
```

Query the decal registry. Same pattern as the mesh query methods.

---

## Advanced

These methods exist for specific use cases. Most mods won't need them.

### GetAllEntries

```csharp
public static Dictionary<string, MeshEntry> GetAllEntries()
```

Returns the full internal dictionary. Useful if you need to iterate over all entries or build tooling.

### BuildMaterialCatalog

```csharp
public static Dictionary<string, float[]> BuildMaterialCatalog()
```

Scans the database and returns a map of every unique material name to its RGBA color. Used internally by MeshPlacer's material picker UI.

### FindSceneMaterial / FindSceneTexture

```csharp
public static Material FindSceneMaterial(string matName)
public static Texture FindSceneTexture(string texName)
```

Look up a material or texture by name from the loaded game scene. Results are cached. You'd use these if you're building something custom outside of `Spawn()`.

### Init

```csharp
public static void Init()
```

Forces the database to load. You don't need to call this. All query and spawn methods trigger loading automatically on first use.
