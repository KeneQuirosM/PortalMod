using System.Collections.Generic;
using UnityEngine;

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
    ///
    /// FIX real (portales viejos con "TileEntityPowered no encontrado" pese
    /// a haber estado conectados antes; portales nuevos funcionan bien):
    /// portalBlock es un MultiBlockDim="1,2,1" (ver blocks.xml) — de las dos
    /// celdas que ocupa, SOLO la celda madre (BlockValue.ischild == false)
    /// tiene un TileEntityPoweredBlock real, confirmado decompilando
    /// BlockPowered.OnBlockEntityTransformAfterActivated ("if
    /// (!_blockValue.ischild && _world.GetTileEntity(_blockPos) is
    /// TileEntityPowered ...)"). Ademas, Block.MultiBlockArray.AddChilds
    /// (decompilado) confirma que la celda HIJA reusa el MISMO
    /// BlockValue.type que la madre — PortalBlockPatch.IsPortalBlock (que
    /// solo mira el nombre del bloque) no distingue una celda de otra, asi
    /// que una posicion guardada que resulte ser la celda hija pasa la
    /// validacion de PortalManager.Load() sin problema pero jamas va a
    /// encontrar un TileEntity ahi. Si la posicion resulta ser una celda
    /// hija, se resuelve la celda madre real via BlockValue.parent (offset
    /// relativo confirmado decompilando Block.MultiBlockArray.GetParentPos:
    /// "childPos + blockValue.parent == masterPos") y se consulta ESA
    /// posicion en su lugar. PortalManager.Load() ademas corrige de forma
    /// permanente la posicion guardada la primera vez que detecta este caso
    /// (ver comentario ahi), asi que esta resolucion aca es una red de
    /// seguridad extra, no el unico lugar donde se corrige.
    ///
    /// NOTA HONESTA: no se pudo confirmar contra una partida real guardada
    /// si este es el motivo EXACTO del reporte original ("portales viejos
    /// dejaron de funcionar") — es el unico mecanismo real, confirmado por
    /// decompilacion, que explica el sintoma descrito (funciona en portales
    /// nuevos, falla en preexistentes) sin arriesgar nada si resulta no ser
    /// la causa real (si la celda no es hija, este chequeo es un no-op).
    /// Si el problema persiste incluso con este fix, lo mas probable es que
    /// esos portales especificos nunca tuvieron un TileEntityPoweredBlock
    /// real en absoluto (por ejemplo si se colocaron ANTES de que
    /// portalBlock fuera Class="Powered", ver historial de blocks.xml) — en
    /// ese caso, desconectar y reconectar el cable con la herramienta de
    /// cableado real del juego (que crea el TileEntity bajo demanda, ver
    /// ItemActionConnectPower.GetPoweredBlock) deberia arreglarlo sin
    /// necesitar mas cambios de codigo.
    /// </summary>
    internal static class PortalPower
    {
        // FIX real (log spam — "TileEntityPowered no encontrado" cientos de
        // veces por sesion): estos dos logs eran diagnostico explicito sin
        // ningun throttle, corriendo en cada ambient tick (~1/seg por
        // portal, ver PortalVisualFX) y en cada intento de teletransporte.
        // Se limita a como maximo 1 vez cada 30s POR POSICION (no global:
        // varios portales no deberian competir por el mismo throttle).
        private static readonly Dictionary<Vector3i, float> _lastLogTime = new Dictionary<Vector3i, float>();
        private const float LogThrottleSeconds = 30f;

        /// <summary>True si el TileEntity electrico del portal en esta posicion esta realmente energizado (cableado a una fuente encendida con potencia suficiente).</summary>
        internal static bool HasNearbyPower(Vector3i pos)
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null)
            {
                return false;
            }

            // FIX real (reporte: 9 de 10 portales fallaban "TileEntityPowered
            // no encontrado" — solo el portal cerca del jugador pasaba el
            // check): el chunk del portal puede estar descargado de memoria
            // (streaming por distancia — el resto del jugador no esta cerca).
            // World.GetTileEntity ya decompilado confirma que, sin chunk
            // cargado, devuelve null exactamente igual que "de verdad no hay
            // TileEntity ahi" — indistinguible sin chequear el chunk aparte.
            // World.IsChunkAreaLoaded(x,y,z) es la MISMA API real ya usada en
            // PortalManager.CheckPortalBlockAt para este exacto problema (se
            // prefiere sobre "world.GetChunkFromWorldPos(pos)": ese metodo no
            // existe en World, vive en World.ChunkCache — confirmado por
            // decompilacion, IsChunkAreaLoaded ya hace ese trabajo). Si el
            // chunk no esta cargado, NO se puede saber el estado real de
            // energia — se asume energizado (en vez de bloquear el viaje) en
            // vez de tratar "no se pudo verificar" como "sin energia": el
            // check solo debe fallar cuando el chunk SI esta cargado y
            // confirma que no hay corriente.
            if (!world.IsChunkAreaLoaded(pos.x, pos.y, pos.z))
            {
                return true;
            }

            var resolvedPos = ResolveMasterCellPos(world, pos);
            var tileEntity = world.GetTileEntity(resolvedPos) as TileEntityPowered;

            if (tileEntity == null)
            {
                LogThrottled(pos, $"[PortalMod] Portal en pos {pos} (celda consultada {resolvedPos}) - TileEntityPowered no encontrado (world.GetTileEntity devolvio null o un tipo distinto).");
                return false;
            }

            LogThrottled(pos, $"[PortalMod] Portal en pos {pos} (celda consultada {resolvedPos}) - IsPowered: {tileEntity.IsPowered}");
            return tileEntity.IsPowered;
        }

        /// <summary>
        /// Si "pos" resulta ser la celda HIJA del multiblock 1x2x1 del
        /// portal, resuelve y devuelve la celda MADRE real (la unica con
        /// TileEntity) — ver FIX real de la clase. Si no es una celda hija
        /// (caso normal, portal registrado correctamente), devuelve "pos" sin cambios.
        /// </summary>
        private static Vector3i ResolveMasterCellPos(World world, Vector3i pos)
        {
            var blockValue = world.GetBlock(pos);
            return blockValue.ischild ? pos + blockValue.parent : pos;
        }

        private static void LogThrottled(Vector3i pos, string message)
        {
            var now = Time.time;
            if (_lastLogTime.TryGetValue(pos, out var last) && now - last < LogThrottleSeconds)
            {
                return;
            }

            _lastLogTime[pos] = now;
            API.Log(message);
        }
    }
}
