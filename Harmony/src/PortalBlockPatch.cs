using System;
using HarmonyLib;
using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Patches de Harmony sobre la clase base "Block" (no se usa una subclase
    /// de bloque en C# a proposito, para que portalBlock siga siendo un bloque
    /// XML puro definido enteramente en blocks.xml). Cada patch filtra por
    /// nombre de bloque ("portalBlock" / "portalBlockActive") para no afectar
    /// al resto de bloques del juego.
    ///
    /// TODO GENERAL: Block.OnBlockPlaceBefore ya se ajusto contra un error real
    /// de carga del Assembly-CSharp.dll de V3.0 (ver comentario en
    /// Block_OnBlockPlaceBefore_Patch). Block.OnBlockActivated tuvo DOS
    /// intentos fallidos seguidos (AmbiguousMatchException, luego "Undefined
    /// target method") y por eso su patch quedo comentado con /* */ mas abajo
    /// hasta tener las firmas reales — ver API.LogOnBlockActivatedOverloads,
    /// que las loguea via reflection al iniciar el mod. Block.OnBlockRemoved
    /// sigue sin confirmar — la firma usada ahi es la conocida de builds
    /// anteriores (A20-A21/1.0). Cualquier firma incorrecta falla ruidosamente
    /// al cargar el mod (Harmony PatchAll/aplicacion del patch: "method not
    /// found", "AmbiguousMatchException", "Cannot get result from void
    /// method", etc.) — revisar el log del juego si esto vuelve a ocurrir y
    /// ajustar segun el mensaje exacto.
    /// </summary>
    internal static class PortalBlockPatch
    {
        internal static bool IsPortalBlock(BlockValue blockValue)
        {
            return IsPortalBlock(blockValue.Block);
        }

        internal static bool IsPortalBlock(Block block)
        {
            if (block == null)
            {
                return false;
            }

            var name = block.GetBlockName();
            return name == "portalBlock" || name == "portalBlockActive";
        }
    }

    // ============================================================================
    // 1) BLOQUE COLOCADO -> abrir ventana XUi para asignar el tag inicial.
    // ============================================================================
    [HarmonyPatch(typeof(Block), "OnBlockPlaceBefore")]
    internal static class Block_OnBlockPlaceBefore_Patch
    {
        // Firma real confirmada por el error de carga de Harmony:
        //   virtual void Block.OnBlockPlaceBefore(WorldBase _world,
        //       ref BlockPlacement.Result _bpResult, EntityAlive _ea, GameRandom _rnd)
        // Es "void", NO "bool" — Harmony no puede inyectar "__result" en un
        // Postfix de un metodo void ("Cannot get result from void method...").
        // Se elimino ese parametro; el Postfix ya no declara "_rnd" tampoco
        // porque no lo necesita (Harmony solo inyecta los parametros del
        // metodo original que el patch declara por nombre, el resto se ignora).
        //
        // TODO: al ser "OnBlockPlaceBefore" (llamado ANTES de colocar el
        // bloque), sin __result ya no hay forma de saber si el juego termino
        // aceptando o rechazando la colocacion (por ejemplo por colision en el
        // punto elegido). Este Postfix ahora abre la ventana de tag apenas se
        // INTENTA colocar un portalBlock, no necesariamente cuando el bloque
        // quedo realmente en el mundo. Si en el juego real la ventana llega a
        // abrirse sin que el bloque exista, mover esta logica a un hook que
        // corra DESPUES de la colocacion real (candidato: Block.OnBlockAdded,
        // pendiente de confirmar su firma tambien contra el DLL real).
        private static void Postfix(Block __instance, ref BlockPlacement.Result _bpResult, EntityAlive _ea)
        {
            if (!PortalBlockPatch.IsPortalBlock(__instance))
            {
                return;
            }

            var player = _ea as EntityPlayer;
            if (player == null)
            {
                return;
            }

            var blockPos = _bpResult.blockPos;
            API.Log($"portalBlock colocado en {blockPos} por {PortalIdentity.GetSteamId(player)}. Abriendo ventana de nombre de tag.");

            // El bloque todavia no esta registrado en PortalManager: se registra
            // recien cuando el jugador confirma un tag en la ventana (ver XUiPortalTag).
            XUiPortalTag.OpenForNewPortal(player, blockPos);
        }
    }

    // ============================================================================
    // 2) INTERACCION CON TECLA E -> renombrar si ya tiene tag, o pedirlo si no.
    //
    // DESHABILITADO TEMPORALMENTE (comentado con /* */ a proposito, no borrado):
    // la firma explicita (WorldBase, Vector3i, BlockValue, EntityAlive) que se
    // probo aqui NO existe en el Assembly-CSharp.dll real de V3.0 — el error de
    // carga paso de "AmbiguousMatchException" (no sabia cual sobrecarga) a
    // "Undefined target method" (ninguna sobrecarga coincide con esos 4 tipos
    // exactos). Dos adivinanzas seguidas fallaron, asi que en vez de intentar
    // una tercera a ciegas y volver a tumbar la carga del mod, se comenta este
    // patch completo para que PatchAll() ya ni lo intente, y se agrega en su
    // lugar un logueo por reflection de TODAS las sobrecargas reales de
    // Block.OnBlockActivated (ver API.cs, LogOnBlockActivatedOverloads) para
    // leerlas directamente del log del juego en vez de seguir adivinando.
    //
    // IMPACTO: mientras este patch siga comentado, interactuar (tecla E) con
    // un portal ya colocado NO abre la ventana de renombrado (regla 9 de la
    // spec del mod) — el resto del mod (colocar, vincular por tag, viajar,
    // destruir) sigue funcionando normalmente, ya que no depende de este patch.
    //
    // TODO: una vez que el log muestre las firmas reales de
    // Block.OnBlockActivated, descomentar este bloque, ajustar el
    // [HarmonyPatch(...)] con los tipos correctos y el Prefix con los
    // parametros/nombres que correspondan, y borrar este comentario.
    // ============================================================================
    /*
    [HarmonyPatch(typeof(Block), "OnBlockActivated",
        new Type[] { typeof(WorldBase), typeof(Vector3i), typeof(BlockValue), typeof(EntityAlive) })]
    internal static class Block_OnBlockActivated_Patch
    {
        private static bool Prefix(WorldBase _world, Vector3i _blockPos, BlockValue _blockValue,
            EntityAlive _entityFocusing, ref bool __result)
        {
            if (!PortalBlockPatch.IsPortalBlock(_blockValue))
            {
                // No es un portal: dejar que el juego procese la activacion normalmente.
                return true;
            }

            var player = _entityFocusing as EntityPlayer;
            if (player == null)
            {
                __result = false;
                return false;
            }

            if (PortalManager.Instance.TryGetPortalRef(_blockPos, out var portalRef))
            {
                // Regla 9: portal ya tiene tag -> ofrecer renombrarlo.
                XUiPortalTag.OpenForRename(player, _blockPos, portalRef.Tag);
            }
            else
            {
                // El bloque existe en el mundo pero aun no fue registrado
                // (por ejemplo si el jugador cerro la ventana sin confirmar
                // al colocarlo): permitir asignarle un tag ahora.
                XUiPortalTag.OpenForNewPortal(player, _blockPos);
            }

            __result = true;
            // Evita que el motor ejecute cualquier logica de activacion por
            // defecto asociada a la clase base Block para este bloque.
            return false;
        }
    }
    */

    // ============================================================================
    // 3) BLOQUE DESTRUIDO -> desregistrar del PortalManager.
    // ============================================================================
    [HarmonyPatch(typeof(Block), "OnBlockRemoved")]
    internal static class Block_OnBlockRemoved_Patch
    {
        // TODO: verificar en Assembly-CSharp V3.0. Firma esperada (builds previas):
        //   public virtual void OnBlockRemoved(WorldBase _world, Chunk _chunk,
        //       Vector3i _blockPos, BlockValue _blockValue)
        private static void Postfix(Block __instance, Vector3i _blockPos, BlockValue _blockValue)
        {
            if (!PortalBlockPatch.IsPortalBlock(_blockValue))
            {
                return;
            }

            if (PortalManager.Instance.TryGetPortalRef(_blockPos, out var portalRef))
            {
                PortalManager.Instance.UnregisterPortal(portalRef.SteamId, _blockPos);
                API.Log($"portalBlock destruido en {_blockPos}, desregistrado (tag='{portalRef.Tag}').");
            }
        }
    }
}
