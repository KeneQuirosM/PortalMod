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

            // FIX real (el teletransporte debia sentirse instantaneo, estilo
            // Valheim — pedido explicito: "no delays, no buffs, no 'please
            // wait'"): antes se bloqueaba el viaje con un mensaje si
            // World.IsChunkAreaLoaded fallaba en el destino. Decompilando el
            // propio mecanismo de teletransporte del juego
            // (EntityPlayer.Teleport, usado por EntityPlayerLocal.
            // TeleportToPosition para respawn/cama/etc.) se confirmo que
            // VANILLA NO espera a que el chunk este cargado en absoluto: solo
            // llama SetPosition(_pos) de inmediato y dispara
            // Respawn(RespawnType.Teleport) — el streaming de chunks
            // alrededor del jugador se encarga solo, despues, del pop-in
            // (TeleportToPosition incluso relanza una correccion de altura
            // en un coroutine que espera a "Spawned", no a que el chunk este
            // cargado). No existe ningun "RequestQueuedChunk" en el
            // Assembly-CSharp.dll real (se confirmo por reflection — 0
            // coincidencias); el propio juego resuelve esto sin ninguna
            // espera sincronica. Se elimina el bloqueo aca para igualar ese
            // comportamiento: el viaje ya no se cancela por chunk sin
            // cargar, se ejecuta siempre.
            //
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

            PortalManager.Instance.SetCooldown(steamId);
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
        // Se escanea DESCENDENTE desde el portal (nunca ascendente, para no
        // terminar nunca arriba de una estructura) buscando el primer Y
        // donde CanPlayersSpawnAtPos ya de por si acepta la posicion.
        private static Vector3i FindLandingBlockPos(Vector3i portalPos)
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null)
            {
                return portalPos;
            }

            const int maxScanDown = 32;
            for (var dy = 0; dy >= -maxScanDown; dy--)
            {
                var candidate = portalPos + new Vector3i(0, dy, 0);
                // _bAllowToSpawnOnAirPos: true, igual que usa el propio
                // PlayerMoveController.updateRespawn/TryAddRecoveryPosition
                // al buscar una posicion de rescate.
                if (world.CanPlayersSpawnAtPos(candidate.ToVector3(), true))
                {
                    return candidate;
                }
            }

            // No se encontro espacio valido en el rango escaneado: quedarse
            // con la posicion original del portal (mismo comportamiento que
            // antes de este fix) en vez de arriesgarse a subir por encima de
            // una estructura.
            return portalPos;
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
