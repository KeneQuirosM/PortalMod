using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Efectos visuales del portal usando UNICAMENTE recursos vanilla (sin
    /// AssetBundles ni modelos externos):
    ///
    ///  - Cambio de estado inactivo/activo: se resuelve intercambiando el
    ///    BlockValue del bloque colocado entre "portalBlock" (huerfano),
    ///    "portalBlockActive" (vinculado, fase baja del pulso) y
    ///    "portalBlockActivePulseHigh" (vinculado, fase alta del pulso) —
    ///    los tres son variantes del mismo bloque base definidas en
    ///    blocks.xml, cada una con sus propias propiedades Light* vanilla.
    ///    Alternar entre las dos variantes activas simula el "leve
    ///    pulso/parpadeo" pedido, sin depender de una propiedad de flicker
    ///    nativa que no se pudo confirmar (ver TODO en blocks.xml).
    ///
    ///  - Particulas ambientales (idle) y de rafaga de teletransporte:
    ///    reutilizan nombres de particulas YA EXISTENTES en el juego base
    ///    ("p_electric_shock", "p_sparks_fuse"), confirmados con grep contra
    ///    los XML vanilla instalados (usados ahi en atributos particle="...").
    /// </summary>
    internal static class PortalVisualFX
    {
        internal enum BlockState
        {
            Inactive,
            Active,
            ActivePulseHigh
        }

        private const string InactiveBlockName = "portalBlock";
        private const string ActiveBlockName = "portalBlockActive";
        private const string ActivePulseHighBlockName = "portalBlockActivePulseHigh";

        private const float AmbientTickInterval = 0.6f;
        // Las particulas del estado huerfano son "escasas": solo se disparan
        // 1 de cada N ticks ambientales para lograr el efecto lento/en espera.
        private const int OrphanParticleTickModulo = 4;

        private static float _nextAmbientTick;
        private static bool _pulseHighFrame;
        private static int _ambientTickCount;

        // ========================================================================
        // CAMBIO DE ESTADO DEL BLOQUE (inactivo <-> activo)
        // ========================================================================

        /// <summary>
        /// Intercambia el BlockValue en el mundo por la variante correspondiente
        /// al nuevo estado. No hace nada si el bloque ya esta en ese estado
        /// (evita trafico de red / parpadeo innecesario en cada ambient tick).
        /// </summary>
        public static void SetBlockState(Vector3i pos, BlockState state)
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null)
            {
                return;
            }

            var targetName = state == BlockState.Inactive ? InactiveBlockName
                : state == BlockState.Active ? ActiveBlockName
                : ActivePulseHighBlockName;

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
        // TICK AMBIENTAL: pulso de luz en portales activos + particulas idle
        // ========================================================================

        /// <summary>Se invoca desde PortalTeleport.Tick() en cada tick de juego; se auto-throttlea internamente.</summary>
        public static void AmbientTick()
        {
            if (Time.time < _nextAmbientTick)
            {
                return;
            }

            _nextAmbientTick = Time.time + AmbientTickInterval;
            _pulseHighFrame = !_pulseHighFrame;
            _ambientTickCount++;

            foreach (var pos in PortalManager.Instance.GetAllPortalPositions())
            {
                if (PortalManager.Instance.IsPositionActive(pos))
                {
                    // Estado ACTIVO: pulso de luz alternando entre las dos
                    // variantes de bloque, mas particulas densas/rapidas cada tick.
                    SetBlockState(pos, _pulseHighFrame ? BlockState.ActivePulseHigh : BlockState.Active);
                    SpawnAmbientParticle(pos, intense: true);
                }
                else
                {
                    // Estado INACTIVO/huerfano: luz tenue estatica ya definida en
                    // blocks.xml (portalBlock), sin pulso; particulas escasas.
                    if (_ambientTickCount % OrphanParticleTickModulo == 0)
                    {
                        SpawnAmbientParticle(pos, intense: false);
                    }
                }
            }
        }

        private static void SpawnAmbientParticle(Vector3i blockPos, bool intense)
        {
            var worldPos = new Vector3(blockPos.x + 0.5f, blockPos.y + 1f, blockPos.z + 0.5f);

            // Nombres de particula CONFIRMADOS como reales (grep contra
            // Data/Config/*.xml vanilla instalado, usados ahi en atributos
            // particle="..." de verdad): "p_electric_shock" para el pulso
            // intenso del par vinculado, "p_sparks_fuse" (mas sutil) para el
            // estado huerfano/inactivo. Los nombres anteriores
            // ("electricsparks_lightning"/"electricsparks_small") eran
            // inventados — 0 coincidencias en todo el juego — y ademas la
            // construccion del ParticleEffect estaba rota (ver FIX real en
            // SpawnParticleServer), asi que nunca se habian detectado como
            // invalidos.
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

            // Mismo nombre confirmado que el pulso intenso (ver SpawnAmbientParticle).
            SpawnParticleServer("p_electric_shock", worldPos);
        }

        // FIX real ("Unknown particle effect: 0" repitiendose cada ambient tick,
        // ~cada 0.6s por portal): decompilando ParticleEffect.GetDynamicTransform
        // contra el Assembly-CSharp.dll real se confirmo que ese log se dispara
        // cuando "ParticleId" (un int) no esta en la tabla de particulas
        // cargadas. El codigo anterior (por reflection) creaba el
        // ParticleEffect con su constructor SIN ARGUMENTOS y luego intentaba
        // asignar campos "Name"/"ParticleName" que NO EXISTEN en la clase real
        // (sus campos reales son: type, attachment, pos, rot, color,
        // lightValue, ParticleId, soundName, volumeScale,
        // additionalHitSoundName, opqueTextureId, parentEntityId,
        // parentTransform, debugName) — ambos TrySetMember fallaban en
        // silencio, "ParticleId" quedaba en su valor por defecto (0), y por
        // eso SIEMPRE se logueaba "Unknown particle effect: 0" al llamar
        // SpawnParticleEffectServer.
        //
        // El constructor real que si asigna "ParticleId = ToId(_name)" es:
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
