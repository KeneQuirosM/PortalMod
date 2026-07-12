namespace PortalMod
{
    /// <summary>
    /// Requisito de energia electrica para que un portal funcione (Feature:
    /// requiere electricidad). Lee el estado REAL del TileEntity electrico
    /// del portal — ya no un chequeo de distancia propio (ver historial mas
    /// abajo): portalBlock ahora es Class="Powered" en blocks.xml (ver FIX
    /// real ahi), lo que le da un TileEntityPoweredBlock real, conectable
    /// con la herramienta de cableado del juego como cualquier otro
    /// consumidor de energia (generador -> cable -> portal).
    ///
    /// API real confirmada por decompilacion:
    ///   - World.GetTileEntity(Vector3i) devuelve el TileEntity en esa
    ///     posicion (o null si no hay ninguno).
    ///   - TileEntityPowered.IsPowered (propiedad publica, bool): en
    ///     servidor devuelve PowerItem.IsPowered (el resultado real del
    ///     calculo del grafo de energia — cableado + fuente encendida +
    ///     potencia suficiente); en cliente devuelve el campo replicado
    ///     "isPowered". Exactamente el estado que se necesita.
    ///
    /// HISTORIAL (version anterior de este archivo, reemplazada en este
    /// commit): antes de que portalBlock aceptara cableado, este archivo
    /// hacia un chequeo de DISTANCIA propio (recorrer
    /// PowerManager.Instance.PowerSources y comparar posiciones a mano)
    /// porque no habia forma de conectar un cable real al portal. Ya no
    /// hace falta: con el TileEntity real, el estado de energia es el
    /// mismo que usaria cualquier otro bloque electrico del juego.
    /// </summary>
    internal static class PortalPower
    {
        /// <summary>True si el TileEntity electrico del portal en esta posicion esta realmente energizado (cableado a una fuente encendida con potencia suficiente).</summary>
        internal static bool HasNearbyPower(Vector3i pos)
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null)
            {
                return false;
            }

            return world.GetTileEntity(pos) is TileEntityPowered tileEntityPowered && tileEntityPowered.IsPowered;
        }
    }
}
