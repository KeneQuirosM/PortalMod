using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Punto de entrada del mod. 7 Days to Die V3.0 descubre esta clase porque
    /// implementa IModApi y la instancia automaticamente al cargar el mod.
    /// </summary>
    public class API : IModApi
    {
        public static Mod ModInstance { get; private set; }
        internal const string HarmonyId = "com.keneqirosm.portalmod";

        public void InitMod(Mod _modInstance)
        {
            ModInstance = _modInstance;
            Log("Inicializando PortalMod...");

            // Harmony esta integrado de forma nativa desde A20 y se carga desde
            // 0Harmony.dll del propio juego, no hace falta empaquetarlo con el mod.
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log($"Harmony patches aplicados ({harmony.GetPatchedMethods().Count()} metodos parcheados).");

            // Inicializa los sistemas del mod. Ambos son singletons estaticos
            // para simplificar el acceso desde los patches de Harmony.
            PortalManager.Instance.Init();
            PortalTeleport.Init();

            // ModEvents: API de eventos nativa de 7 Days to Die (namespace raiz).
            // TODO: verificar en Assembly-CSharp V3.0 la firma exacta de los delegados
            // de ModEvents; la forma de suscripcion (RegisterHandler vs "+=") puede
            // variar levemente entre builds. Los nombres de evento usados abajo
            // (GameStartDone, GameShutdown, PlayerSpawnedInWorld, EntityKilled,
            // GameUpdate) son estables desde A20 pero deben confirmarse en V3.0.
            ModEvents.GameStartDone.RegisterHandler(OnGameStartDone);
            ModEvents.GameShutdown.RegisterHandler(OnGameShutdown);
            ModEvents.PlayerSpawnedInWorld.RegisterHandler(OnPlayerSpawnedInWorld);
            ModEvents.GameUpdate.RegisterHandler(OnGameUpdate);

            Log("PortalMod inicializado correctamente.");
        }

        // Cuando el mundo termina de cargar: intenta restaurar los portales
        // persistidos en disco (ver PortalManager.Load()).
        private void OnGameStartDone(ref ModEvents.SGameStartDoneData _data)
        {
            PortalManager.Instance.Load();
        }

        // Al apagar el servidor/salir del mundo: persiste el estado actual de portales.
        private void OnGameShutdown()
        {
            PortalManager.Instance.Save();
        }

        // Se usa unicamente para logging de depuracion; el registro real de
        // portales ocurre en PortalBlockPatch.cs cuando se coloca el bloque.
        private void OnPlayerSpawnedInWorld(ref ModEvents.SPlayerSpawnedInWorldData _data)
        {
            Log($"Jugador conectado. RespawnType={_data.RespawnType}");
        }

        // Tick global del servidor/cliente: usado por PortalTeleport para
        // revisar colisiones jugador<->portal en cada frame/tick logico.
        private void OnGameUpdate()
        {
            PortalTeleport.Tick();
        }

        internal static void Log(string message)
        {
            Debug.Log($"[PortalMod] {message}");
        }

        internal static void LogWarning(string message)
        {
            Debug.LogWarning($"[PortalMod] {message}");
        }

        internal static void LogError(string message)
        {
            Debug.LogError($"[PortalMod] {message}");
        }
    }
}
