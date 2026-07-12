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
    /// TODO GENERAL: Block.OnBlockPlaceBefore, la sobrecarga objetivo de
    /// Block.OnBlockActivated (EntityPlayerLocal, no EntityAlive) y el hook de
    /// destruccion real (Block.OnBlockDestroyedBy, no OnBlockRemoved — ver
    /// comentario en Block_OnBlockDestroyedBy_Patch) ya se confirmaron contra
    /// el Assembly-CSharp.dll real de V3.0 (ver comentarios en cada patch, y
    /// API.LogOnBlockActivatedOverloads para el logueo de respaldo via
    /// reflection). Cualquier firma incorrecta falla ruidosamente al cargar
    /// el mod (Harmony PatchAll/aplicacion del patch: "method not found",
    /// "AmbiguousMatchException", "Cannot get result from void method",
    /// etc.) — revisar el log del juego si esto vuelve a ocurrir y ajustar
    /// segun el mensaje exacto.
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

            // FIX real (encontrado al implementar la validacion de portales
            // al cargar el mundo, ver PortalManager.Load): faltaba
            // "portalBlockActivePulseHigh" aqui. PortalVisualFX.AmbientTick
            // alterna el BlockValue de un portal vinculado entre
            // "portalBlockActive" y "portalBlockActivePulseHigh" cada ~0.6s
            // (ver blocks.xml) - sin este tercer nombre, CUALQUIER patch que
            // dependa de IsPortalBlock (activacion con tecla E, destruccion,
            // y ahora la validacion de carga) fallaba en silencio la mitad
            // del tiempo, justo cuando el bloque estaba en la fase "alta"
            // del pulso.
            var name = block.GetBlockName();
            return name == "portalBlock" || name == "portalBlockActive" || name == "portalBlockActivePulseHigh";
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
            var isPortalBlock = PortalBlockPatch.IsPortalBlock(__instance);
            API.Log("[PortalMod] OnBlockPlaceBefore Postfix - bloque: " + __instance?.GetBlockName() + " esPortalBlock: " + isPortalBlock);

            if (!isPortalBlock)
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
            API.Log("[PortalMod] Llamando a XUiPortalTag.OpenForNewPortal...");
            XUiPortalTag.OpenForNewPortal(player, blockPos);
        }
    }

    // ============================================================================
    // 2) INTERACCION CON TECLA E -> renombrar si ya tiene tag, o pedirlo si no.
    //
    // Firma real confirmada por reflection directa contra el Assembly-CSharp.dll
    // instalado (MetadataLoadContext, sin ejecutar el ensamblado — ver tambien
    // API.LogOnBlockActivatedOverloads para confirmarlo en el log del juego).
    // Block.OnBlockActivated tiene dos sobrecargas en V3.0:
    //   bool OnBlockActivated(WorldBase, Vector3i, BlockValue, EntityPlayerLocal)
    //   bool OnBlockActivated(String _commandName, WorldBase, Vector3i, BlockValue, EntityPlayerLocal)
    // El ultimo parametro es "EntityPlayerLocal", no "EntityAlive" (de ahi el
    // "Undefined target method" original: Harmony buscaba una sobrecarga con
    // EntityAlive que nunca existio). Se targetea la primera (sin _commandName),
    // que es la que dispara la interaccion normal con tecla E. EntityPlayerLocal
    // hereda de EntityPlayer, asi que el cast "as EntityPlayer" de abajo sigue
    // siendo valido.
    // ============================================================================
    [HarmonyPatch(typeof(Block), "OnBlockActivated",
        new Type[] { typeof(WorldBase), typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal) })]
    internal static class Block_OnBlockActivated_Patch
    {
        private static bool Prefix(WorldBase _world, Vector3i _blockPos, BlockValue _blockValue,
            EntityPlayerLocal _player, ref bool __result)
        {
            if (!PortalBlockPatch.IsPortalBlock(_blockValue))
            {
                // No es un portal: dejar que el juego procese la activacion normalmente.
                return true;
            }

            var player = _player as EntityPlayer;
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

    // ============================================================================
    // 3) BLOQUE DESTRUIDO -> desregistrar del PortalManager.
    //
    // FIX real (el portal se desregistraba solo, inmediatamente despues de
    // colocarlo — log real: "Portal registrado" seguido de "Portal eliminado"/
    // "portalBlock destruido" en el mismo instante, sin que el jugador
    // destruyera nada): "OnBlockRemoved" NO es un hook de "el jugador destruyo
    // este bloque" — decompilando Chunk.SetBlock contra el Assembly-CSharp.dll
    // real se confirmo que dispara OnBlockRemoved para CUALQUIER cambio de
    // "type" en esa posicion (via SetBlock -> SetBlockRaw + comparacion de
    // tipo), incluida la propia inicializacion interna de un multiblock
    // (portalBlock usa MultiBlockDim="1,2,1"): MultiBlockManager.
    // UpdateTrackedBlockData() se llama dentro del mismo SetBlock que coloca
    // el bloque, y ese seguimiento interno del multiblock puede volver a
    // escribir la MISMA posicion, disparando un OnBlockRemoved "fantasma"
    // sobre el portalBlock recien colocado.
    //
    // El hook real de "un jugador destruyo este bloque por daño/mineria" es
    // Block.OnBlockDestroyedBy(WorldBase, BlockValueRef, BlockValue, int
    // _entityId, bool _bUseHarvestTool) — confirmado decompilando
    // Block.OnBlockDamaged, que lo llama SOLO cuando el daño acumulado llega a
    // MaxDamage, y ANTES de aplicar el SetBlockRPC que reemplaza el bloque por
    // aire/downgrade. No aparece invocado desde ningun lugar relacionado con
    // colocacion o seguimiento de multiblocks.
    // ============================================================================
    [HarmonyPatch(typeof(Block), "OnBlockDestroyedBy")]
    internal static class Block_OnBlockDestroyedBy_Patch
    {
        private static void Postfix(WorldBase _world, BlockValueRef _bvRef, BlockValue _blockValue)
        {
            if (!PortalBlockPatch.IsPortalBlock(_blockValue))
            {
                return;
            }

            var blockPos = _bvRef.ToBlockPos(_world);

            if (PortalManager.Instance.TryGetPortalRef(blockPos, out var portalRef))
            {
                PortalManager.Instance.UnregisterPortal(portalRef.SteamId, blockPos);
                API.Log($"portalBlock destruido en {blockPos}, desregistrado (tag='{portalRef.Tag}').");
            }
        }
    }
}
