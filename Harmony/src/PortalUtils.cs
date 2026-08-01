using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Utilidades pequenas compartidas entre PortalTeleport, PortalBlockPatch y
    /// XUiPortalTag: resolucion de identidad de jugador (steamId) y mensajes de
    /// HUD localizados. Separado en su propio archivo para no duplicar logica
    /// entre los patches de Harmony y el sistema de teletransporte.
    /// </summary>
    internal static class PortalIdentity
    {
        // FIX real (Bug reportado — "pierdo la propiedad de mis portales cada
        // vez que me desconecto, tengo que destruirlos y volver a colocarlos",
        // y en servidores dedicados "los portales se pierden al reconectarse"):
        // el fallback (entityId.ToString(), ver TRADEOFF mas abajo) es
        // exactamente la causa. entityId NO es estable entre reconexiones — el
        // mismo jugador fisico recibe un entityId distinto cada vez que se
        // conecta, y PortalManager usa ese valor como parte de la clave bajo la
        // que estan registrados sus portales (ver GetPortalKey). Al
        // reconectarse, el jugador resuelve una ownerKey nueva que nunca
        // coincide con la que quedo guardada en portals.dat — sus portales
        // siguen ahi (el bloque fisico existe) pero quedan huerfanos/
        // inaccesibles para el, indistinguible en el juego de "perdi la
        // propiedad".
        //
        // CONFIRMADO CONTRA Assembly-CSharp.dll REAL (analisis de causa raiz,
        // ver TESTING.md): la version anterior de este archivo buscaba un
        // identificador de plataforma estable como propiedad/campo DIRECTO de
        // EntityPlayer (candidatos "PlatformUserIdentifierAbs", "PlatformId",
        // "CrossplatformId", "UserIdentifier", "SteamId", "steamID"). Se
        // inspecciono la jerarquia real de EntityPlayer/EntityPlayerLocal/
        // EntityAlive/Entity en el DLL instalado: NINGUNO de esos candidatos
        // (ni ningun otro miembro relacionado con identidad de plataforma)
        // existe ahi — la busqueda entera apuntaba al objeto EQUIVOCADO.
        //
        // El dato real vive en un objeto COMPLETAMENTE DISTINTO, "ClientInfo"
        // (uno por conexion de red activa, mantenido por ConnectionManager —
        // el mismo objeto que el juego ya usa internamente para
        // autenticacion/anti-cheat/networking):
        //   ConnectionManager.Instance.Clients.ForEntityId(int entityId)
        //     -> ClientInfo (null si no hay conexion de red para ese entityId)
        //   ClientInfo.CrossplatformId / ClientInfo.PlatformId
        //     -> PlatformUserIdentifierAbs (puede ser null)
        //   PlatformUserIdentifierAbs.CombinedString -> string real y estable
        // Se prefiere CrossplatformId (el ID unificado del sistema de
        // crossplay EOS de V3.0, igual sin importar la plataforma nativa) y
        // se cae a PlatformId si el primero no resuelve (por ejemplo un
        // servidor Steam-only sin crossplay habilitado).
        //
        // Como la busqueda original NUNCA miraba en el lugar correcto, este
        // mecanismo jamas resolvia nada, ni una sola vez, para ningun
        // jugador — TODO steamId terminaba siendo SIEMPRE
        // entityId.ToString(), inestable entre reconexiones. Esto explica el
        // bug reportado de forma COMPLETA: no es un caso raro sin cubrir, es
        // que el fix nunca funciono en absoluto desde que se agrego.
        //
        // "ClientInfo" solo existe para conexiones de red reales (dedicado, o
        // el propio host jugando en un mundo con red activa) — en un mundo
        // verdaderamente offline/singleplayer sin ConnectionManager activo
        // puede no haber entrada para el jugador local, por eso se preserva
        // el fallback a entityId.ToString() (ver TRADEOFF), ahora solo
        // alcanzado en ese caso realmente sin red, no en todo multiplayer
        // como pasaba antes.
        //
        // Ya no hace falta ninguna capa de reflection (ni sus caches de
        // Type/MemberInfo): con los miembros reales confirmados se llama
        // directo, mas simple y sin el costo de reflection en un metodo que
        // corre SIN throttle en cada frame por jugador.

        // Una vez resuelto con exito un identificador estable para un
        // entityId dado, se cachea para el resto de la sesion de ese jugador:
        // GetSteamId se llama sin throttle en cada frame por jugador (ver
        // PortalTeleport.CheckPlayerPortalCollision), asi que repetir la
        // lectura por reflection en cada llamada (aun con el MemberInfo ya
        // cacheado por Type) seria costo innecesario para un valor que no
        // puede cambiar mientras ese entityId siga conectado. NO se cachea un
        // resultado nulo/no resuelto a proposito: el dato de identidad de
        // plataforma del jugador podria no estar sincronizado todavia justo al
        // conectarse (mismo caveat que PortalParty sobre datos que tardan en
        // poblarse) — reintentar en llamadas siguientes es barato (una lectura
        // de propiedad ya resuelta) y permite que el fix "prenda" apenas el
        // dato este disponible, sin esperar a una reconexion.
        private static readonly Dictionary<int, string> _resolvedStableIdByEntityId = new Dictionary<int, string>();

        // FIX real (Bug reportado — "los miembros de mi party no pueden usar
        // mis portales"): registra que entityIds llegaron a devolver el
        // fallback (entityId.ToString()) en ALGUNA llamada anterior a que el
        // identificador estable resolviera. Necesario para distinguir, en el
        // momento en que TryResolveStablePlatformId por fin tiene exito, dos
        // casos MUY distintos:
        //   a) Primera llamada de la sesion para este entityId y YA resuelve
        //      estable (dato de plataforma disponible desde el principio) —
        //      nunca se devolvio el fallback, no hay nada que reasignar.
        //   b) Se devolvio el fallback en llamadas previas (dato de
        //      plataforma todavia no sincronizado en ese momento — ver
        //      comentario de _resolvedStableIdByEntityId) y AHORA recien
        //      resuelve — esto es un "cruce" real: cualquier registro de
        //      PortalManager ya hecho bajo el fallback viejo (por ejemplo
        //      un portal que el jugador coloco en esos primeros segundos)
        //      queda huerfano de su identidad real a menos que se reasigne
        //      explicitamente (ver PortalManager.ReassignSteamId).
        private static readonly HashSet<int> _entityIdsThatUsedFallback = new HashSet<int>();

        /// <summary>
        /// Devuelve un identificador unico por jugador usado como clave primaria
        /// en PortalManager para separar los portales de cada jugador. Intenta
        /// primero un identificador de plataforma estable entre sesiones (ver
        /// TryResolveStablePlatformId); si no se puede resolver, cae al
        /// entityId.ToString() de siempre (ver TRADEOFF documentado ahi). Si el
        /// identificador estable resuelve DESPUES de haber devuelto el
        /// fallback para este mismo jugador en esta sesion (ver FIX real de
        /// _entityIdsThatUsedFallback), dispara PortalManager.ReassignSteamId
        /// para que cualquier estado ya registrado bajo el fallback viejo
        /// (portales, atribucion de dueño original, cooldown, cache de party)
        /// se mueva al identificador real en vez de quedar huerfano.
        /// </summary>
        public static string GetSteamId(EntityPlayer player)
        {
            if (player == null)
            {
                return null;
            }

            if (_resolvedStableIdByEntityId.TryGetValue(player.entityId, out var cachedStableId))
            {
                return cachedStableId;
            }

            var stableId = TryResolveStablePlatformId(player);
            if (!string.IsNullOrEmpty(stableId))
            {
                _resolvedStableIdByEntityId[player.entityId] = stableId;

                if (_entityIdsThatUsedFallback.Remove(player.entityId))
                {
                    var oldFallbackId = player.entityId.ToString();
                    API.Log($"[PortalMod] PortalIdentity: id de plataforma estable resuelto para entityId={player.entityId} despues de haber usado el fallback ({oldFallbackId} -> {stableId}); reasignando estado en PortalManager.");
                    PortalManager.Instance.ReassignSteamId(oldFallbackId, stableId);
                }

                return stableId;
            }

            // TRADEOFF (fallback, ver FIX real de la clase arriba): a
            // diferencia de un identificador de plataforma real, entityId NO
            // es estable entre reconexiones/sesiones — el mismo jugador puede
            // recibir un entityId distinto la proxima vez que se conecte, lo
            // que rompe la asociacion de sus portales guardados en
            // PortalManager. Solo se llega aca si ningun candidato de
            // TryResolveStablePlatformId resolvio nada (todavia, o nunca).
            // Se registra el entityId como "uso el fallback" para poder
            // detectar el cruce arriba si mas adelante SI llega a resolver.
            _entityIdsThatUsedFallback.Add(player.entityId);
            return player.entityId.ToString();
        }

        /// <summary>
        /// Resuelve el identificador de plataforma estable (Steam64/EOS) del
        /// jugador via ConnectionManager.Instance.Clients — ver comentario
        /// extenso de la clase sobre por que este es el camino real (y por
        /// que buscarlo en EntityPlayer, como hacia la version anterior,
        /// nunca podia funcionar). Devuelve null si no hay una conexion de
        /// red activa para este entityId (por ejemplo, mundo offline real)
        /// o si ninguno de los dos identificadores resuelve — ambos casos
        /// son best-effort, nunca deben tumbar el resto del mod.
        /// </summary>
        private static string TryResolveStablePlatformId(EntityPlayer player)
        {
            try
            {
                var connectionManager = ConnectionManager.Instance;
                var clientInfo = connectionManager != null
                    ? connectionManager.Clients?.ForEntityId(player.entityId)
                    : null;

                if (clientInfo == null)
                {
                    return null;
                }

                // Prefijo "plat:" a proposito: distingue en portals.dat/logs un
                // ownerKey resuelto por este camino nuevo de un entityId crudo
                // (fallback) o de un ID de party ("party:", ver
                // PortalManager.PartyKeyPrefix) — nunca colisiona con ninguno
                // de los dos formatos anteriores.
                var platformId = clientInfo.CrossplatformId ?? clientInfo.PlatformId;
                var combined = platformId?.CombinedString;

                return string.IsNullOrWhiteSpace(combined) ? null : "plat:" + combined;
            }
            catch (Exception e)
            {
                // Puramente best-effort: un fallo aca NUNCA debe impedir que el
                // resto del mod funcione, solo degrada al fallback de entityId
                // (mismo comportamiento que antes de este fix).
                API.LogWarning($"PortalIdentity: fallo resolviendo id de plataforma estable ({e.Message}); se usa entityId como fallback.");
                return null;
            }
        }
    }

    internal static class PortalHud
    {
        // TODO: verificar en Assembly-CSharp V3.0 la API correcta para mostrar
        // mensajes de HUD dirigidos a un jugador especifico. Candidatos de
        // builds anteriores: GameManager.ShowTooltip(EntityPlayerLocal, string),
        // GameManager.Instance.ChatMessageServer(...), o un NetPackage custom
        // (NetPackageGeneralMessageServer) para servidores dedicados.
        public static void ShowMessage(EntityPlayer player, string localizedText)
        {
            var localPlayer = player as EntityPlayerLocal;
            if (localPlayer != null)
            {
                GameManager.ShowTooltip(localPlayer, localizedText);
            }
            else
            {
                API.Log($"[HUD -> {PortalIdentity.GetSteamId(player)}] {localizedText}");
                // TODO: en servidor dedicado, enviar el mensaje al cliente remoto
                // correspondiente via NetPackage en vez de solo loguearlo server-side.
            }
        }

        public static void ShowActiveMessage(EntityPlayer player, string tag)
        {
            ShowMessage(player, string.Format(Localization.Get("portalHudActive"), tag));
        }

        public static void ShowOrphanMessage(EntityPlayer player, string tag)
        {
            ShowMessage(player, string.Format(Localization.Get("portalHudDestinationMissing"), tag));
        }

        public static void ShowTagInUseMessage(EntityPlayer player)
        {
            ShowMessage(player, Localization.Get("portalHudTagInUse"));
        }

        public static void ShowRenamedMessage(EntityPlayer player, string tag)
        {
            ShowMessage(player, string.Format(Localization.Get("portalHudRenamed"), tag));
        }

        public static void ShowEmptyTagMessage(EntityPlayer player)
        {
            ShowMessage(player, Localization.Get("portalHudEmptyTag"));
        }

        public static void ShowCooldownMessage(EntityPlayer player)
        {
            ShowMessage(player, Localization.Get("portalHudCooldown"));
        }

        /// <summary>Tooltip mostrado al apuntar (hover) a un portal — ver PortalHoverFX.</summary>
        public static void ShowTargetMessage(EntityPlayer player, string tag, bool linked)
        {
            ShowMessage(player, string.Format(Localization.Get(linked ? "portalHudTargetLinked" : "portalHudTargetOrphan"), tag));
        }

        /// <summary>Feature "requiere electricidad": mensaje al intentar usar un portal sin energia cerca — ver PortalPower/PortalTeleport.</summary>
        public static void ShowNoPowerMessage(EntityPlayer player)
        {
            ShowMessage(player, Localization.Get("portalNoEnergy"));
        }
    }
}
