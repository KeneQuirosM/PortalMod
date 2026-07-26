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

        // FIX real (Bug: doble teletransporte para el mismo jugador en el
        // mismo tick — log real: "Teletransporte ejecutado: steamId=171 ->
        // 178,61,777" seguido inmediatamente de otro a "165,61,778", 1 bloque
        // de diferencia — riesgo de duplicacion de items si algo del juego
        // reacciona al movimiento/posicion dos veces). El cooldown de
        // PortalManager (5s, ver PortalManager._cooldowns) ya se chequea mas
        // abajo, pero solo se aplica DESPUES de que ExecuteTeleport llega
        // hasta el final (SetCooldown se llama ahi) — cualquier ventana de
        // reentrada entre "decidimos teletransportar" y "terminamos de
        // ejecutar" podia dejar pasar una segunda activacion antes de que el
        // cooldown quedara escrito. Se agrega un throttle propio, MAS CORTO
        // (2s) y MAS TEMPRANO (primero que cualquier otro chequeo de este
        // metodo, incluido el de PortalManager), fijado al toque en
        // ExecuteTeleport ANTES de hacer cualquier otra cosa — mismo patron
        // Dictionary&lt;string,float&gt; + Time.time que el resto de esta clase.
        private static readonly Dictionary<string, float> _lastTeleportTime = new Dictionary<string, float>();
        private const float PostTeleportThrottleSeconds = 2f;

        // FIX real (Bug reportado — "cooldown fijo deja al jugador atascado/
        // devuelto si el mundo tarda mas en cargar"): un teletransporte cuyo
        // chunk DESTINO todavia no esta cargado se registra aca en vez de
        // ejecutarse al instante (ver TryTeleport) — ProcessPendingTeleports,
        // llamado cada Tick(), lo completa apenas el chunk termine de cargar
        // o, como limite, cuando venza "Deadline" (PortalConfig.
        // MaxChunkWaitSeconds desde la activacion). Sin mensaje de "espera"
        // visible: en el caso normal (chunk ya cargado) TryTeleport ejecuta
        // directo sin pasar por aca, asi que el viaje se sigue sintiendo
        // instantaneo — esto solo cubre el caso excepcional que causaba el
        // bug reportado. Independiente de "_lastTeleportTime" de arriba: ese
        // throttle cierra la ventana de reentrada alrededor de la EJECUCION
        // real (se fija recien cuando ExecuteTeleport corre, sea al toque o
        // diferido), mientras que el cooldown de PortalManager (ver
        // SetCooldown en TryTeleport) ya cubre el reintento desde el
        // ACTIVACION del portal — ambos mecanismos conviven sin pisarse.
        private sealed class PendingTeleport
        {
            public EntityPlayer Player;
            public Vector3i OriginBlockPos;
            public Vector3i DestinationBlockPos;
            public float Deadline;
        }

        // Clave: steamId (ver PortalIdentity.GetSteamId) — un jugador solo
        // puede tener UN teletransporte pendiente a la vez (el cooldown, ya
        // aplicado en TryTeleport en el momento de encolar, impide activar
        // un segundo portal mientras el primero sigue pendiente).
        private static readonly Dictionary<string, PendingTeleport> _pendingTeleports = new Dictionary<string, PendingTeleport>();

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

            try
            {
                // Feature 1 ("modo fantasma al colocar el portal"): preview
                // semitransparente mientras el jugador local tiene equipado
                // un item de portal (ver PortalPlacementGhost). Aislado en
                // su propio try/catch, igual que PortalHoverFX arriba: es
                // puramente client-side/cosmetico, un fallo aca nunca debe
                // afectar el resto del tick (colision/teletransporte).
                PortalPlacementGhost.Tick(world);
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion en PortalPlacementGhost.Tick(): {e}");
            }

            // Autoguardado periodico (ver PortalManager.MaybeAutoSave): acota
            // la ventana de perdida de datos ante un crash duro del proceso,
            // ya que OnApplicationQuit nunca se ejecuta en ese caso. Save()
            // ya tiene try/catch propio.
            PortalManager.Instance.MaybeAutoSave();

            try
            {
                // Completa los teletransportes que quedaron esperando a que
                // su chunk destino terminara de cargar (ver TryTeleport/
                // PendingTeleport arriba). Independiente del loop de
                // jugadores de abajo: un jugador con un teletransporte
                // pendiente sigue en cooldown, asi que no vuelve a entrar
                // por CheckPlayerPortalCollision mientras tanto.
                ProcessPendingTeleports(world);
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion en PortalTeleport.ProcessPendingTeleports(): {e}");
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

            // FIX real (Bug: doble teletransporte, ver comentario en
            // _lastTeleportTime arriba): chequeo mas temprano y mas estricto
            // que todo lo demas en este metodo, para cerrar cualquier
            // ventana de reentrada.
            if (_lastTeleportTime.TryGetValue(steamId, out var lastTeleport) &&
                Time.time - lastTeleport < PostTeleportThrottleSeconds)
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

            // El cooldown arranca AHORA (activacion del portal), no recien al
            // llegar — sea que el viaje se ejecute instantaneo o quede
            // pendiente esperando el chunk destino (ver mas abajo). Esto es
            // lo que evita que el chequeo de colision del ORIGEN (el jugador
            // sigue fisicamente parado ahi mientras el teletransporte esta
            // pendiente) dispare TryTeleport de nuevo en cada frame — el
            // IsOnCooldown de arriba en CheckPlayerPortalCollision ya lo
            // bloquea. Se movio aca desde ExecuteTeleport a proposito: antes
            // solo se aplicaba DESPUES de moverse, lo que dejaba una ventana
            // sin cooldown durante toda la espera pendiente.
            PortalManager.Instance.SetCooldown(steamId);

            // FIX real (Bug reportado — "cooldown fijo deja al jugador
            // atascado en una pared o lo devuelve al origen si el mundo
            // tarda mas en cargar"): decompilando el propio mecanismo de
            // teletransporte del juego (EntityPlayer.Teleport, usado por
            // EntityPlayerLocal.TeleportToPosition para respawn/cama/etc.) ya
            // se habia confirmado en una sesion anterior que VANILLA NO
            // espera a que el chunk este cargado — llama SetPosition de
            // inmediato y deja que el streaming de chunks se encargue del
            // pop-in despues. Eso sigue siendo cierto y sigue siendo el
            // comportamiento por defecto aca (ver "chunkReady" abajo: el
            // caso comun, chunk ya cargado, ejecuta instantaneo exactamente
            // igual que antes). El problema reportado es OTRO: World.
            // GetBlock() en un chunk sin cargar devuelve BlockValue.Air
            // indistinguible de "aca no hay nada real" (ver FIX real en
            // FindLandingBlockPos), asi que si el chunk destino NO esta
            // cargado, la logica de aterrizaje de este mod (FindLandingBlockPos,
            // que vanilla ni siquiera tiene) puede calcular mal el punto de
            // aparicion — de ahi "atascado en una pared" (aterrizo sobre una
            // lectura de aire que en realidad era terreno solido sin cargar
            // todavia). No existe ningun "RequestQueuedChunk" en el
            // Assembly-CSharp.dll real (confirmado por reflection en la
            // sesion anterior) para forzar la carga, asi que la unica opcion
            // real es ESPERAR (con un limite acotado, configurable via
            // PortalConfig.MaxChunkWaitSeconds, para no colgarse si el chunk
            // jamas llega a cargar) a que World.IsChunkAreaLoaded confirme
            // que ya se puede confiar en una lectura de bloque real ahi,
            // antes de calcular el aterrizaje y mover al jugador — sin
            // mostrar ningun mensaje de "espera" (la espera tipica es
            // imperceptible: el chunk de un portal ya visitado suele estar
            // cargado o cargar en un frame o dos).
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            var chunkReady = world == null || PortalConfig.MaxChunkWaitSeconds <= 0f ||
                world.IsChunkAreaLoaded(destinationBlockPos.x, destinationBlockPos.y, destinationBlockPos.z);

            if (chunkReady)
            {
                ExecuteTeleport(player, steamId, originPos, destinationBlockPos);
                return;
            }

            _pendingTeleports[steamId] = new PendingTeleport
            {
                Player = player,
                OriginBlockPos = originPos,
                DestinationBlockPos = destinationBlockPos,
                Deadline = Time.time + PortalConfig.MaxChunkWaitSeconds
            };

            API.Log($"[PortalMod] Teletransporte diferido: steamId={steamId} destino={destinationBlockPos} (chunk todavia no cargado; esperando hasta {PortalConfig.MaxChunkWaitSeconds}s antes de ejecutar igual).");
        }

        /// <summary>
        /// Completa los teletransportes encolados por TryTeleport porque su
        /// chunk destino todavia no estaba cargado en el momento de la
        /// activacion — llamado desde Tick() en cada frame. Ejecuta apenas
        /// el chunk termina de cargar, o al vencer "Deadline" (lo que ocurra
        /// primero) para no dejar al jugador esperando indefinidamente si
        /// ese chunk en particular nunca llega a cargar.
        /// </summary>
        private static void ProcessPendingTeleports(World world)
        {
            if (_pendingTeleports.Count == 0)
            {
                return;
            }

            // AUDITORIA (manejo de errores): no se puede remover de
            // _pendingTeleports mientras se enumera — se junta la lista de
            // claves a remover y se aplica despues, mismo patron que
            // PortalManager.Load()/otros loops de este mod que modifican una
            // coleccion en base a lo que encuentran mientras la recorren.
            List<string> toRemove = null;

            foreach (var kvp in _pendingTeleports)
            {
                var pending = kvp.Value;

                try
                {
                    // Jugador se desconecto (o murio) mientras esperaba: no
                    // tiene sentido teletransportar un EntityPlayer ya
                    // invalido — se descarta sin ejecutar.
                    if (pending.Player == null || !pending.Player.IsAlive())
                    {
                        (toRemove ?? (toRemove = new List<string>())).Add(kvp.Key);
                        continue;
                    }

                    var chunkReady = world.IsChunkAreaLoaded(
                        pending.DestinationBlockPos.x, pending.DestinationBlockPos.y, pending.DestinationBlockPos.z);

                    if (!chunkReady && Time.time < pending.Deadline)
                    {
                        // Seguir esperando: seguira aca hasta el proximo Tick().
                        continue;
                    }

                    if (!chunkReady)
                    {
                        API.LogWarning($"[PortalMod] Teletransporte pendiente para steamId={kvp.Key}: se agoto la espera ({PortalConfig.MaxChunkWaitSeconds}s) y el chunk destino {pending.DestinationBlockPos} sigue sin cargar; se ejecuta igual (FindLandingBlockPos usara la posicion del portal sin escanear, ver FIX real ahi).");
                    }

                    ExecuteTeleport(pending.Player, kvp.Key, pending.OriginBlockPos, pending.DestinationBlockPos);
                    (toRemove ?? (toRemove = new List<string>())).Add(kvp.Key);
                }
                catch (Exception e)
                {
                    API.LogError($"Excepcion completando teletransporte pendiente para steamId={kvp.Key}: {e}");
                    (toRemove ?? (toRemove = new List<string>())).Add(kvp.Key);
                }
            }

            if (toRemove != null)
            {
                foreach (var key in toRemove)
                {
                    _pendingTeleports.Remove(key);
                }
            }
        }

        private static void ExecuteTeleport(EntityPlayer player, string steamId, Vector3i originBlockPos, Vector3i destinationBlockPos)
        {
            // FIX real (Bug: doble teletransporte, ver comentario en
            // _lastTeleportTime): se fija ANTES de hacer cualquier otra cosa
            // (particulas, SetPosition, buffs) para cerrar la ventana de
            // reentrada lo mas posible — no al final del metodo.
            _lastTeleportTime[steamId] = Time.time;

            // Flash + rafaga de particulas en el portal de ORIGEN, antes de mover
            // al jugador: una vez teletransportado ya no queda nadie ahi para
            // disparar el efecto via buff, asi que se hace explicitamente aqui.
            PortalVisualFX.SpawnTeleportBurst(originBlockPos);

            // Centrar al jugador frente al portal destino, con un pequeno offset
            // vertical para no aparecer incrustado en el bloque. FindLandingBlockPos
            // (ver mas abajo) corrige la celda Y si hace falta (techo/estructura
            // construida sobre el portal, ver FIX real ahi) — en el caso normal
            // devuelve destinationBlockPos sin cambios.
            // Feature 2 ("indicador de salida" / rotacion real): la
            // rotacion real ya escrita al colocar el portal (ver
            // PortalOrientation.ApplyPlayerFacingRotation, llamada desde
            // Block_OnBlockPlaceBefore_Patch) vive directamente en el
            // BlockValue del bloque en el mundo — no hace falta persistirla
            // aparte en PortalManager (mismo patron que ya usa
            // PortalVisualFX para leer/escribir "rotation": la fuente de
            // verdad es siempre el BlockValue real). Se lee aca para poder
            // preferir la celda "al frente" del portal como aterrizaje (ver
            // FindLandingBlockPos).
            var worldForRotation = GameManager.Instance != null ? GameManager.Instance.World : null;
            var destRotation = worldForRotation != null ? worldForRotation.GetBlock(destinationBlockPos).rotation : PortalOrientation.RotationNorth;
            var landingPos = FindLandingBlockPos(destinationBlockPos, destRotation);
            var destination = new Vector3(landingPos.x + 0.5f, landingPos.y + 0.1f, landingPos.z + 0.5f);

            // FIX real (el jugador seguia apareciendo ENCIMA del techo pese
            // al fix anterior con World.CanPlayersSpawnAtPos): usar
            // player.Teleport(...) (commit "instant portal teleport") fue lo
            // que en realidad REINTRODUJO el bug. Teleport() hace
            // "SetPosition(_pos); ...; Respawn(RespawnType.Teleport)" — y
            // decompilando PlayerMoveController.Respawn(RespawnType) se
            // confirmo que ESE metodo NO reposiciona nada de inmediato, solo
            // fija "respawnReason"/un timer y pone Spawned=false: dispara una
            // maquina de estados que el propio PlayerMoveController sigue
            // procesando en frames POSTERIORES via updateRespawn() — el mismo
            // metodo que, si la posicion no pasa World.CanPlayersSpawnAtPos
            // (ni esa posicion +1), la descarta y reubica al jugador en
            // World.GetHeight(x,z)+1 (ver FIX real de FindLandingBlockPos).
            // O sea: aunque FindLandingBlockPos elija un Y perfectamente
            // valido, Teleport() igual dispara ese rescate unos frames
            // despues, que puede terminar sobreescribiendo la posicion que
            // recien pusimos.
            //
            // NOTA: se investigo el comando de consola "teleport" (para ver
            // si evita este rescate) — decompilando ConsoleCmdTeleportsAbs.
            // ExecuteTeleport -> NetPackageTeleportPlayer.ProcessPackage se
            // confirmo que TAMBIEN llama primaryPlayer.TeleportToPosition(...)
            // (la misma cadena Teleport()/Respawn(RespawnType.Teleport) de
            // arriba) — no es una ruta alternativa que evite el rescate, solo
            // no lo nota en la practica porque normalmente se usa en zonas
            // abiertas. Entity.SetPosition(Vector3, bool) (decompilado: solo
            // actualiza "position"/"boundingBox"/transform de fisica, sin
            // tocar Respawn/CanPlayersSpawnAtPos/rescate de terreno en
            // absoluto) es la API real mas simple que logra evitar
            // completamente esa maquina de estados. Se vuelve a este metodo
            // (el que este archivo usaba antes del commit "instant portal
            // teleport") en vez de Teleport().
            //
            // FIX real (Bug: no se podia teletransportar estando en un
            // vehiculo — esto funcionaba durante la breve ventana en la que
            // este archivo uso player.Teleport(...), y se rompio al volver a
            // SetPosition directo): el cuerpo real decompilado de
            // EntityPlayer.Teleport es "if (AttachedToEntity)
            // AttachedToEntity.SetPosition(_pos); else SetPosition(_pos);" —
            // si el jugador esta AttachedToEntity (sentado/manejando un
            // vehiculo), mover al JUGADOR no sirve de nada porque su
            // posicion visual la controla el vehiculo; hay que mover el
            // vehiculo. Se replica exactamente esa parte (sin el resto del
            // cuerpo de Teleport(), que dispara Respawn() y reintroduce el
            // bug del techo de arriba).
            if (player.AttachedToEntity != null)
            {
                player.AttachedToEntity.SetPosition(destination, true);
            }
            else
            {
                player.SetPosition(destination, true);
            }

            // Rafaga de particulas en el DESTINO, ademas de la que dispara
            // buffPortalTravel (onSelfBuffStart en buffs.xml) al aplicarse el
            // buff justo debajo: refuerza el "efecto explosivo breve" pedido
            // tanto en origen como en destino.
            PortalVisualFX.SpawnTeleportBurst(destinationBlockPos);

            // El cooldown YA se aplico en TryTeleport, en el momento de la
            // activacion (ver comentario ahi) — no volver a aplicarlo aca
            // pisaria con un timestamp MAS TARDE (Time.time de este
            // instante, potencialmente varios segundos despues de la
            // activacion si el teletransporte quedo pendiente esperando el
            // chunk, ver ProcessPendingTeleports), extendiendo el cooldown
            // real mas alla de lo configurado.
            ApplyTravelBuff(player);

            API.Log($"Teletransporte ejecutado: steamId={steamId} -> {destinationBlockPos}");
        }

        // FIX real (el jugador aparecia ENCIMA de un techo/estructura
        // construida sobre el portal destino, en vez de dentro de el):
        // el intento anterior de este fix usaba "blockValue.isair ||
        // !blockValue.Block.shape.IsTerrain()" como chequeo de "celda
        // libre" — ese es el criterio que usa Block.MultiBlockArray.
        // AddChilds para decidir si un multiblock puede alojar un hijo ahi,
        // pero NO significa "un jugador puede pararse aca": decompilando
        // BlockShapeCube.IsTerrain() se confirmo que NO sobreescribe el
        // metodo (hereda el default "false" de BlockShape) — es decir,
        // CUALQUIER pared/piso/techo normal construido por un jugador
        // (Shape="Cube") tambien "pasa" ese chequeo, asi que el intento
        // anterior nunca detectaba un techo real y el escaneo jamas
        // se activaba (siempre devolvia destinationBlockPos sin cambios).
        //
        // La causa real de por que terminaba arriba del techo ademas se
        // confirmo decompilando PlayerMoveController.updateRespawn (parte
        // del propio Respawn(RespawnType.Teleport) que dispara
        // EntityPlayer.Teleport): si la posicion que le pasamos NO pasa
        // World.CanPlayersSpawnAtPos (ni esa posicion +1 en Y), el JUEGO
        // MISMO descarta silenciosamente nuestra posicion y reubica al
        // jugador en "World.GetHeight(x,z) + 1" — la altura de la superficie
        // solida mas alta en esa columna XZ, que en un cuarto cerrado ES el
        // techo. Por eso hace falta usar la MISMA API real que usa esa
        // rescate interno (World.CanPlayersSpawnAtPos — publica, usada
        // tambien por EntityPlayerLocal.TryAddRecoveryPosition para puntos
        // de recuperacion) para elegir un Y que el juego vaya a aceptar tal
        // cual, en vez de adivinar una condicion de "pasable" propia.
        //
        // FIX real (Bug: "1 bloque adelante" del intento anterior podia
        // terminar DENTRO de una pared — "adelante" depende de la rotacion
        // del portal y del layout real del cuarto, ninguno de los dos
        // garantiza que esa celda especifica este libre): se vuelve a la
        // posicion XZ EXACTA del portal — el jugador debe aparecer siempre
        // dentro del propio marco del portal (el "footprint"), nunca
        // desplazado a un costado. Solo se ajusta la celda Y:
        //   1) Primero se prueba EN o apenas ARRIBA de la Y del portal (0,
        //      +1, +2) — cubre el caso normal (portal libre) y el caso de
        //      un piso/objeto que invadio la celda inferior del marco.
        //   2) Si nada de eso pasa CanPlayersSpawnAtPos (techo demasiado
        //      bajo justo arriba del portal), se escanea DESCENDENTE desde
        //      la Y del portal para encontrar el piso real.
        // En ambos pasos se usa CanPlayersSpawnAtPos — la MISMA API real que
        // usa el rescate interno del juego (PlayerMoveController.
        // updateRespawn, ver comentario mas arriba) — para garantizar que la
        // celda elegida ya sea valida ANTES de escribir la posicion: aunque
        // este archivo ya no llama a Teleport()/Respawn() (ver comentario
        // arriba de ExecuteTeleport, se revirtio a SetPosition especificamente
        // para evitar ese rescate), sigue siendo la forma correcta de
        // confirmar "el jugador realmente puede pararse aca" sin adivinar.
        // FEATURE 2 ("el punto de salida debe estar al frente del portal, no
        // adentro del bloque"): antes de este cambio el aterrizaje SIEMPRE
        // usaba la columna XZ exacta del portal (ver todo el historial de
        // bugs documentado arriba sobre por que — sin rotacion real,
        // "adelante" no era confiable, ver PortalOrientation). Ahora que
        // portalBlock respeta la rotacion del jugador al colocarse, se puede
        // calcular con confianza la celda "al frente" (lado opuesto al que
        // se entra) y probarla PRIMERO — relevante en particular para
        // estilos no planos (ej. "cylinder", ver blocks.xml) donde aparecer
        // adentro del propio marco puede dejar al jugador chocando/
        // incrustado contra el modelo al salir. Se prueba la celda al
        // frente SOLO si su chunk esta cargado y pasa
        // World.CanPlayersSpawnAtPos (nunca se asume "libre" a ciegas, mismo
        // criterio que el resto de este metodo) — si cualquiera de las dos
        // falla, se cae al comportamiento ORIGINAL (adentro del marco, ver
        // "TryFindSpawnableColumn" mas abajo), nunca se deja al jugador sin
        // aterrizar.
        private static Vector3i FindLandingBlockPos(Vector3i portalPos, byte rotation)
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null)
            {
                return portalPos;
            }

            // FIX real (Bug reportado — "el jugador queda atascado en una
            // pared" al llegar): World.GetBlock() en una celda de un chunk
            // que TODAVIA no esta cargado devuelve BlockValue.Air —
            // exactamente el mismo valor que "aca de verdad no hay nada"
            // (mismo caveat ya confirmado y documentado para esta API en
            // PortalManager.CheckPortalBlockAt y PortalPower.HasNearbyPower).
            // World.CanPlayersSpawnAtPos (usado por el scan de abajo)
            // termina leyendo ese mismo bloque real — sin este chequeo, el
            // scan podria confiar con total seguridad en una posicion cuyo
            // terreno real (una vez el chunk termine de cargar) resulte
            // solido, dejando al jugador incrustado en el instante en que el
            // chunk hace pop-in. Si el chunk del portal destino no esta
            // cargado, no se escanea en absoluto: se devuelve la posicion
            // original del portal sin modificar (mas seguro que aterrizar
            // sobre una lectura no confiable). PortalTeleport.TryTeleport ya
            // intenta evitar llegar a este caso esperando (con limite
            // configurable, ver PortalConfig.MaxChunkWaitSeconds) a que el
            // chunk destino este cargado antes de ejecutar el teletransporte
            // — este chequeo es la red de seguridad final para cuando ese
            // limite se agota igual sin que el chunk haya cargado.
            if (!world.IsChunkAreaLoaded(portalPos.x, portalPos.y, portalPos.z))
            {
                API.LogWarning($"[PortalMod] FindLandingBlockPos: chunk destino en {portalPos} todavia no cargado; se usa la posicion del portal sin escanear (evita aterrizar sobre una lectura de terreno no confiable).");
                return portalPos;
            }

            var frontColumnBase = portalPos + PortalOrientation.ForwardOffset(rotation);
            if (world.IsChunkAreaLoaded(frontColumnBase.x, frontColumnBase.y, frontColumnBase.z) &&
                TryFindSpawnableColumn(world, frontColumnBase, out var frontLanding))
            {
                return frontLanding;
            }

            if (TryFindSpawnableColumn(world, portalPos, out var insideLanding))
            {
                return insideLanding;
            }

            // No se encontro espacio valido ni al frente ni adentro del
            // marco: quedarse con la posicion original del portal en vez de
            // arriesgarse a subir/bajar demasiado (mismo fallback final que
            // antes de la Feature 2).
            return portalPos;
        }

        /// <summary>
        /// Escanea verticalmente desde "columnBase" (primero hacia arriba,
        /// despues hacia abajo) buscando la primera celda que pase
        /// World.CanPlayersSpawnAtPos — misma logica/limites que usaba
        /// originalmente FindLandingBlockPos antes de la Feature 2, extraida
        /// a su propio metodo para poder probarla tanto en la columna "al
        /// frente" del portal como, de fallback, en la columna original
        /// (adentro del marco).
        /// </summary>
        private static bool TryFindSpawnableColumn(World world, Vector3i columnBase, out Vector3i result)
        {
            const int maxScanUp = 2;
            for (var dy = 0; dy <= maxScanUp; dy++)
            {
                var candidate = columnBase + new Vector3i(0, dy, 0);
                // _bAllowToSpawnOnAirPos: true, igual que usa el propio
                // PlayerMoveController.updateRespawn/TryAddRecoveryPosition
                // al buscar una posicion de rescate.
                if (world.CanPlayersSpawnAtPos(candidate.ToVector3(), true))
                {
                    result = candidate;
                    return true;
                }
            }

            const int maxScanDown = 32;
            for (var dy = -1; dy >= -maxScanDown; dy--)
            {
                var candidate = columnBase + new Vector3i(0, dy, 0);
                if (world.CanPlayersSpawnAtPos(candidate.ToVector3(), true))
                {
                    result = candidate;
                    return true;
                }
            }

            result = default(Vector3i);
            return false;
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
