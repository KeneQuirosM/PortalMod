using System.Collections.Generic;

namespace PortalMod
{
    /// <summary>
    /// Mapeo estilo+bioma -> nombre de bloque (blocks.xml). Fuente unica de
    /// verdad, usada tanto por PortalVisualFX (para elegir que variante
    /// mostrar) como por PortalBlockPatch.IsPortalBlock (para reconocer
    /// TODAS las variantes como "es un portal") — separado en su propia
    /// clase para no repetir la lista en dos lugares y volver a caer en el
    /// bug ya encontrado esta sesion donde "portalBlockActivePulseHigh"
    /// faltaba en IsPortalBlock.
    ///
    /// ESTILOS (Feature "Opcion A: 6 items separados", cada uno con su
    /// propio modelo 3D): "legacy" (el "portalBlock"/"portalBlockActive*"
    /// original, sin cambios, conservado para no romper mundos ya
    /// guardados) + "platform"/"grid"/"claws"/"cylinder"/"wings"/"arch"
    /// (los 6 nuevos, ver blocks.xml). Cada estilo tiene su propio bloque
    /// inactivo y sus propias 6 variantes activas (default + 5 biomas),
    /// definidas en blocks.xml con Extends encadenado (el inactivo de cada
    /// estilo Extends="portalBlock", heredando Class="Powered"/MaxDamage/
    /// etc.; sus activos Extends del inactivo del mismo estilo).
    ///
    /// BIOMAS: nombres REALES confirmados contra Data/Config/biomes.xml
    /// vanilla instalado: "snow", "wasteland", "burnt_forest", "pine_forest",
    /// "desert" (existe ademas "underwater", sin mapeo especifico — usa
    /// "default", clave "" aqui). NO existe un bioma "ciudad"/"city" en
    /// V3.0.
    /// </summary>
    internal static class PortalBiomes
    {
        internal const string DefaultStyle = "legacy";

        /// <summary>Clave interna usada en los diccionarios de abajo para "sin bioma especifico / default".</summary>
        private const string DefaultBiomeKey = "";

        // estilo -> nombre de bloque inactivo.
        private static readonly Dictionary<string, string> StyleInactiveBlockNames = new Dictionary<string, string>
        {
            { "legacy", "portalBlock" },
            { "platform", "portalBlock_platform" },
            { "grid", "portalBlock_grid" },
            { "claws", "portalBlock_claws" },
            { "cylinder", "portalBlock_cylinder" },
            { "wings", "portalBlock_wings" },
            { "arch", "portalBlock_arch" },
        };

        // estilo -> (bioma real, o "" para default) -> nombre de bloque activo.
        // Los nombres de "legacy" siguen la convencion vieja (sin guion bajo,
        // ej. "portalBlockActiveSnow"); los 6 estilos nuevos usan guion bajo
        // (ej. "portalBlock_platform_active_snow") — ambos confirmados
        // exactamente contra blocks.xml.
        private static readonly Dictionary<string, Dictionary<string, string>> StyleBiomeActiveBlockNames =
            new Dictionary<string, Dictionary<string, string>>
            {
                ["legacy"] = new Dictionary<string, string>
                {
                    [DefaultBiomeKey] = "portalBlockActive",
                    ["snow"] = "portalBlockActiveSnow",
                    ["wasteland"] = "portalBlockActiveWasteland",
                    ["burnt_forest"] = "portalBlockActiveBurntForest",
                    ["pine_forest"] = "portalBlockActivePineForest",
                    ["desert"] = "portalBlockActiveDesert",
                },
                ["platform"] = new Dictionary<string, string>
                {
                    [DefaultBiomeKey] = "portalBlock_platform_active",
                    ["snow"] = "portalBlock_platform_active_snow",
                    ["wasteland"] = "portalBlock_platform_active_wasteland",
                    ["burnt_forest"] = "portalBlock_platform_active_burntForest",
                    ["pine_forest"] = "portalBlock_platform_active_pineForest",
                    ["desert"] = "portalBlock_platform_active_desert",
                },
                ["grid"] = new Dictionary<string, string>
                {
                    [DefaultBiomeKey] = "portalBlock_grid_active",
                    ["snow"] = "portalBlock_grid_active_snow",
                    ["wasteland"] = "portalBlock_grid_active_wasteland",
                    ["burnt_forest"] = "portalBlock_grid_active_burntForest",
                    ["pine_forest"] = "portalBlock_grid_active_pineForest",
                    ["desert"] = "portalBlock_grid_active_desert",
                },
                ["claws"] = new Dictionary<string, string>
                {
                    [DefaultBiomeKey] = "portalBlock_claws_active",
                    ["snow"] = "portalBlock_claws_active_snow",
                    ["wasteland"] = "portalBlock_claws_active_wasteland",
                    ["burnt_forest"] = "portalBlock_claws_active_burntForest",
                    ["pine_forest"] = "portalBlock_claws_active_pineForest",
                    ["desert"] = "portalBlock_claws_active_desert",
                },
                ["cylinder"] = new Dictionary<string, string>
                {
                    [DefaultBiomeKey] = "portalBlock_cylinder_active",
                    ["snow"] = "portalBlock_cylinder_active_snow",
                    ["wasteland"] = "portalBlock_cylinder_active_wasteland",
                    ["burnt_forest"] = "portalBlock_cylinder_active_burntForest",
                    ["pine_forest"] = "portalBlock_cylinder_active_pineForest",
                    ["desert"] = "portalBlock_cylinder_active_desert",
                },
                ["wings"] = new Dictionary<string, string>
                {
                    [DefaultBiomeKey] = "portalBlock_wings_active",
                    ["snow"] = "portalBlock_wings_active_snow",
                    ["wasteland"] = "portalBlock_wings_active_wasteland",
                    ["burnt_forest"] = "portalBlock_wings_active_burntForest",
                    ["pine_forest"] = "portalBlock_wings_active_pineForest",
                    ["desert"] = "portalBlock_wings_active_desert",
                },
                ["arch"] = new Dictionary<string, string>
                {
                    [DefaultBiomeKey] = "portalBlock_arch_active",
                    ["snow"] = "portalBlock_arch_active_snow",
                    ["wasteland"] = "portalBlock_arch_active_wasteland",
                    ["burnt_forest"] = "portalBlock_arch_active_burntForest",
                    ["pine_forest"] = "portalBlock_arch_active_pineForest",
                    ["desert"] = "portalBlock_arch_active_desert",
                },
            };

