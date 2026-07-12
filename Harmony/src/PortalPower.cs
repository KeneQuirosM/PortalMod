using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Requisito de energia electrica para que un portal funcione (Feature:
    /// requiere electricidad). Verifica si hay una fuente de energia activa
    /// (generador, banco de baterias, panel solar) dentro de un radio de
    /// bloques alrededor del portal.
    ///
    /// ACLARACION IMPORTANTE sobre el pedido original ("mismo rango que
    /// focos vanilla"): el sistema de energia real de V3.0 (confirmado
    /// decompilando PowerManager/PowerItem/PowerSource contra el
    /// Assembly-CSharp.dll real) NO funciona por radio/distancia — es un
    /// grafo de cableado (PowerItem.Parent/Children, armado con la
    /// herramienta de cableado), donde un foco (BlockPoweredLight) necesita
    /// estar conectado por CABLE a una fuente, sin importar la distancia. No
    /// existe un "rango de focos" real que copiar. Lo que SI se implementa
    /// aqui, con APIs reales, es un chequeo de distancia propio (no nativo
    /// del juego): se recorre PowerManager.Instance.PowerSources (lista real
    /// de todas las fuentes de energia del mundo) y se compara la posicion
    /// de cada una (PowerItem.Position, real) contra la del portal,
    /// aceptando cualquier fuente encendida (PowerSource.IsOn, real —
    /// confirmado por decompilacion que simplemente expone el campo interno
    /// "isOn": el generador/banco de baterias/panel solar esta activo) sin
    /// necesidad de cableado.
    /// </summary>
    internal static class PortalPower
    {
        internal const int RangeBlocks = 10;
        private const int RangeBlocksSquared = RangeBlocks * RangeBlocks;

        /// <summary>True si hay al menos una fuente de energia ENCENDIDA dentro de RangeBlocks bloques de la posicion dada.</summary>
        internal static bool HasNearbyPower(Vector3i pos)
        {
            if (!PowerManager.HasInstance)
            {
                return false;
            }

            var sources = PowerManager.Instance.PowerSources;
            if (sources == null)
            {
                return false;
            }

            foreach (var source in sources)
            {
                if (source == null || !source.IsOn)
                {
                    continue;
                }

                var sourcePos = source.Position;
                var dx = sourcePos.x - pos.x;
                var dy = sourcePos.y - pos.y;
                var dz = sourcePos.z - pos.z;
                var distSq = dx * dx + dy * dy + dz * dz;

                if (distSq <= RangeBlocksSquared)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
