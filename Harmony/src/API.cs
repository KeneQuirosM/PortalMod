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

            // Diagnostico: Block_OnBlockActivated_Patch (PortalBlockPatch.cs) quedo
            // comentado despues de dos intentos fallidos de adivinar su firma real
            // (AmbiguousMatchException, luego "Undefined target method"). En vez de
            // una tercera adivinanza, se loguean por reflection TODAS las
            // sobrecargas reales de Block.OnBlockActivated ANTES de PatchAll, para
            // que quede en el log del juego incluso si algo mas fallara al parchear.
            LogOnBlockActivatedOverloads();

            // Harmony esta integrado de forma nativa desde A20 y se carga desde
            // 0Harmony.dll del propio juego, no hace falta empaquetarlo con el mod.
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log($"Harmony patches aplicados ({harmony.GetPatchedMethods().Count()} metodos parcheados).");

            // Dependencia OPCIONAL con 0-SCore: si esta cargado, sus propias
            // particulas para los modelos gupFuturePortalN.unity3d SI se
            // resuelven (no hay spam real que filtrar), asi que el filtro de
            // log de "Unknown particle effect" (LogFilterPatch.cs) NO debe
            // registrarse en ese caso — evita ocultar un error real y
            // distinto detras del mismo filtro.
            //
            // FIX real (el chequeo original "ModManager.ModLoaded('0-SCore')"
            // nunca detectaba SCore aunque estuviera instalado): confirmado
            // en el log real del juego que el mod de SCore carga con
            // Mod.Name EXACTO "0-SCore_sphereii" (no "0-SCore" a secas) — y
            // ModManager.ModLoaded(_modName) compara ese nombre por igualdad
            // exacta contra su diccionario interno (indexado por Mod.Name,
            // confirmado decompilando ModManager.loadModsFromFolder), asi que
            // "0-SCore" nunca hacia match. Se cambia a una busqueda flexible
            // por substring sobre ModManager.GetLoadedMods() (real, ya
            // confirmada) para tolerar variaciones de nombre entre forks/
            // versiones de SCore (ej. un futuro "0-SCore" sin sufijo, o
            // "0_SCore"), en vez de depender de un nombre exacto fragil.
            // Mod.Name es un string plano (propiedad real de la clase Mod,
            // confirmada por reflection) — NO existe una property anidada
            // "Mod.ModInfo" en la clase real (esa estructura, con un DataItem
            // "Name.Value", pertenece al parser V1 viejo de ModInfo.xml, no a
            // la clase Mod que expone ModManager en tiempo de ejecucion).
            var sCorePresent = ModManager.GetLoadedMods().Any(m => m.Name != null && m.Name.Contains("SCore"));
            Log("SCore detectado: " + sCorePresent);
            if (!sCorePresent)
            {
                Log_Error_ParticleFilter_Patch.Register(harmony);
            }

            // Inicializa los sistemas del mod. Ambos son singletons estaticos
            // para simplificar el acceso desde los patches de Harmony.
            PortalManager.Instance.Init();
            PortalTeleport.Init();

            // ModEvents: API de eventos nativa de 7 Days to Die (namespace raiz).
            // GameStartDone y PlayerSpawnedInWorld SI compilan contra el
            // Assembly-CSharp.dll real de V3.0 con la firma "ref SXxxData" usada
            // abajo. GameShutdown y GameUpdate NO — el compilador real reporto que
            // el delegado esperado por ModEvents.ModEventHandlerDelegate ya no
            // coincide con un método sin parametros (ver GameManager_Update_Patch
            // / GameManager_OnApplicationQuit_Patch mas abajo: en vez de adivinar
            // otra firma de ModEvents, esos dos casos se resolvieron con Harmony
            // patches sobre GameManager, un mecanismo que este mod ya usa en todos
            // lados y cuya superficie (Update/OnApplicationQuit de un
            // MonoBehaviour) es mucho mas estable entre versiones que la API
            // interna de ModEvents).
            ModEvents.GameStartDone.RegisterHandler(OnGameStartDone);
            ModEvents.PlayerSpawnedInWorld.RegisterHandler(OnPlayerSpawnedInWorld);

            Log("PortalMod inicializado correctamente.");
        }

        // Cuando el mundo termina de cargar: intenta restaurar los portales
        // persistidos en disco (ver PortalManager.Load()).
        private void OnGameStartDone(ref ModEvents.SGameStartDoneData _data)
        {
            PortalManager.Instance.Load();
        }

        // Se usa unicamente para logging de depuracion; el registro real de
        // portales ocurre en PortalBlockPatch.cs cuando se coloca el bloque.
        private void OnPlayerSpawnedInWorld(ref ModEvents.SPlayerSpawnedInWorldData _data)
        {
            Log($"Jugador conectado. RespawnType={_data.RespawnType}");
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

        /// <summary>
        /// Enumera por reflection TODAS las sobrecargas de "OnBlockActivated"
        /// declaradas en la clase "Block" (publicas y no publicas, de instancia)
        /// y las loguea una por una con el wrapper Log(string) de esta clase
        /// (evita referenciar la clase global "Log" de LogLibrary.dll sin
        /// calificar, que dentro de API queda tapada por este mismo metodo
        /// y produce CS0119), para leer las firmas reales directamente del
        /// log del juego en vez de seguir adivinando tipos.
        /// </summary>
        private static void LogOnBlockActivatedOverloads()
        {
            try
            {
                var overloads = typeof(Block).GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.Name == "OnBlockActivated")
                    .ToList();

                if (overloads.Count == 0)
                {
                    Log("OnBlockActivated overloads: ninguna encontrada en la clase Block (nombre de metodo distinto en V3.0?).");
                    return;
                }

                foreach (var method in overloads)
                {
                    var parameters = string.Join(", ", method.GetParameters()
                        .Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    Log($"OnBlockActivated overloads: {method.ReturnType.Name} OnBlockActivated({parameters})");
                }
            }
            catch (Exception e)
            {
                // Puramente diagnostico: nunca debe impedir que el resto del mod cargue.
                Log($"OnBlockActivated overloads: fallo al enumerar via reflection ({e.Message}).");
            }
        }
    }

    /// <summary>
    /// Reemplaza a "ModEvents.GameUpdate" (ver comentario en API.InitMod): tick
    /// global usado por PortalTeleport para revisar colisiones jugador&lt;-&gt;portal
    /// y por PortalVisualFX para el pulso de luz ambiental. GameManager.Update()
    /// es un metodo Update() de MonoBehaviour estandar de Unity — extremadamente
    /// estable entre versiones del juego en comparacion con la API interna de
    /// ModEvents — y ya es un patron usado por muchos mods de Harmony para 7D2D.
    /// Nota: esto corre una vez por FRAME renderizado (no por tick logico fijo),
    /// mas frecuente de lo que "GameUpdate" probablemente disparaba; es seguro
    /// porque tanto PortalTeleport.Tick() como PortalVisualFX.AmbientTick() ya
    /// se auto-throttlean con Time.time internamente.
    ///
    /// AUDITORIA (manejo de errores): este Postfix se ejecuta DENTRO de
    /// GameManager.Update(), el loop principal de Unity — corre para TODOS
    /// los jugadores, en cada frame, tanto en cliente como en servidor
    /// dedicado. Antes no tenia try/catch: una excepcion sin capturar en
    /// CUALQUIER parte de PortalTeleport.Tick() (o de lo que llama —
    /// PortalVisualFX.AmbientTick, PortalHoverFX.Tick, el chequeo de
    /// colision por jugador) se propagaria hacia arriba a traves de
    /// GameManager.Update() mismo, en vez de quedar contenida al mod. Se
    /// agrega un try/catch de ultima instancia aqui — PortalTeleport.Tick()
    /// y sus llamados internos YA tienen su propio try/catch mas granular
    /// (por jugador, por portal) para no perder trabajo de mas de lo
    /// necesario; este es solo la red de seguridad final para que, pase lo
    /// que pase, GameManager.Update() nunca vea una excepcion salir de este mod.
    /// </summary>
    [HarmonyPatch(typeof(GameManager), "Update")]
    internal static class GameManager_Update_Patch
    {
        private static void Postfix()
        {
            try
            {
                PortalTeleport.Tick();
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion sin capturar en PortalTeleport.Tick() (contenida aqui para no afectar GameManager.Update): {e}");
            }
        }
    }

    /// <summary>
    /// Reemplaza a "ModEvents.GameShutdown" (ver comentario en API.InitMod):
    /// persiste el estado de portales antes de que el juego termine de cerrar.
    /// OnApplicationQuit es el callback estandar de Unity que GameManager
    /// sobrescribe para su propia logica de guardado-y-salida, por lo que es un
    /// punto de enganche estable para ejecutar nuestro propio guardado justo antes.
    ///
    /// AUDITORIA (manejo de errores): Save() ya tiene su propio try/catch
    /// interno, pero se agrega uno aqui tambien — este Prefix corre ANTES de
    /// la logica de cierre real de GameManager.OnApplicationQuit; si algo
    /// fuera de Save() (por ejemplo la resolucion del singleton
    /// PortalManager.Instance) fallara, no queremos bloquear el cierre
    /// normal del juego/servidor.
    /// </summary>
    [HarmonyPatch(typeof(GameManager), "OnApplicationQuit")]
    internal static class GameManager_OnApplicationQuit_Patch
    {
        private static void Prefix()
        {
            try
            {
                PortalManager.Instance.Save();
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion sin capturar guardando portales al cerrar (contenida aqui para no bloquear el cierre del juego): {e}");
            }
        }
    }
}