        // Variantes "PulseHigh" de la familia "legacy": ya no se seleccionan
        // activamente (ver FIX real en PortalVisualFX.cs — el pulso de luz
        // se descontinuo al agregar cableado electrico real), pero siguen
        // definidas en blocks.xml y deben seguir siendo reconocidas como
        // "es un portal" por compatibilidad con bloques ya colocados antes
        // de ese cambio.
        private static readonly string[] LegacyPulseHighBlockNames =
        {
            "portalBlockActivePulseHigh",
            "portalBlockActivePulseHighSnow",
            "portalBlockActivePulseHighWasteland",
            "portalBlockActivePulseHighBurntForest",
            "portalBlockActivePulseHighPineForest",
            "portalBlockActivePulseHighDesert",
        };

        /// <summary>Nombre de bloque inactivo (blocks.xml) para un estilo. Si el estilo no se reconoce, usa DefaultStyle.</summary>
        internal static string GetInactiveBlockName(string style)
        {
            if (!string.IsNullOrEmpty(style) && StyleInactiveBlockNames.TryGetValue(style, out var name))
            {
                return name;
            }

            return StyleInactiveBlockNames[DefaultStyle];
        }

        /// <summary>Nombre de bloque XML (blocks.xml) para el estado vinculado, segun estilo y bioma. Si el estilo o el bioma no se reconocen, cae al default de cada uno.</summary>
        internal static string GetActiveBlockName(string style, string biome)
        {
            var styleKey = !string.IsNullOrEmpty(style) && StyleBiomeActiveBlockNames.ContainsKey(style)
                ? style
                : DefaultStyle;

            var biomeMap = StyleBiomeActiveBlockNames[styleKey];
            var biomeKey = !string.IsNullOrEmpty(biome) && biomeMap.ContainsKey(biome)
                ? biome
                : DefaultBiomeKey;

            return biomeMap[biomeKey];
        }

        /// <summary>Resuelve el estilo a partir del nombre de bloque INACTIVO real encontrado en el mundo (ver PortalManager.ResolveStyleName), o null si no coincide con ningun estilo conocido.</summary>
        internal static string GetStyleFromInactiveBlockName(string blockName)
        {
            foreach (var pair in StyleInactiveBlockNames)
            {
                if (pair.Value == blockName)
                {
                    return pair.Key;
                }
            }

            return null;
        }

        /// <summary>Todos los nombres de bloque validos (todos los estilos, inactivos + activos, incluidas las variantes de pulso ya sin uso activo), usado por PortalBlockPatch.IsPortalBlock.</summary>
        internal static IEnumerable<string> GetAllBlockNames()
        {
            foreach (var name in StyleInactiveBlockNames.Values)
            {
                yield return name;
            }

            foreach (var biomeMap in StyleBiomeActiveBlockNames.Values)
            {
                foreach (var name in biomeMap.Values)
                {
                    yield return name;
                }
            }

            foreach (var name in LegacyPulseHighBlockNames)
            {
                yield return name;
            }
        }
    }
}
