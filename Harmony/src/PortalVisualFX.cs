using System;
using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Efectos visuales del portal usando UNICAMENTE recursos vanilla (sin
    /// AssetBundles externos que no formen ya parte de Resources/):
    ///
    ///  - Cambio de modelo/color: se resuelve intercambiando el BlockValue
    ///    del bloque colocado entre el bloque inactivo del ESTILO del
    ///    portal (huerfano — Feature "Opcion A: 6 items separados", el
    ///    estilo se elige al craftear/colocar el item, ver PortalManager.
    ///    GetStyle) y la variante de ese mismo estilo para el bioma
    ///    correspondiente (vinculado — ver PortalBiomes), los nombres de
    ///    bloque son variantes del mismo bloque base definidas en
    ///    blocks.xml, cada una con sus propias propiedades Model/Light/
    ///    TintColor. Este swap SOLO ocurre al vincularse/desvincularse un
    ///    par (RegisterPortal/UnregisterPortal en PortalManager.cs), NUNCA
    ///    en el ambient tick — ver FIX real mas abajo sobre por que.
    ///
    ///  - Particulas ambientales (idle) y de rafaga de teletransporte:
    ///    usan "electric_fence_sparks", el UNICO nombre tematicamente
    ///    electrico confirmado real para la API que usa este archivo
    ///    (GameManager.SpawnParticleEffectServer/ParticleEffect) — ver FIX
    ///    real en SpawnParticleServer sobre por que los nombres usados
    ///    antes ("p_electric_shock"/"p_sparks_fuse", validos pero de un
    ///    sistema de particulas DISTINTO) nunca funcionaban aqui.
    ///
    /// FIX real (el portal perdia la conexion de cable constantemente,
    /// Feature "requiere electricidad" — portalBlock ahora es Class="Powered"
    /// en blocks.xml, ver FIX real ahi): el diseño anterior de este archivo
    /// alternaba el BlockValue del portal cada ~0.6s entre dos variantes
    /// "activas" (pulso de luz) Y ademas re-evaluaba/aplicaba el estado
    /// segun energia en CADA ambient tick. Decompilando Chunk.SetBlock
    /// contra el Assembly-CSharp.dll real se confirmo que CUALQUIER swap de
    /// BlockValue con un "type" (ID de bloque) distinto dispara
    /// Block.OnBlockRemoved en el bloque ANTERIOR
    /// ("blockValue.type != _blockValue.type" es el gate) — y
    /// BlockPowered.OnBlockRemoved (heredado por portalBlock) llama
    /// explicitamente a PowerManager.Instance.RemovePowerNode(...) y
    /// tileEntityPowered.RemoveWires() en ese momento. Como portalBlock,
    /// portalBlockActive y cada variante de bioma son bloques con "type"
    /// (ID) distintos entre si (aunque compartan Class="Powered" via
    /// Extends), CUALQUIER swap entre ellos desconecta el cable — y como el
    /// diseño anterior swapeaba cada 0.6s (pulso) y tambien cada vez que
    /// cambiaba el estado de energia, el portal jamas podia quedarse
    /// conectado mas de un instante: el propio swap que reflejaba "ahora
    /// tiene energia" era lo que le quitaba el cable, causando que en el
    /// siguiente tick volviera a leerse sin energia, undo del swap, y asi en
    /// loop infinito.
    ///
    /// FIX: el BlockValue del portal ahora SOLO cambia al vincularse/
    /// desvincularse un par (evento raro y deliberado, disparado por el
    /// jugador colocando el segundo portal o destruyendo uno), nunca por el
    /// estado de energia ni en un tick periodico. Esto significa que, una
    /// vez vinculado, el modelo/color del portal refleja el BIOMA de forma
    /// estable (no cambia si se desconecta el generador), y el requisito de
    /// energia real (Feature "requiere electricidad") se comunica al
    /// jugador via el mensaje HUD "Portal sin energia" (ver PortalTeleport.cs)
    /// en vez de un cambio visual — el swap de bloque y el cableado
    /// electrico real son mutuamente incompatibles en V3.0 tal como esta
    /// implementado el sistema de energia (grafo de PowerItem atado al
    /// TileEntity de una posicion/BlockValue especifica).
    ///
    /// INVESTIGACION ADICIONAL (pedido: suscribirse a un evento de cambio de
    /// energia de TileEntityPowered para refrescar el visual): se buscaron
    /// por reflection TODOS los eventos C# (`event`) declarados en
    /// TileEntityPowered, TileEntity y PowerItem contra el
    /// Assembly-CSharp.dll real. El UNICO evento real que existe es
    /// "Destroyed" (tipo XUiEvent_TileEntityDestroyed, se dispara al
    /// destruirse el TileEntity, no al cambiar su energia) — NO existe
    /// ningun evento/callback de "cambio de estado de energia". Decompilando
    /// PowerManager.Update() se confirmo que el grafo de energia se
    /// RECALCULA por POLLING cada 0.16s (no por eventos), y
    /// TileEntityPoweredBlock.ClientUpdate() replica ese estado a los
    /// clientes — el mismo patron de polling que ya usa este archivo (leer
    /// PortalPower.HasNearbyPower en cada ambient tick, cada 0.6s, para
    /// decidir la particula). Aun si hubiera un evento, conectarlo a un
    /// swap de BlockValue reintroduciria EXACTAMENTE el mismo problema
    /// descripto arriba (el propio swap desconectaria el cable que acaba de
    /// energizarse) — el problema no es "polling vs evento", es que
    /// cualquier cambio de BlockValue corta el cable, sea como sea que se
    /// detecte el cambio de energia.
    /// </summary>
    internal static class PortalVisualFX
    {
        private enum BlockState
        {
            Inactive,
            Active
        }

        // FIX real (spam de "[FX] AmbientTick" en el log — 500+ entradas por
        // sesion incluso con 0 portales colocados): el intervalo real de
        // 0.6s permitia mas de un tick por segundo. Se sube a 1.0s (limite
        // pedido explicitamente: "no correr mas de una vez por segundo") y
        // se agrega un early-exit por 0 portales en AmbientTick (ver mas
        // abajo) para no hacer NINGUN trabajo (ni logging) cuando no hay
        // portales colocados en el mundo.
        private const float AmbientTickInterval = 1.0f;
        // Las particulas del estado huerfano/sin energia son "escasas": solo
        // se disparan 1 de cada N ticks ambientales para lograr el efecto
        // lento/en espera.
        private const int OrphanParticleTickModulo = 4;

        private static float _nextAmbientTick;
        private static int _ambientTickCount;

        /// <summary>
        /// Reevalua y aplica el bloque correcto para una posicion de portal:
        /// variante del ESTILO+bioma (Feature "Opcion A: 6 items separados",
        /// ver PortalBiomes) si esta vinculado, o el bloque inactivo del
        /// mismo estilo (huerfano) si no. Se llama SOLO al vincularse/
        /// desvincularse un par (PortalManager.RegisterPortal/
        /// UnregisterPortal) — nunca desde el ambient tick, ver FIX real de
        /// la clase.
        /// </summary>
        internal static void RefreshBlockState(Vector3i pos, bool linked)
        {
            var style = PortalManager.Instance.GetStyle(pos);

            // Diagnostico ("el portal se ve apagado aunque tenga energia"):
            // este es el UNICO lugar donde el modelo del portal cambia (ver
            // FIX real de la clase). Si el log de aqui no aparece al
            // vincular un par, el bug no es de energia sino de que
            // RegisterPortal nunca llego a llamar esto.
            API.Log($"[PortalMod] RefreshBlockState pos={pos} linked={linked} style={style ?? "(default/legacy)"}");

            if (linked)
            {
                var biome = PortalManager.Instance.GetBiome(pos);
                SetBlockState(pos, BlockState.Active, biome, style);
            }
            else
            {
                SetBlockState(pos, BlockState.Inactive, null, style);
            }
        }

        /// <summary>
        /// Intercambia el BlockValue en el mundo por la variante
        /// correspondiente. No hace nada si el bloque ya esta en ese estado
        /// (evita trafico de red / desconexiones de cable innecesarias).
        /// </summary>
        private static void SetBlockState(Vector3i pos, BlockState state, string biome, string style)
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null)
            {
                return;
            }

            var targetName = state == BlockState.Inactive
                ? PortalBiomes.GetInactiveBlockName(style)
                : PortalBiomes.GetActiveBlockName(style, biome);

            // TODO: verificar en Assembly-CSharp V3.0 la API real para resolver
            // un Block por nombre y construir su BlockValue. Candidato de builds
            // anteriores: Block.GetBlockByName(string) devuelve un Block cuyo
            // campo "blockID" se usa para construir "new BlockValue(blockID)".
            var targetBlock = Block.GetBlockByName(targetName);
            if (targetBlock == null)
            {
                // Diagnostico ("el portal se ve apagado aunque tenga
                // energia"): si esto aparece en el log, el bug real es que
                // el bloque de la variante ("targetName") no existe/no
                // cargo — revisar blocks.xml (typo de nombre, o el mod no
                // termino de cargar esta variante).
                API.LogWarning($"[PortalMod] SetBlockState: no se encontro el bloque '{targetName}' (revisa Config/blocks.xml) — el portal en {pos} se quedara con su modelo actual.");
                return;
            }

            // TODO: verificar en Assembly-CSharp V3.0 el metodo correcto para
            // leer el bloque actual en una posicion. Candidato de builds
            // anteriores: World.GetBlock(Vector3i).
            var currentBv = world.GetBlock(pos);
            API.Log($"[PortalMod] SetBlockState pos={pos} bloqueActual={currentBv.Block?.GetBlockName()} bloqueObjetivo={targetName}");
            if (currentBv.Block == targetBlock)
            {
                // Ya esta en el bloque correcto — no hace falta swap (y no
                // se toca el cable/TileEntity, ver FIX real de la clase).
                return;
            }

            // El compilador real confirmo que BlockValue espera un uint, no un
            // int como se asumia (targetBlock.blockID es int) — cast explicito.
            var newBv = new BlockValue((uint)targetBlock.blockID);
            newBv.rotation = currentBv.rotation;

            // TODO: verificar en Assembly-CSharp V3.0 la firma exacta de
            // World.SetBlockRPC para replicar el cambio a todos los clientes
            // en servidor dedicado. Candidato de builds anteriores:
            // World.SetBlockRPC(Vector3i _blockPos, BlockValue _bv).
            world.SetBlockRPC(pos, newBv);
        }

        // ========================================================================
        // TICK AMBIENTAL: solo particulas idle — NUNCA cambia el BlockValue
        // (ver FIX real de la clase: eso desconectaria el cable).
        // ========================================================================

        /// <summary>Se invoca desde PortalTeleport.Tick() en cada tick de juego; se auto-throttlea internamente.</summary>
        public static void AmbientTick()
        {
            if (Time.time < _nextAmbientTick)
            {
                return;
            }

            _nextAmbientTick = Time.time + AmbientTickInterval;

            // FIX real (spam de log — ver comentario en AmbientTickInterval):
            // salir ANTES de tocar _ambientTickCount o loguear nada si no hay
            // ningun portal colocado en el mundo (caso mas comun: mod recien
            // instalado, o mundo sin portales aun).
            var positions = PortalManager.Instance.GetAllPortalPositions();
            if (positions.Count == 0)
            {
                return;
            }

            _ambientTickCount++;

            // AUDITORIA (manejo de errores): GetAllPortalPositions() ya
            // devuelve una copia (snapshot) segura para enumerar (ver FIX en
            // PortalManager.cs). Cada posicion se procesa en su propio
            // try/catch para que un portal puntual con datos raros no le
            // quite el ambient tick al resto.
            foreach (var pos in positions)
            {
                try
                {
                    // Leer el estado de energia real (TileEntityPowered.IsPowered,
                    // ver PortalPower.cs) aqui es seguro: solo decide que
                    // particula disparar, nunca toca el BlockValue.
                    if (PortalManager.Instance.IsPositionActive(pos) && PortalPower.HasNearbyPower(pos))
                    {
                        // Vinculado + con energia: particulas densas/rapidas cada tick.
                        SpawnAmbientParticle(pos, intense: true);
                    }
                    else if (_ambientTickCount % OrphanParticleTickModulo == 0)
                    {
                        // Huerfano O sin energia: particulas escasas.
                        SpawnAmbientParticle(pos, intense: false);
                    }
                }
                catch (Exception e)
                {
                    API.LogError($"Excepcion en AmbientTick para portal en {pos}: {e}");
                }
            }
        }

        private static void SpawnAmbientParticle(Vector3i blockPos, bool intense)
        {
            var worldPos = new Vector3(blockPos.x + 0.5f, blockPos.y + 1f, blockPos.z + 0.5f);

            // FIX real (particulas invisibles pese a que "p_electric_shock"/
            // "p_sparks_fuse" son nombres reales — ver FIX real de
            // SpawnParticleServer mas abajo sobre POR QUE igual fallaban):
            // se usa el mismo nombre confirmado para ambos estados
            // (intenso/escaso), diferenciados solo por la frecuencia de
            // disparo (ver AmbientTick — cada 0.6s vs cada ~2.4s), ya que es
            // el UNICO nombre tematicamente electrico confirmado real para
            // esta API especifica (ver FIX real abajo).
            SpawnParticleServer("electric_fence_sparks", worldPos);
        }

        // ========================================================================
        // RAFAGA DE TELETRANSPORTE (origen y destino)
        // ========================================================================

        /// <summary>
        /// Efecto explosivo breve en la posicion indicada, usado tanto en el
        /// portal de ORIGEN (el jugador ya no esta ahi para recibir un buff) como,
        /// opcionalmente, para reforzar el efecto de llegada en el DESTINO ademas
        /// del que ya dispara buffPortalTravel (ver buffs.xml, onSelfBuffStart).
        /// </summary>
        public static void SpawnTeleportBurst(Vector3i blockPos)
        {
            var worldPos = new Vector3(blockPos.x + 0.5f, blockPos.y + 1f, blockPos.z + 0.5f);

            // Mismo nombre confirmado que el ambiental (ver SpawnAmbientParticle y FIX real abajo).
            SpawnParticleServer("electric_fence_sparks", worldPos);
        }

        // FIX real ("Unknown particle effect: <hash>" para nuestras propias
        // llamadas, particulas nunca visibles pese a compilar sin errores):
        // "p_electric_shock"/"p_sparks_fuse" (usados antes) SI son nombres
        // reales — confirmado de nuevo por grep contra buffs.xml/items.xml
        // vanilla instalados — pero pertenecen a un sistema de particulas
        // COMPLETAMENTE DISTINTO al que usa esta clase. Decompilando el
        // Assembly-CSharp.dll real se confirmaron DOS sistemas separados:
        //   1) El que usan los <triggered_effect action="AttachParticleEffectToEntity"
        //      particle="p_electric_shock"> de buffs.xml/items.xml vanilla
        //      (clase MinEventActionAttachParticleEffectToEntity): carga el
        //      addressable "ParticleEffects/<nombre-con-prefijo>.prefab"
        //      DIRECTO por ruta completa (con el prefijo "p_" incluido, sin
        //      strippearlo) y lo instancia como hijo de la entidad. NO pasa
        //      por ParticleEffect.ToId()/loadedTs en ningun momento.
        //   2) El que usa GameManager.SpawnParticleEffectServer/ParticleEffect
        //      (el que usa este archivo): decompilando
        //      ParticleEffect.LoadResources() se confirmo que precarga TODOS
        //      los assets bajo la etiqueta Addressables "particleeffects"
        //      (minuscula, catalogo DISTINTO al "ParticleEffects/..." de
        //      arriba) cuyo nombre de archivo empieza con el prefijo "p_", y
        //      registra cada uno en el diccionario "loadedTs" bajo
        //      ToId(nombre SIN el prefijo "p_") — es decir, para este
        //      sistema especifico el nombre que hay que pasarle al
        //      constructor de ParticleEffect va SIN "p_". "p_electric_shock"
        //      no es necesariamente (ni probablemente) un asset del
        //      catalogo "particleeffects" en absoluto — pertenece al otro
        //      catalogo ("ParticleEffects/..."), por eso GetDynamicTransform
        //      nunca lo encontraba en loadedTs sin importar el prefijo.
        //
        //      Se busco en TODO Assembly-CSharp.dll cada llamada real a
        //      "new ParticleEffect(<string literal>, ...)" (la MISMA firma
        //      que usa este archivo) para obtener nombres 100% confirmados
        //      de ESTE catalogo especifico. De los 17 nombres unicos
        //      encontrados (blood_impact, electric_fence_sparks, big_smoke,
        //      drone_heal_beam/player, nozzleflashuzi/nozzlesmokeuzi,
        //      blood_eat, confetti, smoke, blockdestroy_<material>,
        //      treefall, twitch_fireworks, supply_crate_impact,
        //      impact_metal_on_wood, paint_block, tiresmoke),
        //      "electric_fence_sparks" (usado en ElectricWireController y
        //      SpinningBladeTrapController, sistemas electricos reales en
        //      juego) es el UNICO tematicamente electrico/de energia — se
        //      usa aqui para ambos estados (intenso/escaso), diferenciados
        //      solo por frecuencia de disparo.
        //
        // El resto de la mecanica ya estaba bien confirmada: el constructor
        // real que asigna "ParticleId = ToId(_name)" es:
        //   ParticleEffect(string _name, Vector3 _pos, float _lightValue,
        //       Color _color, string _soundName, Transform _parentTransform,
        //       bool _OLDCreateColliders, float _volumeScale = 1f,
        //       string _additionalHitSound = "")
        // confirmado tanto decompilando el propio constructor como viendo
        // decenas de usos reales identicos en el juego (ej. EntityAlive.
        // OnEntityDeath, Block.SpawnDestroyParticleEffect). Y
        // GameManager.SpawnParticleEffectServer(ParticleEffect, int _entityId,
        // bool _forceCreation, bool _worldSpawn) es la unica sobrecarga real
        // (sin ambiguedad). Se llama directo, sin reflection: _entityId=-1
        // (no hay entidad asociada, efecto puramente ambiental/de posicion),
        // _forceCreation=false y _worldSpawn=true (worldPos ya es una
        // posicion absoluta de mundo, no relativa a una entidad — mismo
        // patron que Block.SpawnDestroyParticleEffect).
        private static void SpawnParticleServer(string particleName, Vector3 worldPos)
        {
            if (GameManager.Instance == null)
            {
                return;
            }

            var effect = new ParticleEffect(particleName, worldPos, 0f, Color.white, null, null, _OLDCreateColliders: false);
            GameManager.Instance.SpawnParticleEffectServer(effect, -1, _forceCreation: false, _worldSpawn: true);
        }
    }
}
