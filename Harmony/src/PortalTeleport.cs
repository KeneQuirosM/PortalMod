using System;
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

        // Mismo throttle, pero para el mensaje de "sin energia" (Feature
        // "requiere electricidad").
        private static readonly Dictionary<string, float> _lastNoPowerMessageTime = new Dictionary<string, float>();
        private const float NoPowerMessageThrottleSeconds = 3f;

        // FIX real (Bug 1 — lag desde que se agrego el sistema de party):
        // CheckPlayerPortalCollision corre una vez por FRAME por cada
        // jugador (ver Tick() mas abajo, sin throttle propio), y llamaba a
        // PortalManager.CheckPartyMembershipChanged() en cada una de esas
        // pasadas pese a que el comentario original decia "throttleado
        // internamente" — CheckPartyMembershipChanged en si NO throttlea por
        // tiempo, solo compara claves ya resueltas. La resolucion de esa
        // clave (GetPortalKey -> PortalParty.TryGetPartyId) puede terminar en
        // PortalParty.TryGetPartyIdFromStaticManager, que hace
        // AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetTypes())
        // — reflejar TODOS los tipos de TODOS los ensamblados cargados del
        // proceso — por cada uno de sus 3 candidatos de nombre. Repetir eso
        // en cada frame por cada jugador conectado es exactamente el tipo de
        // costo que puede generar el lag reportado. Se agrega aqui el mismo
        // patron de throttle por steamId que ya usan los mensajes de arriba
        // (ver tambien el cacheo de resultados dentro de PortalParty.cs, que
        // ataca la otra mitad del problema: el costo de CADA llamada
        // individual, no solo su frecuencia).
        private static readonly Dictionary<string, float> _lastPartyCheckTime = new Dictionary<string, float>();
        private const float PartyCheckThrottleSeconds = 5f;

        public static void Init()
        {
            API.Log("PortalTeleport inicializado.");
        }

        /// <summary>Se invoca una vez por frame desde GameManager_Update_Patch (ver API.cs).</summary>
        public static void Tick()
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null)
            {
                return;
            }

            // AUDITORIA (manejo de errores): cada sub-sistema se aisla en su
            // propio try/catch. API.cs ya tiene una red de seguridad
            // englobando TODO Tick(), pero sin aislamiento aqui adentro una
            // excepcion en, por ejemplo, PortalHoverFX.Tick() (client-only,
            // toca camara/UI) abortaria tambien el pulso ambiental de
            // PortalVisualFX y el chequeo de colision de TODOS los
            // jugadores para ese frame. Con el try/catch por sub-sistema,
            // una falla en uno no le quita el tick a los demas.
            try
            {
                // Pulso de luz + particulas ambientales de todos los portales
                // conocidos (auto-throttleado internamente, ver PortalVisualFX).
                PortalVisualFX.AmbientTick();
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion en PortalVisualFX.AmbientTick(): {e}");
            }

            try
            {
                // Tooltip + texto flotante al apuntar a un portal con la mira
                // (auto-throttleado internamente, ver PortalHoverFX). Solo afecta
                // al jugador local de este cliente, no a la lista de jugadores.
                PortalHoverFX.Tick(world);
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion en PortalHoverFX.Tick(): {e}");
            }

            // Autoguardado periodico (ver PortalManager.MaybeAutoSave): acota
            // la ventana de perdida de datos ante un crash duro del proceso,
            // ya que OnApplicationQuit nunca se ejecuta en ese caso. Save()
            // ya tiene try/catch propio.
            PortalManager.Instance.MaybeAutoSave();

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

                // Aislado por jugador: si un jugador puntual tiene datos raros
                // (por ejemplo una posicion invalida) que hacen fallar el
                // chequeo, el resto de los jugadores igual se revisan este frame.
                try
                {
                    CheckPlayerPortalCollision(player);
                }
                catch (Exception e)
                {
                    API.LogError($"Excepcion revisando colision de portal para steamId={PortalIdentity.GetSteamId(player)}: {e}");
                }
            }
        }

        private static void CheckPlayerPortalCollision(EntityPlayer player)
        {
            var steamId = PortalIdentity.GetSteamId(player);
            if (string.IsNullOrEmpty(steamId))
            {
                return;
            }

            // Feature "portales por party": detecta si la party del jugador
            // cambio desde la ultima revision y migra sus portales
            // automaticamente si hace falta. Se llama ANTES del chequeo de
            // cooldown a proposito, para que la migracion siga funcionando
            // aunque el jugador este en cooldown.
            //
            // FIX real (Bug 1 — lag, ver comentario en _lastPartyCheckTime
            // arriba): CheckPartyMembershipChanged NO throttlea por tiempo
            // internamente pese a lo que decia este comentario antes — el
            // throttle real se agrega aca, con el mismo patron que
            // _lastOrphanMessageTime/_lastNoPowerMessageTime.
            if (!_lastPartyCheckTime.TryGetValue(steamId, out var lastPartyCheck) ||
                Time.time - lastPartyCheck >= PartyCheckThrottleSeconds)
            {
                _lastPartyCheckTime[steamId] = Time.time;
                PortalManager.Instance.CheckPartyMembershipChanged(player);
            }

            // Un jugador recien teletransportado no puede volver a activar un
            // portal hasta que expire el cooldown (regla 7 — evita loops).
            // El cooldown es SIEMPRE por steamId individual, nunca por party
            // (ver comentario en PortalManager._cooldowns) — dos miembros de
            // la misma party pueden viajar cada uno con su propio cooldown
            // independiente.
            if (PortalManager.Instance.IsOnCooldown(steamId))
            {
                return;
            }

            // "ownerKey": steamId personal o "party:<id>" segun corresponda
            // (ver PortalManager.GetPortalKey) — es la clave real bajo la que
            // estan registrados los portales que este jugador puede usar,
            // reemplaza el uso directo de "steamId" para todo lo relacionado
            // con DUEÑO/PROPIEDAD del portal (Feature "portales por party").
            var ownerKey = PortalManager.Instance.GetPortalKey(player);
            if (string.IsNullOrEmpty(ownerKey))
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
                if (portalRef.OwnerKey != ownerKey)
                {
                    // Portal de otro jugador/party: no interactuan entre si
                    // (regla original) — con la Feature de party, esto ahora
                    // compara "ownerKey" (party o personal), no el steamId
                    // crudo, para que los companeros de party SI puedan
                    // usarse entre si los portales de cualquier miembro.
                    return;
                }

                TryTeleport(player, steamId, ownerKey, portalRef.Tag, feet);
            }
        }

        private static bool TryResolvePortalAt(Vector3i pos, out PortalManager.PortalRef portalRef)
        {
            return PortalManager.Instance.TryGetPortalRef(pos, out portalRef);
        }

        private static void TryTeleport(EntityPlayer player, string steamId, string ownerKey, string tag, Vector3i originPos)
        {
            if (!PortalManager.Instance.TryGetDestination(ownerKey, tag, originPos, out var destinationBlockPos))
            {
                // Regla 6: portal huerfano — no ocurre teletransporte.
                ShowOrphanMessageThrottled(player, steamId, tag);
                return;
            }

            // AUDITORIA (chunk destino no cargado): un portal registrado
            // puede estar en un chunk que el servidor/cliente ya descargo
            // por distancia (streaming de chunks) — PortalManager.Load() ya
            // asume esto como posible (ver PortalBlockCheck.Unknown) y NO
            // descarta portales en chunks sin cargar. Teletransportar a
            // ciegas a una posicion sin chunk cargado puede dejar al jugador
            // cayendo en un area sin terreno/colisiones generadas todavia.
            // World.IsChunkAreaLoaded ya esta confirmada y en uso real en
            // PortalManager.CheckPortalBlockAt — se reutiliza aqui como
            // chequeo previo al viaje.
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null || !world.IsChunkAreaLoaded(destinationBlockPos.x, destinationBlockPos.y, destinationBlockPos.z))
            {
                PortalHud.ShowDestinationNotLoadedMessage(player);
                return;
            }

            // Feature "requiere electricidad": el portal de ORIGEN (el que el
            // jugador esta pisando/activando) necesita estar realmente
            // cableado a una fuente de energia encendida — portalBlock es
            // Class="Powered" en blocks.xml (ver FIX real ahi) y
            // PortalPower.HasNearbyPower lee el TileEntityPowered.IsPowered
            // real de esa posicion (ver PortalPower.cs). No se exige energia
            // tambien en el DESTINO: alcanza con que el portal que el
            // jugador esta usando activamente tenga energia para iniciar el
            // viaje.
            if (!PortalPower.HasNearbyPower(originPos))
            {
                ShowNoPowerMessageThrottled(player, steamId, tag);
                return;
            }

            ExecuteTeleport(player, steamId, originPos, destinationBlockPos);
        }

        private static void ExecuteTeleport(EntityPlayer player, string steamId, Vector3i originBlockPos, Vector3i destinationBlockPos)
        {
            // Flash + rafaga de particulas en el portal de ORIGEN, antes de mover
            // al jugador: una vez teletransportado ya no queda nadie ahi para
            // disparar el efecto via buff, asi que se hace explicitamente aqui.
            PortalVisualFX.SpawnTeleportBurst(originBlockPos);

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

            // Rafaga de particulas en el DESTINO, ademas de la que dispara
            // buffPortalTravel (onSelfBuffStart en buffs.xml) al aplicarse el
            // buff justo debajo: refuerza el "efecto explosivo breve" pedido
            // tanto en origen como en destino.
            PortalVisualFX.SpawnTeleportBurst(destinationBlockPos);

            PortalManager.Instance.SetCooldown(steamId);
            ApplyTravelBuff(player);

            API.Log($"Teletransporte ejecutado: steamId={steamId} -> {destinationBlockPos}");
        }

        private static void ApplyTravelBuff(EntityPlayer player)
        {
            // Firma real confirmada por reflection/decompile contra el
            // Assembly-CSharp.dll instalado: EntityBuffs.AddBuff(string _name,
            // int _instigatorId = -1, bool _netSync = true, bool
            // _fromElectrical = false, float _buffDuration = -1f). El default
            // "_buffDuration = -1f" significa "usar la duracion propia del
            // buff definida en buffs.xml" — exactamente lo que se quiere aqui,
            // asi que esta llamada con solo el nombre ya es correcta. No hace
            // falta (ni existe) un RemoveBuff manual despues del
            // teletransporte: el buff se quita solo al expirar su duracion.
            // El bug real de "el jugador queda lento para siempre" era que
            // buffPortalTravel en buffs.xml nunca tenia una duracion real
            // configurada (ver FIX real ahi) — con eso corregido, AddBuff
            // usa los 2s reales del buff y las passive_effect de velocidad se
            // revierten solas al expirar, sin necesidad de tocar este metodo.
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

        private static void ShowNoPowerMessageThrottled(EntityPlayer player, string steamId, string tag)
        {
            var key = steamId + "|" + tag;
            var now = Time.time;

            if (_lastNoPowerMessageTime.TryGetValue(key, out var last) && now - last < NoPowerMessageThrottleSeconds)
            {
                return;
            }

            _lastNoPowerMessageTime[key] = now;
            PortalHud.ShowNoPowerMessage(player);
        }
    }
}
