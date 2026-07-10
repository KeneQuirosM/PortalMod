using System;
using System.Linq;
using System.Reflection;
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
    ///  - Particulas ambientales (idle) y de rafaga de teletransporte: se
    ///    reutilizan nombres de particulas/sonidos YA EXISTENTES en el juego
    ///    base. Como no hay forma de listar los nombres reales de particulas
    ///    sin abrir el editor de Unity o decompilar el AssetBundle del
    ///    juego, cada nombre usado aqui esta marcado como candidato/placeholder
    ///    con un TODO explicito indicando como confirmarlo.
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

            // TODO: verificar nombre real de particula vanilla en V3.0. No existe
            // un "portal"/"teleport" nativo en 7 Days to Die; se reutiliza aqui un
            // efecto electrico generico como placeholder tematico (chispas de
            // valla electrica / soldadura). Busca candidatos reales inspeccionando
            // el juego con un editor de particulas o grepeando referencias
            // "electric"/"spark"/"zap" en los XML/Prefabs que vengan con el juego.
            var particleName = intense ? "electricsparks_lightning" : "electricsparks_small";

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

            // TODO: verificar nombre real de particula vanilla en V3.0 para un
            // destello brillante/explosivo. Placeholder tematico: chispazo grande
            // de electricidad (mismo razonamiento que en SpawnAmbientParticle).
            SpawnParticleServer("electricsparks_lightning_big", worldPos);
        }

        /// <summary>
        /// El compilador real (Assembly-CSharp.dll V3.0) confirmo que
        /// ParticleEffect ya no tiene un constructor publico de 2 argumentos
        /// (string, Vector3), pero no se tuvo acceso al DLL para determinar el
        /// constructor/factory correcto. En vez de adivinar una segunda firma a
        /// ciegas (como con "Open" en XUiPortalTag.cs), la construccion del
        /// ParticleEffect y la llamada a SpawnParticleEffectServer se resuelven
        /// por reflection: se prueba primero un constructor (string, Vector3);
        /// si no existe, se prueba un constructor sin argumentos combinado con
        /// propiedades/campos de nombre y posicion conocidos por convencion
        /// (Name/ParticleName, Position/position). Si tampoco existe eso, se
        /// registra un warning y no se dispara el efecto — nunca se lanza una
        /// excepcion que pueda tumbar el mod por un efecto puramente cosmetico.
        /// TODO: reemplazar por una llamada directa en cuanto se confirme el
        /// constructor/metodo real (decompilar ParticleEffect con ILSpy/dnSpy).
        /// </summary>
        private static void SpawnParticleServer(string particleName, Vector3 worldPos)
        {
            if (GameManager.Instance == null)
            {
                return;
            }

            var effect = TryCreateParticleEffect(particleName, worldPos);
            if (effect == null)
            {
                API.LogWarning($"No se pudo construir ParticleEffect para '{particleName}' (constructor/propiedades no confirmados en V3.0).");
                return;
            }

            InvokeSpawnParticleEffectServer(effect);
        }

        private static object TryCreateParticleEffect(string particleName, Vector3 worldPos)
        {
            var type = typeof(ParticleEffect);

            var twoArgCtor = type.GetConstructor(new[] { typeof(string), typeof(Vector3) });
            if (twoArgCtor != null)
            {
                return twoArgCtor.Invoke(new object[] { particleName, worldPos });
            }

            var emptyCtor = type.GetConstructor(Type.EmptyTypes);
            if (emptyCtor == null)
            {
                return null;
            }

            var instance = emptyCtor.Invoke(null);
            if (!TrySetMember(instance, "Name", particleName))
            {
                TrySetMember(instance, "ParticleName", particleName);
            }
            if (!TrySetMember(instance, "Position", worldPos))
            {
                TrySetMember(instance, "position", worldPos);
            }
            return instance;
        }

        private static bool TrySetMember(object instance, string memberName, object value)
        {
            var type = instance.GetType();

            var field = type.GetField(memberName);
            if (field != null && field.FieldType.IsInstanceOfType(value))
            {
                field.SetValue(instance, value);
                return true;
            }

            var prop = type.GetProperty(memberName);
            if (prop != null && prop.CanWrite && prop.PropertyType.IsInstanceOfType(value))
            {
                prop.SetValue(instance, value);
                return true;
            }

            return false;
        }

        private static void InvokeSpawnParticleEffectServer(object particleEffect)
        {
            var method = typeof(GameManager).GetMethods()
                .Where(m => m.Name == "SpawnParticleEffectServer")
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault(m =>
                {
                    var ps = m.GetParameters();
                    return ps.Length > 0 && ps[0].ParameterType.IsInstanceOfType(particleEffect);
                });

            if (method == null)
            {
                API.LogWarning("No se encontro un metodo SpawnParticleEffectServer compatible en GameManager.");
                return;
            }

            var parameters = method.GetParameters();
            var args = new object[parameters.Length];
            args[0] = particleEffect;
            for (var i = 1; i < parameters.Length; i++)
            {
                var p = parameters[i];
                args[i] = p.HasDefaultValue ? p.DefaultValue
                    : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)
                    : null;
            }

            method.Invoke(GameManager.Instance, args);
        }
    }
}
