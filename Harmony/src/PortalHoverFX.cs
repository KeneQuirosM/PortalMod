using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Feedback client-side al apuntar (hover) a un portalBlock con la mira:
    /// tooltip HUD mostrando el tag (para saber a donde lleva) y texto
    /// flotante sobre el bloque en el mundo, visible desde lejos.
    ///
    /// Ambos usan APIs reales confirmadas por decompilacion contra el
    /// Assembly-CSharp.dll de V3.0:
    ///   - Deteccion de mira: EntityPlayerLocal.HitInfo (campo publico, tipo
    ///     WorldRayHitInfo) -> .bHitValid + .hit.blockPos (HitInfoDetails).
    ///     Es el mismo mecanismo que usa el juego para resaltar/activar
    ///     bloques con la tecla E.
    ///   - Tooltip: GameManager.ShowTooltip via PortalHud.ShowMessage (ya
    ///     usado en el resto del mod).
    ///   - Texto flotante: DamageText.Create(string, Color, Vector3 worldPos,
    ///     Vector3 velocity, float scale) — confirmado decompilando
    ///     EntityAlive.DamageEntity, que es exactamente lo que el juego usa
    ///     para los numeros de dano flotantes sobre las entidades. Es un
    ///     metodo publico y generico (cualquier string), asi que se reutiliza
    ///     directo para el tag del portal en vez del sistema, mucho mas
    ///     pesado, de carteles/TileEntitySign (texturas horneadas por
    ///     bloque). DamageText.Create instancia su propio GameObject
    ///     localmente via Resources.Load+Object.Instantiate SIN replicacion
    ///     de red y usa Camera.main — por eso esta clase corre unicamente
    ///     sobre el jugador LOCAL de cada cliente (World.GetPrimaryPlayer())
    ///     y nunca en GameManager.IsDedicatedServer (ahi no existe camara).
    /// </summary>
    internal static class PortalHoverFX
    {
        private const float TooltipThrottleSeconds = 2f;

        // DamageText.TimeDuration real es 1.5s (confirmado en su constructor
        // decompilado) antes de autodestruirse; se relanza un poco despues de
        // que el anterior ya se disipo para que no se amontonen instancias.
        private const float FloatingTextThrottleSeconds = 1.8f;

        private static Vector3i? _lastHoverPos;
        private static float _nextTooltipTime;
        private static float _nextFloatingTextTime;

        /// <summary>Se invoca una vez por frame desde PortalTeleport.Tick().</summary>
        public static void Tick(World world)
        {
            if (GameManager.IsDedicatedServer || world == null)
            {
                return;
            }

            var localPlayer = world.GetPrimaryPlayer();
            var hitInfo = localPlayer?.HitInfo;
            if (hitInfo == null || !hitInfo.bHitValid)
            {
                _lastHoverPos = null;
                return;
            }

            if (!TryResolveHoveredPortal(hitInfo.hit.blockPos, out var portalPos, out var portalRef))
            {
                _lastHoverPos = null;
                return;
            }

            var now = Time.time;
            var justStartedHovering = _lastHoverPos == null || !_lastHoverPos.Value.Equals(portalPos);
            _lastHoverPos = portalPos;

            var linked = PortalManager.Instance.IsPositionActive(portalPos);

            // Al empezar a mirar un portal nuevo se muestra de inmediato (sin
            // esperar el throttle), igual que hacen los tooltips de accion
            // vanilla al pasar de un bloque a otro.
            if (justStartedHovering || now >= _nextTooltipTime)
            {
                _nextTooltipTime = now + TooltipThrottleSeconds;
                PortalHud.ShowTargetMessage(localPlayer, portalRef.Tag, linked);
            }

            if (now >= _nextFloatingTextTime)
            {
                _nextFloatingTextTime = now + FloatingTextThrottleSeconds;
                SpawnFloatingTag(portalPos, portalRef.Tag, linked);
            }
        }

        /// <summary>
        /// El portal ocupa 1x2x1 (MultiBlockDim en blocks.xml) pero solo UNA
        /// de las dos celdas queda registrada en PortalManager (la posicion
        /// exacta de colocacion/activacion — ver PortalBlockPatch.cs). El
        /// raycast de mira puede impactar cualquiera de las dos celdas
        /// visibles, asi que se prueban las tres posiciones candidatas (la
        /// propia, una celda abajo y una arriba) — mismo patron que ya usa
        /// CheckPlayerPortalCollision en PortalTeleport.cs con pies/cabeza
        /// del jugador.
        /// </summary>
        private static bool TryResolveHoveredPortal(Vector3i hitBlockPos, out Vector3i portalPos, out PortalManager.PortalRef portalRef)
        {
            if (PortalManager.Instance.TryGetPortalRef(hitBlockPos, out portalRef))
            {
                portalPos = hitBlockPos;
                return true;
            }

            var below = hitBlockPos + new Vector3i(0, -1, 0);
            if (PortalManager.Instance.TryGetPortalRef(below, out portalRef))
            {
                portalPos = below;
                return true;
            }

            var above = hitBlockPos + new Vector3i(0, 1, 0);
            if (PortalManager.Instance.TryGetPortalRef(above, out portalRef))
            {
                portalPos = above;
                return true;
            }

            portalPos = default(Vector3i);
            return false;
        }

        private static void SpawnFloatingTag(Vector3i blockPos, string tag, bool linked)
        {
            // Un poco por encima del marco de 2 bloques de alto, para que el
            // texto no se solape con el modelo.
            var worldPos = new Vector3(blockPos.x + 0.5f, blockPos.y + 2.3f, blockPos.z + 0.5f);
            var color = linked ? new Color(0.55f, 0.65f, 1f) : new Color(0.75f, 0.75f, 0.75f);

            // Velocidad suave hacia arriba (mismo patron que el juego usa
            // para los numeros de dano, solo que mas lento/sutil ya que este
            // texto es informativo, no de combate).
            DamageText.Create(tag, color, worldPos, new Vector3(0f, 0.3f, 0f), 0.6f);
        }
    }
}
