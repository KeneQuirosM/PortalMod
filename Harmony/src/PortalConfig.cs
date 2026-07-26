using System;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace PortalMod
{
    /// <summary>
    /// Configuracion ajustable por el operador del servidor, leida de
    /// "Config/PortalModConfig.xml" dentro de la carpeta del mod (mismo lugar
    /// que blocks.xml/items.xml/etc., pero este archivo NO lo interpreta el
    /// juego — es propio de PortalMod, parseado a mano con XDocument, igual
    /// que PortalManager hace persistencia propia en texto plano por no
    /// existir un hook oficial de config/guardado custom por mod en V3.0).
    ///
    /// FIX real (Bug reportado — "el cooldown de 5s fijo deja al jugador
    /// atascado/devuelto si el mundo tarda mas en cargar"): antes
    /// PortalManager.CooldownSeconds era un "const float" hardcodeado a 5 —
    /// ni compilaba a un valor distinto sin recompilar el DLL, mucho menos
    /// era ajustable por servidor. Se agrega este loader, invocado una vez
    /// desde API.InitMod ANTES de que cualquier sistema lea el valor, con
    /// fallback seguro al mismo default de siempre (5s) si el archivo falta,
    /// esta corrupto, o tiene un valor fuera de rango — el mod nunca deja de
    /// cargar por un config invalido.
    /// </summary>
    internal static class PortalConfig
    {
        private const string ConfigFileName = "PortalModConfig.xml";

        private const float DefaultCooldownSeconds = 5f;
        private const float MinCooldownSeconds = 0f;
        private const float MaxCooldownSeconds = 30f;

        // Feature "esperar chunk destino": ver FIX real en PortalTeleport.cs
        // (TryTeleport/ProcessPendingTeleports) sobre por que un chunk
        // destino sin cargar puede dejar al jugador incrustado. 0 desactiva
        // la espera por completo (siempre instantaneo, comportamiento
        // identico al de antes de este fix).
        private const float DefaultMaxChunkWaitSeconds = 2f;
        private const float MinMaxChunkWaitSeconds = 0f;
        private const float MaxMaxChunkWaitSeconds = 10f;

        public static float TeleportCooldownSeconds { get; private set; } = DefaultCooldownSeconds;
        public static float MaxChunkWaitSeconds { get; private set; } = DefaultMaxChunkWaitSeconds;

        /// <summary>Lee (o re-lee) la configuracion desde disco. Llamado una vez desde API.InitMod.</summary>
        public static void Load()
        {
            var path = GetConfigFilePath();
            if (path == null || !File.Exists(path))
            {
                API.Log($"[PortalMod] No se encontro Config/{ConfigFileName}; usando valores por defecto (cooldown={DefaultCooldownSeconds}s, esperaChunk={DefaultMaxChunkWaitSeconds}s).");
                TeleportCooldownSeconds = DefaultCooldownSeconds;
                MaxChunkWaitSeconds = DefaultMaxChunkWaitSeconds;
                return;
            }

            try
            {
                var root = XDocument.Load(path).Root;

                TeleportCooldownSeconds = ReadFloatValue(root, "TeleportCooldownSeconds", DefaultCooldownSeconds, MinCooldownSeconds, MaxCooldownSeconds);
                MaxChunkWaitSeconds = ReadFloatValue(root, "MaxChunkWaitSeconds", DefaultMaxChunkWaitSeconds, MinMaxChunkWaitSeconds, MaxMaxChunkWaitSeconds);

                API.Log($"[PortalMod] Configuracion cargada de Config/{ConfigFileName}: TeleportCooldownSeconds={TeleportCooldownSeconds}s, MaxChunkWaitSeconds={MaxChunkWaitSeconds}s.");
            }
            catch (Exception e)
            {
                API.LogWarning($"[PortalMod] Fallo leyendo Config/{ConfigFileName} ({e.Message}); usando valores por defecto (cooldown={DefaultCooldownSeconds}s, esperaChunk={DefaultMaxChunkWaitSeconds}s).");
                TeleportCooldownSeconds = DefaultCooldownSeconds;
                MaxChunkWaitSeconds = DefaultMaxChunkWaitSeconds;
            }
        }

        private static float ReadFloatValue(XElement root, string elementName, float defaultValue, float min, float max)
        {
            var raw = root?.Element(elementName)?.Attribute("value")?.Value;

            if (string.IsNullOrEmpty(raw))
            {
                // Elemento ausente: no es un error, muchos servidores solo
                // querran tocar UNO de los valores y dejar el resto en su
                // default — cada elemento se resuelve de forma independiente.
                return defaultValue;
            }

            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                API.LogWarning($"[PortalMod] Config/{ConfigFileName}: '{elementName}' value=\"{raw}\" no es un numero valido; usando default ({defaultValue}).");
                return defaultValue;
            }

            if (parsed < min || parsed > max)
            {
                API.LogWarning($"[PortalMod] Config/{ConfigFileName}: '{elementName}'={parsed} fuera de rango permitido [{min}, {max}]; usando default ({defaultValue}).");
                return defaultValue;
            }

            return parsed;
        }

        private static string GetConfigFilePath()
        {
            var modPath = API.ModInstance != null ? API.ModInstance.Path : null;
            return modPath != null ? Path.Combine(modPath, "Config", ConfigFileName) : null;
        }
    }
}
