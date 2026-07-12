using System.Collections.Generic;

namespace PortalMod
{
    /// <summary>
    /// Mapeo bioma -> variante de bloque (Feature: color y modelo de portal
    /// segun el bioma donde se coloca). Fuente unica de verdad para los
    /// nombres de bloque de blocks.xml, usada tanto por PortalVisualFX (para
    /// elegir que variante mostrar) como por PortalBlockPatch.IsPortalBlock
    /// (para reconocer TODAS las variantes como "es un portal") — separado
    /// en su propia clase para no repetir la lista en dos lugares y volver a
    /// caer en el bug ya encontrado esta sesion donde
    /// "portalBlockActivePulseHigh" faltaba en IsPortalBlock.
    ///
    /// Nombres de bioma REALES confirmados contra Data/Config/biomes.xml
    /// vanilla instalado: "snow", "wasteland", "burnt_forest", "pine_forest",
    /// "desert" (existen ademas "underwater" y algunos ids de biomemap sin
    /// definicion de bioma propia, como "radiated"/"water"/"caveFloor"/
    /// "caveCeiling", sin mapeo especifico aqui). NO existe un bioma
    /// "ciudad"/"city" en V3.0:
    /// las ciudades son prefabs/POIs colocados SOBRE un bioma normal
    /// (tipicamente wasteland/pine_forest), no un bioma propio — el
    /// "default/ciudad" del pedido original se interpreta como el fallback
    /// para cualquier bioma sin mapeo especifico (incluye "underwater" y
    /// cualquier caso donde el bioma no se pudo resolver), y reutiliza
    /// portalBlockActive/portalBlockActivePulseHigh (gupFuturePortal6,
    /// morado) sin cambios.
    /// </summary>
    internal static class PortalBiomes
    {
        internal const string InactiveBlockName = "portalBlock";
        internal const string DefaultActiveBlockName = "portalBlockActive";
        internal const string DefaultActivePulseHighBlockName = "portalBlockActivePulseHigh";

        private static readonly Dictionary<string, (string Active, string PulseHigh)> BiomeBlockNames =
            new Dictionary<string, (string Active, string PulseHigh)>
            {
                { "snow", ("portalBlockActiveSnow", "portalBlockActivePulseHighSnow") },
                { "wasteland", ("portalBlockActiveWasteland", "portalBlockActivePulseHighWasteland") },
                { "burnt_forest", ("portalBlockActiveBurntForest", "portalBlockActivePulseHighBurntForest") },
                { "pine_forest", ("portalBlockActivePineForest", "portalBlockActivePulseHighPineForest") },
                { "desert", ("portalBlockActiveDesert", "portalBlockActivePulseHighDesert") },
            };

        /// <summary>Nombre de bloque XML (blocks.xml) para el estado activo (vinculado + con energia) segun bioma.</summary>
        internal static string GetActiveBlockName(string biome, bool pulseHigh)
        {
            if (!string.IsNullOrEmpty(biome) && BiomeBlockNames.TryGetValue(biome, out var names))
            {
                return pulseHigh ? names.PulseHigh : names.Active;
            }

            return pulseHigh ? DefaultActivePulseHighBlockName : DefaultActiveBlockName;
        }

        /// <summary>Todos los nombres de bloque validos (base + todas las variantes por bioma), usado por PortalBlockPatch.IsPortalBlock.</summary>
        internal static IEnumerable<string> GetAllBlockNames()
        {
            yield return InactiveBlockName;
            yield return DefaultActiveBlockName;
            yield return DefaultActivePulseHighBlockName;

            foreach (var names in BiomeBlockNames.Values)
            {
                yield return names.Active;
                yield return names.PulseHigh;
            }
        }
    }
}
