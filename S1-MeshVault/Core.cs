using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(MeshVault.MeshVaultPlugin), "MeshVault", "1.0.1", "hdlmrell")]
[assembly: MelonColor(255, 100, 149, 237)]

namespace MeshVault
{
    /// <summary>
    /// MelonPlugin entry point for MeshVault. Loads before all mods via <see cref="MelonPlugin"/>.
    /// Init is lazy — runs on first API access when Unity is ready, not during OnPreInitialization.
    /// </summary>
    public class MeshVaultPlugin : MelonPlugin
    {
        /// <summary>
        /// Registers debug tools (MeshPlacer) and scene-load hooks. Release builds are a no-op.
        /// </summary>
        public override void OnPreInitialization()
        {
#if DEBUG
            Tools.MeshPlacer.Register();
            MelonEvents.OnSceneWasLoaded.Subscribe(OnSceneLoaded);
#endif
        }

#if DEBUG
        private static void OnSceneLoaded(int buildIndex, string sceneName)
        {
            if (!GameObject.Find("MV_MeshPlacer"))
            {
                var go = new GameObject("MV_MeshPlacer");
                go.AddComponent<Tools.MeshPlacer>();
                UnityEngine.Object.DontDestroyOnLoad(go);
            }
        }
#endif
    }
}
