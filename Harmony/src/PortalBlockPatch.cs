using System;
using System.Collections.Generic;
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

        // FIX real (encontrado al implementar la validacion de portales al
        // cargar el mundo, ver PortalManager.Load): en un momento
        // "portalBlockActivePulseHigh" falto en la comparacion de nombres de
        // abajo. PortalVisualFX.AmbientTick alterna el BlockValue de un
        // portal vinculado entre varias variantes (ver blocks.xml) — un
        // nombre faltante aqui hace fallar en silencio CUALQUIER patch que
        // dependa de IsPortalBlock (activacion con tecla E, destruccion,
        // validacion de carga) durante esa fase. Para no repetir el bug al
        // agregar las variantes por bioma (Feature de color/modelo por
        // bioma), la lista completa de nombres vive en un unico lugar:
        // PortalBiomes.GetAllBlockNames().
        private static readonly HashSet<string> AllPortalBlockNames =
            new HashSet<string>(PortalBiomes.GetAllBlockNames());

        internal static bool IsPortalBlock(Block block)
        {
            if (block == null)
            {
                return false;
            }

            return AllPortalBlockNames.Contains(block.GetBlockName());
        }

        /// <summary>
        /// Logica compartida de "el jugador activo (tecla E) un portal": si
        /// ya tiene tag, ofrecer renombrarlo; si no, pedir uno nuevo. Extraida
        /// a un metodo propio para que tanto Block_OnBlockActivated_Patch (el
        /// overload de 4 argumentos, sin _commandName, que SIEMPRE se uso
        /// para esto) como los nuevos patches sobre BlockPowered (ver mas
        /// abajo — necesarios desde que portalBlock es Class="Powered" para
        /// aceptar cableado, ver blocks.xml) llamen exactamente al mismo
        /// codigo.
        /// </summary>
        internal static void HandlePortalActivation(EntityPlayer player, Vector3i blockPos)
        {
            if (PortalManager.Instance.TryGetPortalRef(blockPos, out var portalRef))
            {
                // Regla 9: portal ya tiene tag -> ofrecer renombrarlo.
                XUiPortalTag.OpenForRename(player, blockPos, portalRef.Tag);
            }
            else
            {
                // El bloque existe en el mundo pero aun no fue registrado
                // (por ejemplo si el jugador cerro la ventana sin confirmar
                // al colocarlo): permitir asignarle un tag ahora.
                XUiPortalTag.OpenForNewPortal(player, blockPos);
            }
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
        // AUDITORIA (manejo de errores): "OnBlockPlaceBefore" del juego base
        // se ejecuta para TODA colocacion de TODO bloque, no solo portales —
        // una excepcion sin capturar aqui interrumpiria el flujo normal de
        // colocacion de cualquier bloque del juego para el jugador que la
        // disparo. Se envuelve todo el cuerpo en try/catch.
        private static void Postfix(Block __instance, ref BlockPlacement.Result _bpResult, EntityAlive _ea)
        {
            try
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
            catch (Exception e)
            {
                API.LogError($"Excepcion en Block_OnBlockPlaceBefore_Patch.Postfix: {e}");
            }
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
        // AUDITORIA (manejo de errores): este Prefix intercepta la
        // activacion (tecla E) de TODOS los bloques del juego que pasan por
        // esta sobrecarga de Block.OnBlockActivated, no solo portales. Si
        // "IsPortalBlock" o cualquier logica de abajo lanzara una excepcion
        // sin capturar, romperia la interaccion con CUALQUIER bloque
        // activable del juego para ese jugador (no solo portales). Ante un
        // error se devuelve "true" (dejar que el juego procese la
        // activacion normalmente), el fallback mas seguro/menos sorpresivo.
        private static bool Prefix(WorldBase _world, Vector3i _blockPos, BlockValue _blockValue,
            EntityPlayerLocal _player, ref bool __result)
        {
            try
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

                PortalBlockPatch.HandlePortalActivation(player, _blockPos);

                __result = true;
                // Evita que el motor ejecute cualquier logica de activacion por
                // defecto asociada a la clase base Block para este bloque.
                return false;
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion en Block_OnBlockActivated_Patch.Prefix: {e}");
                return true;
            }
        }
    }

    // ============================================================================
    // 2.1) BLOQUE ELECTRICO (BlockPowered) -> preservar la interaccion de
    // arriba para portalBlock ahora que Class="Powered" (Feature "requiere
    // electricidad", ver blocks.xml).
    //
    // BlockPowered (la clase C# real detras de Class="Powered") NO
    // sobreescribe el overload de 4 argumentos sin _commandName que parchea
    // Block_OnBlockActivated_Patch arriba (confirmado decompilando
    // BlockPowered contra el Assembly-CSharp.dll real: solo declara sus
    // propios metodos "OnBlockActivated(string, ...)",
    // "HasBlockActivationCommands" y "GetBlockActivationCommands"), asi que
    // ese patch deberia seguir funcionando sin cambios. PERO
    // HasBlockActivationCommands siempre devuelve true para cualquier
    // BlockPowered, lo que hace que el juego le muestre al jugador un menu
    // de comandos (por defecto, "Take") en vez de (o antes de) la activacion
    // simple — no se pudo confirmar sin el juego real si eso impide que el
    // overload de 4 argumentos llegue a llamarse. Como red de seguridad para
    // no arriesgar la interaccion de nombrar/renombrar (ya probada en
    // sesiones anteriores), se agregan los dos patches siguientes,
    // especificos de portalBlock:
    //   - Suprimir el menu de comandos ("Take") de BlockPowered para
    //     portalBlock, para que el juego use la ruta simple de activacion.
    //   - Redirigir cualquier activacion CON comando que igual le llegue a
    //     portalBlock hacia la misma logica de nombrar/renombrar.
    // ============================================================================
    // AUDITORIA (manejo de errores — radio de impacto amplio): estos dos
    // patches interceptan metodos de la clase BASE "BlockPowered", que
    // TODOS los bloques electricos del juego heredan (generadores, cercas
    // electricas, torretas, luces, etc.), no solo portalBlock. Sin
    // try/catch, una excepcion sin capturar aca podria romper la
    // interaccion/activacion de CUALQUIER bloque electrico del juego para
    // el jugador que la disparo, mucho mas alla de este mod. El chequeo
    // "IsPortalBlock" ya filtra al inicio, pero se envuelve todo en
    // try/catch de todas formas como red de seguridad final dado el radio
    // de impacto.
    [HarmonyPatch(typeof(BlockPowered), "HasBlockActivationCommands")]
    internal static class BlockPowered_HasBlockActivationCommands_Patch
    {
        private static bool Prefix(BlockValue _blockValue, ref bool __result)
        {
            try
            {
                if (!PortalBlockPatch.IsPortalBlock(_blockValue))
                {
                    return true;
                }

                __result = false;
                return false;
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion en BlockPowered_HasBlockActivationCommands_Patch.Prefix: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(BlockPowered), "OnBlockActivated",
        new Type[] { typeof(string), typeof(WorldBase), typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal) })]
    internal static class BlockPowered_OnBlockActivated_Patch
    {
        private static bool Prefix(WorldBase _world, Vector3i _blockPos, BlockValue _blockValue,
            EntityPlayerLocal _player, ref bool __result)
        {
            try
            {
                if (!PortalBlockPatch.IsPortalBlock(_blockValue))
                {
                    return true;
                }

                var player = _player as EntityPlayer;
                if (player == null)
                {
                    __result = false;
                    return false;
                }

                PortalBlockPatch.HandlePortalActivation(player, _blockPos);

                __result = true;
                return false;
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion en BlockPowered_OnBlockActivated_Patch.Prefix: {e}");
                return true;
            }
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
        // AUDITORIA (manejo de errores): "OnBlockDestroyedBy" corre para la
        // destruccion de TODO bloque del juego, no solo portales.
        private static void Postfix(WorldBase _world, BlockValueRef _bvRef, BlockValue _blockValue)
        {
            try
            {
                if (!PortalBlockPatch.IsPortalBlock(_blockValue))
                {
                    return;
                }

                var blockPos = _bvRef.ToBlockPos(_world);

                if (PortalManager.Instance.TryGetPortalRef(blockPos, out var portalRef))
                {
                    PortalManager.Instance.UnregisterPortal(portalRef.OwnerKey, blockPos);
                    API.Log($"portalBlock destruido en {blockPos}, desregistrado (tag='{portalRef.Tag}').");
                }
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion en Block_OnBlockDestroyedBy_Patch.Postfix: {e}");
            }
        }
    }

    // ============================================================================
    // 4) MODELO DEL BLOQUE INSTANCIADO -> activar manualmente los ParticleSystem
    // embebidos en el prefab .unity3d (gupFuturePortalN), ya que vanilla NO los
    // activa solo (ver investigacion en PortalVisualFX.cs: BlockShapeModelEntity.
    // CloneModel es un Object.Instantiate() generico sin gating de SCore, asi
    // que el problema NO es "falta SCore" sino que nadie llama Play() sobre
    // esos ParticleSystem).
    //
    // Hook real confirmado por decompilacion (NO existe "OnBlockLoaded" util
    // para esto: BlockShapeModelEntity.OnBlockAdded/OnBlockLoaded solo crean un
    // BlockEntityData "stub" y no instancian nada todavia). El punto real
    // donde el GameObject del modelo ya existe Y esta activo en la escena es:
    //
    //   Chunk.OnDisplayBlockEntities (llamado desde ChunkManager.
    //   doCopyChunksToUnity, en el hilo principal de Unity, una vez por chunk
    //   visible) hace, en este orden, para cada BlockEntityData:
    //     1. objectForType = GameObjectPool.Instance.GetObjectForType(...)
    //        (instancia/recicla el prefab del modelo)
    //     2. blockEntityData.transform = objectForType.transform
    //     3. block2.OnBlockEntityTransformBeforeActivated(...)
    //     4. objectForType.SetActive(true)
    //     5. block2.OnBlockEntityTransformAfterActivated(...)   <- aqui
    //
    // Block.OnBlockEntityTransformAfterActivated(WorldBase, Vector3i,
    // BlockValue, BlockEntityData) es "public virtual" en la clase BASE
    // "Block" (no en el shape), y BlockEntityData.transform (campo real,
    // confirmado por reflection) apunta exactamente al GameObject que
    // acaba de quedar activo (SetActive(true) ya se ejecuto un paso antes).
    // Point (2) en las preguntas del chat: por eso se puede hacer
    // "_ebcd.transform.GetComponentsInChildren<ParticleSystem>(true)"
    // directamente aca, sin necesitar ningun Tick ni polling (punto 3 de las
    // preguntas: no hace falta, este hook cubre el caso).
    //
    // OJO: como el GameObject viene de un pool (GameObjectPool.Instance),
    // este hook se dispara cada vez que el chunk que contiene el portal
    // vuelve a quedar visible (no solo la primera vez que se coloca el
    // bloque) — se protege con "if (!ps.isPlaying)" para no reiniciar
    // sistemas de particulas en loop que ya estan corriendo.
    // ============================================================================
    [HarmonyPatch(typeof(Block), "OnBlockEntityTransformAfterActivated")]
    internal static class Block_OnBlockEntityTransformAfterActivated_Patch
    {
        // AUDITORIA (manejo de errores — radio de impacto amplio): este hook
        // corre por CADA BlockEntityData que se activa visualmente en CADA
        // chunk visible del juego (no solo portales) — es uno de los mas
        // "calientes" de todo el mod en frecuencia. Una excepcion sin
        // capturar aca podria interrumpir el renderizado/activacion de
        // otros bloques en el mismo chunk.
        private static void Postfix(BlockValue _blockValue, BlockEntityData _ebcd)
        {
            try
            {
                if (GameManager.IsDedicatedServer)
                {
                    return;
                }

                if (!PortalBlockPatch.IsPortalBlock(_blockValue))
                {
                    return;
                }

                var modelTransform = _ebcd?.transform;
                if (modelTransform == null)
                {
                    return;
                }

                foreach (var ps in modelTransform.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (!ps.isPlaying)
                    {
                        ps.Play();
                    }
                }
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion en Block_OnBlockEntityTransformAfterActivated_Patch.Postfix: {e}");
            }
        }
    }
}
