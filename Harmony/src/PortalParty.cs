using System;
using System.Linq;
using System.Reflection;

namespace PortalMod
{
    /// <summary>
    /// Resuelve el ID de "party"/grupo de un jugador, si tiene uno (Feature
    /// "portales por party" — ver PortalManager.GetPortalKey).
    ///
    /// TODO CRITICO — SIN CONFIRMAR CONTRA Assembly-CSharp.dll REAL: no se
    /// pudo verificar en este entorno (sin acceso al DLL/decompilador) si
    /// 7 Days to Die V3.0 tiene un sistema de "party"/grupo formal, ni bajo
    /// qué nombre. A diferencia de otros hallazgos de este mod (confirmados
    /// por decompilación real en sesiones anteriores con el juego
    /// instalado), esto es una hipótesis sin verificar — es posible que la
    /// vanilla de V3.0 no tenga ningún sistema de party y este archivo
    /// termine devolviendo null siempre (lo cual es un fallback SEGURO: el
    /// mod simplemente trata a todos como jugadores solitarios, exactamente
    /// el comportamiento de antes de esta Feature).
    ///
    /// POR QUÉ REFLECTION Y NO UNA LLAMADA DIRECTA: escribir
    /// "EntityPlayer.Party" o "PartyManager.GetParty(...)" directo, sin
    /// confirmar que esos tipos/miembros existen, arriesga un error de
    /// COMPILACIÓN duro (CS0246 "tipo no encontrado") que tumbaría la
    /// compilación de TODO el mod, no solo esta feature — un fallo mucho
    /// peor que una firma de método incorrecta (que Harmony reporta en
    /// runtime con un mensaje claro, ya varias veces en este mod). Por eso
    /// se prueban varios candidatos de nombre por reflection: si ninguno
    /// existe, el mod sigue compilando y funcionando (sin la feature de
    /// party), en vez de negarse a cargar.
    ///
    /// SIGUIENTE PASO (si se confirma que SÍ existe un sistema de party):
    /// reemplazar el cuerpo de TryGetPartyId por una llamada directa al
    /// método/propiedad real, y borrar toda esta capa de reflection — mucho
    /// más simple y rápido de ejecutar (esto corre potencialmente cada
    /// pocos segundos por jugador, ver PortalManager.CheckPartyMembershipChanged).
    /// </summary>
    internal static class PortalParty
    {
        // Candidatos de nombre de propiedad de INSTANCIA en EntityPlayer (o
        // alguna clase base como EntityAlive/Entity) que devolvería el
        // objeto "party"/grupo del jugador.
        private static readonly string[] InstancePartyPropertyCandidates =
        {
            "Party", "PlayerParty", "Group", "PlayerGroup", "Team"
        };

        // Candidatos de nombre de propiedad/campo DENTRO del objeto "party"
        // (una vez obtenido) que contendría su identificador único.
        private static readonly string[] PartyIdMemberCandidates =
        {
            "PartyId", "Id", "GroupId", "TeamId", "partyId", "id"
        };

        // Candidatos de nombre de clase estática/singleton global tipo
        // "PartyManager" que podría exponer un metodo
        // "GetParty(EntityPlayer)"/"GetPartyId(int)" en vez de (o ademas de)
        // una propiedad de instancia en EntityPlayer.
        private static readonly string[] StaticManagerTypeNameCandidates =
        {
            "PartyManager", "GroupManager", "TeamManager"
        };

        /// <summary>
        /// Intenta resolver el ID de party del jugador. Devuelve false (con
        /// partyId=null) si el jugador no esta en ninguna party, o si no se
        /// pudo determinar (candidato no encontrado / sistema de party
        /// inexistente en esta version del juego) — ambos casos se tratan
        /// igual por el llamador (PortalManager.GetPortalKey cae a steamId).
        /// </summary>
        internal static bool TryGetPartyId(EntityPlayer player, out string partyId)
        {
            partyId = null;

            if (player == null)
            {
                return false;
            }

            try
            {
                partyId = TryGetPartyIdFromInstanceProperty(player) ?? TryGetPartyIdFromStaticManager(player);
                return !string.IsNullOrEmpty(partyId);
            }
            catch (Exception e)
            {
                // Puramente best-effort: un fallo aca NUNCA debe impedir que
                // el resto del mod funcione, solo degrada a "sin party".
                API.LogWarning($"PortalParty.TryGetPartyId fallo por reflection ({e.Message}); se trata al jugador como solitario.");
                return false;
            }
        }

