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
        // vez que me desconecto, tengo que destruirlos y volver a colocarlos"):
        // el fallback original (entityId.ToString(), ver HISTORIAL mas abajo)
        // es exactamente la causa. entityId NO es estable entre reconexiones —
        // el mismo jugador fisico recibe un entityId distinto cada vez que se
        // conecta, y PortalManager usa ese valor como parte de la clave bajo la
        // que estan registrados sus portales (ver GetPortalKey). Al
        // reconectarse, el jugador resuelve una ownerKey nueva que nunca
        // coincide con la que quedo guardada en portals.dat — sus portales
        // siguen ahi (el bloque fisico existe) pero quedan huerfanos/
        // inaccesibles para el, indistinguible en el juego de "perdi la
        // propiedad". Destruir y volver a colocar el bloque los re-registra
        // bajo el entityId de la sesion actual, lo cual "arregla" el sintoma
        // hasta la proxima desconexion — exactamente el comportamiento
        // reportado.
        //
        // FIX: antes de caer al entityId inestable, se intenta resolver por
        // reflection un identificador de plataforma real (Steam64 id, EOS id,
        // o similar) expuesto en la jerarquia de EntityPlayer — el mismo
        // patron ya usado en PortalParty.TryGetPartyId (candidatos de nombre
        // probados en runtime, nunca una referencia directa a un tipo/miembro
        // sin confirmar, para no arriesgar un error de COMPILACION si el
        // candidato no existe en el Assembly-CSharp.dll real de V3.0). Si
        // NINGUN candidato resuelve (por ejemplo si el sistema de identidad de
        // V3.0 usa un nombre distinto a todos los probados), se sigue cayendo
        // al fallback anterior — mismo comportamiento que antes de este fix,
        // nunca peor.
        //
        // HISTORIAL: el intento original de esta clase referenciaba
        // "player.PlatformUserIdentifierAbs" directo — ese miembro NO existe en
        // EntityPlayer del Assembly-CSharp.dll real de V3.0 (error de
        // compilacion confirmado). No se tuvo acceso al DLL en esa sesion para
        // confirmar el reemplazo correcto, asi que se opto por el fallback
        // entityId.ToString(), garantizado a compilar — sin notar en ese
        // momento el impacto en persistencia entre sesiones que documenta el
        // TRADEOFF de mas abajo. Este fix ataca esa causa raiz directamente.
        private static readonly string[] StableIdPropertyCandidates =
        {
            "PlatformUserIdentifierAbs", "PlatformId", "CrossplatformId", "UserIdentifier", "SteamId", "steamID"
        };

        // Candidatos de nombre de miembro DENTRO del objeto devuelto por la
        // propiedad/campo de arriba (si ese valor no es ya un tipo simple como
        // string/long) que contendria el identificador unico real como texto —
        // mismo patron que PortalParty.PartyIdMemberCandidates.
        private static readonly string[] StableIdMemberCandidates =
        {
            "CombinedString", "ReadablePlatformUserId", "ReadableString", "InternalId", "Id", "id"
        };

        // Que propiedad/campo (si alguno) existe en un Type dado no cambia en
        // caliente — se resuelve una unica vez por Type y se reusa (null
        // cacheado = "ningun candidato existe en este Type", tambien valido
        // para no volver a escanear la jerarquia en cada llamada). Mismo
        // patron/motivo que PortalParty._instancePropertyCache.
        private static readonly Dictionary<Type, MemberInfo> _stableIdMemberCache = new Dictionary<Type, MemberInfo>();

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
        /// Intenta resolver por reflection un identificador de plataforma
        /// estable (Steam64/EOS/etc.) para este jugador — ver comentario
        /// extenso de la clase sobre por que reflection y no una referencia
        /// directa. Devuelve null si ningun candidato de nombre existe en esta
        /// version del juego, o si existe pero su valor no se pudo interpretar
        /// como un identificador simple; ambos casos son best-effort, nunca
        /// deben tumbar el resto del mod.
        /// </summary>
        private static string TryResolveStablePlatformId(EntityPlayer player)
        {
            try
            {
                var playerType = player.GetType();
                if (!_stableIdMemberCache.TryGetValue(playerType, out var member))
                {
                    // SafeFindStableIdMemberInHierarchy (no FindStableIdMemberInHierarchy
                    // directo): GetSteamId se llama SIN throttle en cada frame por
                    // jugador (ver comentario de _resolvedStableIdByEntityId arriba).
                    // Si el escaneo de la jerarquia lanzara AmbiguousMatchException
                    // (posible si una clase derivada oculta una propiedad del mismo
                    // nombre con una firma distinta) y esa excepcion escapara hasta
                    // ACA, la linea de abajo que cachea el resultado nunca se
                    // ejecutaria — el proximo frame repetiria el mismo escaneo, la
                    // misma excepcion, y el mismo log de warning del catch exterior,
                    // por siempre. Se garantiza que este Type SIEMPRE quede cacheado
                    // (con un miembro real o con null) despues del primer intento,
                    // sin importar el resultado.
                    member = SafeFindStableIdMemberInHierarchy(playerType);
                    _stableIdMemberCache[playerType] = member;
                }

                if (member == null)
                {
                    return null;
                }

                var rawValue = member is PropertyInfo propInfo
                    ? propInfo.GetValue(player)
                    : ((FieldInfo)member).GetValue(player);

                if (rawValue == null)
                {
                    return null;
                }

                // Prefijo "plat:" a proposito: distingue en portals.dat/logs un
                // ownerKey resuelto por este camino nuevo de un entityId crudo
                // (fallback) o de un ID de party ("party:", ver
                // PortalManager.PartyKeyPrefix) — nunca colisiona con ninguno
                // de los dos formatos anteriores.
                if (IsSimpleIdType(rawValue))
                {
                    var text = rawValue.ToString();
                    return string.IsNullOrWhiteSpace(text) ? null : "plat:" + text;
                }

                var nested = ExtractNestedId(rawValue);
                return nested != null ? "plat:" + nested : null;
            }
            catch (Exception e)
            {
                // Puramente best-effort: un fallo aca NUNCA debe impedir que el
                // resto del mod funcione, solo degrada al fallback de entityId
                // (mismo comportamiento que antes de este fix).
                API.LogWarning($"PortalIdentity: fallo resolviendo id de plataforma estable por reflection ({e.Message}); se usa entityId como fallback.");
                return null;
            }
        }

        private static MemberInfo SafeFindStableIdMemberInHierarchy(Type startType)
        {
            try
            {
                return FindStableIdMemberInHierarchy(startType);
            }
            catch (AmbiguousMatchException)
            {
                // Type.GetProperty(string, BindingFlags) lanza esto si una clase
                // derivada oculta ("new") una propiedad del mismo nombre heredada
                // con una firma distinta — candidato ambiguo, se descarta (null =
                // "ningun candidato usable en este Type", cacheado igual que un
                // candidato inexistente).
                return null;
            }
        }

        private static MemberInfo FindStableIdMemberInHierarchy(Type startType)
        {
            for (var t = startType; t != null; t = t.BaseType)
            {
                foreach (var name in StableIdPropertyCandidates)
                {
                    var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null && prop.GetIndexParameters().Length == 0)
                    {
                        return prop;
                    }

                    var field = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        return field;
                    }
                }
            }

            return null;
        }

        private static string ExtractNestedId(object value)
        {
            var valueType = value.GetType();

            foreach (var memberName in StableIdMemberCandidates)
            {
                var prop = valueType.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    var nestedValue = prop.GetValue(value);
                    if (nestedValue != null && !string.IsNullOrWhiteSpace(nestedValue.ToString()))
                    {
                        return nestedValue.ToString();
                    }
                }

                var field = valueType.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var nestedValue = field.GetValue(value);
                    if (nestedValue != null && !string.IsNullOrWhiteSpace(nestedValue.ToString()))
                    {
                        return nestedValue.ToString();
                    }
                }
            }

            return null;
        }

        private static bool IsSimpleIdType(object value)
        {
            return value is string || value is int || value is long || value is Guid;
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
