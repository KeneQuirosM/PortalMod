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
        // bug reportado.
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
            // Flash + rafaga de particulas en el portal de ORIGEN, antes de mover
            // al jugador: una vez teletransportado ya no queda nadie ahi para
            // disparar el efecto via buff, asi que se hace explicitamente aqui.
            PortalVisualFX.SpawnTeleportBurst(originBlockPos);

            // Centrar al jugador frente al portal destino, con un pequeno offset
            // vertical para no aparecer incrustado en el bloque. FindLandingBlockPos
            // (ver mas abajo) corrige la celda Y si hace falta (techo/estructura
            // construida sobre el portal, ver FIX real ahi) — en el caso normal
            // devuelve destinationBlockPos sin cambios.
            var landingPos = FindLandingBlockPos(destinationBlockPos);
            var destination = new Vector3(landingPos.x + 0.5f, landingPos.y + 0.1f, landingPos.z + 0.5f);

            // FIX real (usar la MISMA API que usa el juego para su propio
            // teletransporte, en vez de SetPosition a mano): confirmado por
            // decompilacion que EntityPlayer.Teleport(Vector3 _pos, float
            // _dir = float.MinValue) es el metodo real y generico (existe en
            // la clase base EntityPlayer, no solo en EntityPlayerLocal —
            // funciona tanto en cliente como en servidor dedicado) que usa
            // el propio juego para todo teletransporte real (respawn, cama,
            // etc. pasan por aca via EntityPlayerLocal.TeleportToPosition).
            // Su cuerpo real es "SetPosition(_pos); ...;
            // Respawn(RespawnType.Teleport)" — hace lo mismo que
            // SetPosition pero ademas dispara Respawn(RespawnType.Teleport),
            // la misma señal que usa el juego para asentar al jugador
            // despues de moverlo (limpieza de estado asociada, igual que en
            // cualquier teletransporte vanilla). _dir se deja en su default
            // (no cambiar la rotacion de camara del jugador al llegar, igual
            // que el SetPosition anterior).
            player.Teleport(destination);

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
        // construida sobre el portal destino, en vez de dentro de el): la
        // posicion de aterrizaje se calculaba siempre en destinationBlockPos.y
        // sin verificar nada, confiando en que esa celda (el propio marco del
        // portal, MultiBlockDim="1,2,1") estuviera libre. Si algo raro deja esa
        // celda bloqueada, la correccion debe buscar espacio DESCENDIENDO
        // desde el portal — nunca ascendiendo, que es lo que terminaba
        // dejando al jugador arriba de cualquier techo/piso construido
        // encima. "Pasable" usa la MISMA condicion real que usa el propio
        // juego para decidir si una celda puede alojar algo (confirmado
        // decompilando Block.MultiBlockArray.AddChilds: "blockValue.isair ||
        // !blockValue.Block.shape.IsTerrain()") — el propio portalBlock
        // (Shape="ModelEntity", no "Terrain") ya cuenta como pasable con
        // esto, asi que en el caso normal (nada construido encima ni dentro)
        // esto devuelve destinationBlockPos sin cambios, exactamente el
        // comportamiento de antes de este fix.
        private static Vector3i FindLandingBlockPos(Vector3i portalPos)
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
            // Sin este chequeo, IsPassable de abajo leeria CUALQUIER celda de
            // un chunk sin cargar como "libre" y el scan aceptaria con
            // total confianza una posicion cuyo terreno real (una vez el
            // chunk termine de cargar) puede resultar solido — dejando al
            // jugador incrustado en el instante en que el chunk hace pop-in.
            // Si el chunk del portal destino no esta cargado, no se escanea
            // en absoluto: se devuelve la posicion original del portal sin
            // modificar, exactamente el comportamiento previo a que
            // existiera este scan (mas seguro que aterrizar sobre una
            // lectura no confiable). PortalTeleport.TryTeleport ya intenta
            // evitar llegar a este caso esperando (con limite configurable,
            // ver PortalConfig.MaxChunkWaitSeconds) a que el chunk destino
            // este cargado antes de ejecutar el teletransporte — este chequeo
            // es la red de seguridad final para cuando ese limite se agota
            // igual sin que el chunk haya cargado.
            if (!world.IsChunkAreaLoaded(portalPos.x, portalPos.y, portalPos.z))
            {
                API.LogWarning($"[PortalMod] FindLandingBlockPos: chunk destino en {portalPos} todavia no cargado; se usa la posicion del portal sin escanear (evita aterrizar sobre una lectura de terreno no confiable).");
                return portalPos;
            }

            const int maxScanDown = 32;
            for (var dy = 0; dy >= -maxScanDown; dy--)
            {
                var feetPos = portalPos + new Vector3i(0, dy, 0);
                var headPos = feetPos + new Vector3i(0, 1, 0);
                if (IsPassable(world, feetPos) && IsPassable(world, headPos))
                {
                    return feetPos;
                }
            }

            // No se encontro espacio libre en el rango escaneado: quedarse
            // con la posicion original del portal (mismo comportamiento que
            // antes de este fix) en vez de arriesgarse a subir por encima de
            // una estructura.
            return portalPos;
        }

        private static bool IsPassable(World world, Vector3i pos)
        {
            var blockValue = world.GetBlock(pos);
            return blockValue.isair || !blockValue.Block.shape.IsTerrain();
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