        private static string TryGetPartyIdFromInstanceProperty(EntityPlayer player)
        {
            var playerType = player.GetType();

            foreach (var propName in InstancePartyPropertyCandidates)
            {
                // Busca en toda la jerarquia (EntityPlayer, EntityPlayerLocal,
                // EntityAlive, Entity, ...), no solo en el tipo exacto.
                var prop = FindPropertyInHierarchy(playerType, propName);
                if (prop == null)
                {
                    continue;
                }

                var partyObj = prop.GetValue(player);
                if (partyObj == null)
                {
                    // Propiedad existe pero es null: el jugador no esta en
                    // party ahora mismo (candidato confirmado como real,
                    // simplemente sin valor). No seguir probando otros
                    // candidatos de nombre — este es "el" real.
                    return null;
                }

                var id = ExtractIdFromPartyObject(partyObj);
                if (id != null)
                {
                    return id;
                }

                // La propiedad existe y tiene un objeto, pero no se encontro
                // ningun miembro de ID reconocible dentro — puede que el
                // "partyObj" en si YA SEA el identificador (por ejemplo si
                // "Party" fuera directamente un int/string/Guid).
                if (IsSimpleIdType(partyObj))
                {
                    return partyObj.ToString();
                }
            }

            return null;
        }

        private static string TryGetPartyIdFromStaticManager(EntityPlayer player)
        {
            foreach (var typeName in StaticManagerTypeNameCandidates)
            {
                // Busca el tipo en TODOS los ensamblados cargados (no solo
                // Assembly-CSharp), ya que no se confirmo en cual viviria.
                var managerType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => SafeGetType(a, typeName))
                    .FirstOrDefault(t => t != null);

                if (managerType == null)
                {
                    continue;
                }

                var instance = GetSingletonInstance(managerType);

                // Candidatos de nombre de metodo que reciba el jugador (o su
                // entityId) y devuelva el objeto/ID de party.
                var method = managerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .FirstOrDefault(m =>
                        (m.Name == "GetParty" || m.Name == "GetPartyId" || m.Name == "GetPlayerParty") &&
                        m.GetParameters().Length == 1 &&
                        (m.GetParameters()[0].ParameterType.IsInstanceOfType(player) || m.GetParameters()[0].ParameterType == typeof(int)));

                if (method == null)
                {
                    continue;
                }

                var target = method.IsStatic ? null : instance;
                if (!method.IsStatic && target == null)
                {
                    continue;
                }

                var argValue = method.GetParameters()[0].ParameterType == typeof(int)
                    ? (object)player.entityId
                    : player;

                var result = method.Invoke(target, new[] { argValue });
                if (result == null)
                {
                    return null;
                }

                var id = ExtractIdFromPartyObject(result);
                if (id != null)
                {
                    return id;
                }

                if (IsSimpleIdType(result))
                {
                    return result.ToString();
                }
            }

            return null;
        }

        private static PropertyInfo FindPropertyInHierarchy(Type startType, string propName)
        {
            for (var t = startType; t != null; t = t.BaseType)
            {
                var prop = t.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    return prop;
                }
            }

            return null;
        }

        private static string ExtractIdFromPartyObject(object partyObj)
        {
            var partyType = partyObj.GetType();

            foreach (var memberName in PartyIdMemberCandidates)
            {
                var prop = partyType.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    var value = prop.GetValue(partyObj);
                    if (value != null)
                    {
                        return value.ToString();
                    }
                }

                var field = partyType.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var value = field.GetValue(partyObj);
                    if (value != null)
                    {
                        return value.ToString();
                    }
                }
            }

            return null;
        }

        private static bool IsSimpleIdType(object value)
        {
            return value is int || value is long || value is string || value is Guid;
        }

        private static object GetSingletonInstance(Type managerType)
        {
            var instanceProp = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProp != null)
            {
                return instanceProp.GetValue(null);
            }

            var instanceField = managerType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            return instanceField?.GetValue(null);
        }

        private static Type SafeGetType(Assembly assembly, string typeName)
        {
            try
            {
                return assembly.GetTypes().FirstOrDefault(t => t.Name == typeName);
            }
            catch (ReflectionTypeLoadException)
            {
                return null;
            }
        }
    }
}
