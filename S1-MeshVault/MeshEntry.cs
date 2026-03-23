using UnityEngine;

namespace MeshVault
{
    /// <summary>
    /// Data for a non-combined child mesh (legs, frames, etc.) attached to a parent MeshEntry.
    /// </summary>
    public class ChildMeshEntry
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] UVs;
        public int[] Triangles;
        public string MaterialName;
        public string ShaderName;
        public float[] Color; // RGBA

        // Material property overrides (null = no override)
        public float[] ColorTint;
        public float? Metallic;
        public float? Smoothness;
        public float[] EmissiveColor;
        public float? EmissiveIntensity;
    }

    /// <summary>
    /// Data for a single mesh entry in the MeshVault database, including geometry,
    /// material references, and optional child meshes.
    /// </summary>
    public class MeshEntry
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] UVs;
        public int[] Triangles;
        public string MaterialName;
        public string ShaderName;
        public float[] Color; // RGBA
        public float[] BoundsCenter;
        public float[] BoundsSize;

        // Multi-submesh
        public int[] SubMeshTriCounts;
        public string[] SubMeshMaterialNames;
        public string[] SubMeshShaderNames;

        // Baked UV support
        public string TextureName;
        public float[] BakedColorTint;

        // Per-submesh baked material data
        public string[] SubMeshTextureNames;
        public float[][] SubMeshColorTints;

        // Material property overrides (null = no override, apply to all submeshes)
        public float[] ColorTint;
        public float? Metallic;
        public float? Smoothness;
        public float[] EmissiveColor;
        public float? EmissiveIntensity;

        // Child meshes
        public ChildMeshEntry[] ChildMeshes;
    }
}
