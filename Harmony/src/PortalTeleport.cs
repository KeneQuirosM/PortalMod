using System.Collections.Generic;
using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Deteccion de colision jugador &lt;-&gt; portalBlock y ejecucion del
    /// teletransporte. En lugar de depender de un trigger de fisica dedicado
    /// (que requeriria un collider custom en el prefab del bloque, no
    /// garantizado en todas las instalaciones), se hace un chequeo ligero por
    /// tick sobre la posicion de cada jugador contra el set de posiciones de
    /// portal conocidas (ver PortalManager.GetAllPortalPositions). Con pocos
    /// portales activos este approach es trivialmente barato.
    ///
    /// TODO: verificar en Assembly-CSharp V3.0 si Block/BlockValue expone un
    /// evento de "entidad entro en el volumen del bloque" (por ejemplo via
    /// BlockTrigger, TileEntityTrigger o similar) que permita reemplazar este
    /// polling por un callback nativo mas eficiente.
    /// </summary>
    public static class PortalTeleport
    {
        // Ultimo mensaje de "portal huerfano" mostrado por jugador+tag, para no
        // spamear el HUD mientras el jugador permanece parado sobre el bloque.
        private static readonly Dictionary<string, float> _lastOrphanMessageTime = new Dictionary<string, float>();
        private const float OrphanMessageThrottleSeconds = 3f;

        public static void Init()
        {
            API.Log("PortalTeleport inicializado.");
        }

        /// <summary>Se invoca una vez por tick de juego desde API.OnGameUpdate.</summary>
        public static void Tick()
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null)
            {
                return;
            }

            // TODO: verificar en Assembly-CSharp V3.0 el metodo correcto para
            // enumerar jugadores activos en el server/host. Candidatos conocidos
            // en builds anteriores: World.Players.list, World.GetPlayers(),
            // GameManager.Instance.World.Players.dict.Values.
            var players = world.Players?.list;
            if (players == null)
            {
                return;
            }

            foreach (var entity in players)
            {
                var player = entity as EntityPlayer;
                if (player == null || !player.IsAlive())
                {
                    continue;
                }

                CheckPlayerPortalCollision(player);
            }
        }

        private static void CheckPlayerPortalCollision(EntityPlayer player)
        {
            var steamId = PortalIdentity.GetSteamId(player);
            if (string.IsNullOrEmpty(steamId))
            {
                return;
            }

            // Un jugador recien teletransportado no puede volver a activar un
            // portal hasta que expire el cooldown (regla 7 — evita loops).
            if (PortalManager.Instance.IsOnCooldown(steamId))
            {
                return;
            }

            var playerBlockPos = new Vector3i(
                Mathf.FloorToInt(player.position.x),
                Mathf.FloorToInt(player.position.y),
                Mathf.FloorToInt(player.position.z));

            // El portal ocupa 1x2x1 (ver MultiBlockDim en blocks.xml): revisar
            // tanto la celda de los pies como la de la cabeza del jugador.
            var feet = playerBlockPos;
            var head = playerBlockPos + new Vector3i(0, 1, 0);

            if (TryResolvePortalAt(feet, out var portalRef) || TryResolvePortalAt(head, out portalRef))
            {
                if (portalRef.SteamId != steamId)
                {
                    // Los portales de otros jugadores no interactuan (cada
                    // jugador gestiona su propio set de portales).
                    return;
                }

                TryTeleport(player, steamId, portalRef.Tag, feet);
            }
        }

        private static bool TryResolvePortalAt(Vector3i pos, out PortalManager.PortalRef portalRef)
        {
            return PortalManager.Instance.TryGetPortalRef(pos, out portalRef);
        }

        private static void TryTeleport(EntityPlayer player, string steamId, string tag, Vector3i originPos)
        {
            if (!PortalManager.Instance.TryGetDestination(steamId, tag, originPos, out var destinationBlockPos))
            {
                // Regla 6: portal huerfano — no ocurre teletransporte.
                ShowOrphanMessageThrottled(player, steamId, tag);
                return;
            }

            ExecuteTeleport(player, steamId, destinationBlockPos);
        }

        private static void ExecuteTeleport(EntityPlayer player, string steamId, Vector3i destinationBlockPos)
        {
            // Centrar al jugador frente al portal destino, con un pequeno offset
            // vertical para no aparecer incrustado en el bloque.
            var destination = new Vector3(destinationBlockPos.x + 0.5f, destinationBlockPos.y + 0.1f, destinationBlockPos.z + 0.5f);

            // TODO: verificar en Assembly-CSharp V3.0 la API correcta de
            // teletransporte para EntityPlayer en servidor dedicado / multijugador.
            // Candidatos conocidos de builds anteriores:
            //   - player.SetPosition(Vector3, bool _bResetSpeed = true)  (cliente local)
            //   - GameUtils.TeleportPlayer(ClientInfo _cInfo, Vector3 _pos, Vector3 _rot, World _world)
            //   - EntityPlayerLocal.TeleportToPosition(...)
            // Se usa SetPosition como fallback seguro porque existe en EntityAlive
            // desde builds tempranas; en dedicado puede requerir enviar tambien
            // un NetPackage al cliente para sincronizar camara/posicion visual.
            player.SetPosition(destination, true);

            PortalManager.Instance.SetCooldown(steamId);
            ApplyTravelBuff(player);

            API.Log($"Teletransporte ejecutado: steamId={steamId} -> {destinationBlockPos}");
        }

        private static void ApplyTravelBuff(EntityPlayer player)
        {
            // TODO: verificar en Assembly-CSharp V3.0 la firma exacta de
            // Buffs.AddBuff; en builds anteriores acepta el nombre del buff y,
            // opcionalmente, el entityId de la fuente.
            player.Buffs?.AddBuff("buffPortalTravel");
        }

        private static void ShowOrphanMessageThrottled(EntityPlayer player, string steamId, string tag)
        {
            var key = steamId + "|" + tag;
            var now = Time.time;

            if (_lastOrphanMessageTime.TryGetValue(key, out var last) && now - last < OrphanMessageThrottleSeconds)
            {
                return;
            }

            _lastOrphanMessageTime[key] = now;
            PortalHud.ShowOrphanMessage(player, tag);
        }
    }
}
