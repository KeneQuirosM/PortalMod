using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Efectos visuales del portal usando UNICAMENTE recursos vanilla (sin
    /// AssetBundles externos que no formen ya parte de Resources/):
    ///
    ///  - Cambio de modelo/color: se resuelve intercambiando el BlockValue
    ///    del bloque colocado entre "portalBlock" (huerfano) y la variante
    ///    del bioma correspondiente (vinculado — ver PortalBiomes), los
    ///    nombres de bloque son variantes del mismo bloque base definidas en
    ///    blocks.xml, cada una con sus propias propiedades Model/Light/
    ///    TintColor. Este swap SOLO ocurre al vincularse/desvincularse un
    ///    par (RegisterPortal/UnregisterPortal en PortalManager.cs), NUNCA
    ///    en el ambient tick — ver FIX real mas abajo sobre por que.
    ///
    ///  - Particulas ambientales (idle) y de rafaga de teletransporte:
    ///    reutilizan nombres de particulas YA EXISTENTES en el juego base
    ///    ("p_electric_shock", "p_sparks_fuse"), confirmados con grep contra
    ///    los XML vanilla instalados (usados ahi en atributos particle="...").
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
    /// </summary>
    internal static class PortalVisualFX
    {
        private enum BlockState
        {
            Inactive,
            Active
        }

        private const float AmbientTickInterval = 0.6f;
        // Las particulas del estado huerfano/sin energia son "escasas": solo
        // se disparan 1 de cada N ticks ambientales para lograr el efecto
        // lento/en espera.
        private const int OrphanParticleTickModulo = 4;

        private static float _nextAmbientTick;
        private static int _ambientTickCount;

        /// <summary>
        /// Reevalua y aplica el bloque correcto para una posicion de portal:
        /// variante del bioma si esta vinculado, o el bloque base
        /// "portalBlock" (huerfano) si no. Se llama SOLO al vincularse/
        /// desvincularse un par (PortalManager.RegisterPortal/
        /// UnregisterPortal) — nunca desde el ambient tick, ver FIX real de
        /// la clase.
        /// </summary>
        internal static void RefreshBlockState(Vector3i pos, bool linked)
        {
            if (linked)
            {
                var biome = PortalManager.Instance.GetBiome(pos);
                SetBlockState(pos, BlockState.Active, biome);
            }
            else
            {
                SetBlockState(pos, BlockState.Inactive, null);
            }
        }

        /// <summary>
        /// Intercambia el BlockValue en el mundo por la variante
        /// correspondiente. No hace nada si el bloque ya esta en ese estado
        /// (evita trafico de red / desconexiones de cable innecesarias).
        /// </summary>
        private static void SetBlockState(Vector3i pos, BlockState state, string biome)
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null)
            {
                return;
            }

            var targetName = state == BlockState.Inactive
                ? PortalBiomes.InactiveBlockName
                : PortalBiomes.GetActiveBlockName(biome);

            // TODO: verificar en Assembly-CSharp V3.0 la API real para resolver
            // un Block por nombre y construir su BlockValue. Candidato de builds
            // anteriores: Block.GetBlockByName(string) devuelve un Block cuyo
            // campo "blockID" se usa para construir "new BlockValue(blockID)".
            var targetBlock = Block.GetBlockByName(targetName);
            if (targetBlock == null)
            {
                API.LogWarning($"No se encontro el bloque '{targetName}' (revisa Config/blocks.xml).");
                return;
            }

            // TODO: verificar en Assembly-CSharp V3.0 el metodo correcto para
            // leer el bloque actual en una posicion. Candidato de builds
            // anteriores: World.GetBlock(Vector3i).
            var currentBv = world.GetBlock(pos);
            if (currentBv.Block == targetBlock)
            {
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
            _ambientTickCount++;

            foreach (var pos in PortalManager.Instance.GetAllPortalPositions())
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
        }

        private static void SpawnAmbientParticle(Vector3i blockPos, bool intense)
        {
            var worldPos = new Vector3(blockPos.x + 0.5f, blockPos.y + 1f, blockPos.z + 0.5f);

            // Nombres de particula CONFIRMADOS como reales (grep contra
            // Data/Config/*.xml vanilla instalado, usados ahi en atributos
            // particle="..." de verdad): "p_electric_shock" para el estado
            // vinculado+con energia, "p_sparks_fuse" (mas sutil) para
            // huerfano/sin energia.
            var particleName = intense ? "p_electric_shock" : "p_sparks_fuse";

            SpawnParticleServer(particleName, worldPos);
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

            // Mismo nombre confirmado que el estado intenso (ver SpawnAmbientParticle).
            SpawnParticleServer("p_electric_shock", worldPos);
        }

        // FIX real ("Unknown particle effect: 0" repitiendose cada ambient tick,
        // ~cada 0.6s por portal): decompilando ParticleEffect.GetDynamicTransform
        // contra el Assembly-CSharp.dll real se confirmo que ese log se dispara
        // cuando "ParticleId" (un int) no esta en la tabla de particulas
        // cargadas. El constructor real que si asigna "ParticleId = ToId(_name)" es:
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
