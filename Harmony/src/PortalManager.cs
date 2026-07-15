using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Nucleo del sistema de portales estilo Valheim: vinculacion bidireccional
    /// por tag compartida, sin jerarquia madre/hijo.
    ///
    /// Feature "portales por party": los portales se registran bajo un
    /// "ownerKey" — el steamId personal del jugador (en realidad
    /// EntityPlayer.entityId.ToString(), ver PortalIdentity.GetSteamId en
    /// PortalUtils.cs; NO es estable entre sesiones, ver TODO ahi) si no
    /// esta en ninguna party, o el ID de su party (prefijo "party:") si lo
    /// esta — ver GetPortalKey. Los jugadores de la MISMA party comparten
    /// TODOS sus portales entre si; los de partys distintas (o sin party)
    /// no pueden usar los del otro. Ver PortalParty.cs sobre como se
    /// resuelve la party de un jugador (sin confirmar contra el DLL real,
    /// ver TODO CRITICO ahi) y MigratePortals/CheckPartyMembershipChanged
    /// mas abajo sobre que pasa cuando un jugador entra/sale de una party
    /// con portales ya colocados.
    ///
    /// Estructura de datos principal:
    ///   ownerKey -> tag -> lista de posiciones (maximo 2 elementos == un par).
    ///
    /// PERSISTENCIA: 7 Days to Die V3.0 no expone (hasta donde se ha podido
    /// confirmar) un hook oficial de guardado de datos custom por mundo para
    /// mods puramente Harmony (a diferencia de EntityAlive.SaveData). Por eso
    /// se implementa persistencia manual a un archivo de texto plano dentro de
    /// la carpeta de guardado del mundo activo (ver GetSaveFilePath). Si por
    /// cualquier motivo esa carpeta no es accesible, el sistema sigue
    /// funcionando en memoria pero los portales SE PERDERAN al reiniciar el
    /// servidor.
    /// La API real para ubicar esa carpeta por mundo/slot de guardado
    /// (confirmada por decompilacion, ver FIX real en GetSaveFilePath) es
    /// GameIO.GetSaveGameDir().
    /// </summary>
    public class PortalManager
    {
        private static PortalManager _instance;
        public static PortalManager Instance => _instance ?? (_instance = new PortalManager());

        // AUDITORIA (seguridad en multijugador): protege TODAS las
        // colecciones de abajo (_portals, _positionLookup, _biomes, _styles,
        // _cooldowns) contra acceso concurrente. No se pudo confirmar con
        // certeza si Harmony/el juego llaman a estos metodos exclusivamente
        // desde el hilo principal o si algun camino de red (RPCs de
        // colocacion/destruccion de bloque en servidor dedicado) puede
        // ejecutarlos desde otro hilo — el costo de un lock sin contencion es
        // minimo, asi que se agrega como medida defensiva de bajo costo en
        // vez de asumir que nunca hay concurrencia. "lock" en C# es
        // reentrante por hilo, asi que metodos que se llaman entre si en el
        // mismo hilo (ej. RenamePortal -> UnregisterPortal + RegisterPortal)
        // no causan deadlock.
        private readonly object _lock = new object();

        // steamId -> tag -> [posiciones] (maximo 2 posiciones por tag)
        private readonly Dictionary<string, Dictionary<string, List<Vector3i>>> _portals =
            new Dictionary<string, Dictionary<string, List<Vector3i>>>();

        // Lookup inverso para resolver rapidamente "en que portal estoy parado":
        // posicion de bloque -> (steamId, tag). Se mantiene sincronizado con _portals.
        private readonly Dictionary<Vector3i, PortalRef> _positionLookup =
            new Dictionary<Vector3i, PortalRef>();

        // Bioma REAL detectado en el momento de registrar cada portal (Feature
        // "color y modelo por bioma"). Se resuelve una sola vez al registrar
        // (el bioma de una posicion no cambia en el tiempo en V3.0) y se
        // persiste a disco junto con la posicion — ver Save()/Load().
        private readonly Dictionary<Vector3i, string> _biomes =
            new Dictionary<Vector3i, string>();

        // Estilo de portal (Feature "Opcion A: 6 items separados", ver
        // PortalBiomes.cs) detectado a partir del bloque INACTIVO ya
        // colocado en el momento de registrar. Igual que el bioma, se
        // resuelve una sola vez (el estilo de una posicion no cambia — el
        // jugador elige el estilo al craftear el item, no despues) y se
        // persiste a disco.
        private readonly Dictionary<Vector3i, string> _styles =
            new Dictionary<Vector3i, string>();

        // steamId -> timestamp (Time.time) en el que termina el cooldown de 5s.
        // OJO: el cooldown SIEMPRE es por steamId individual, nunca por party
        // (ver GetPortalKey) — es una proteccion contra loops de
        // teletransporte de UN jugador fisico, no una propiedad del portal
        // ni de su dueño.
        private readonly Dictionary<string, float> _cooldowns = new Dictionary<string, float>();

        // Feature "portales por party": posicion -> steamId de quien coloco
        // FISICAMENTE ese portal (independiente de bajo que ownerKey este
        // registrado ahora mismo). Se usa en MigratePortals para que "salir
        // de la party" solo devuelva al jugador SUS PROPIOS portales, no los
        // de otros miembros. Solo en memoria — NO se persiste a disco (ver
        // comentario en Save()/Load()), asi que tras un reinicio del
        // servidor/mundo esta atribucion se pierde para portales que ya
        // estaban registrados bajo una party antes de ese reinicio.
        private readonly Dictionary<Vector3i, string> _originalOwnerSteamId =
            new Dictionary<Vector3i, string>();

        // Feature "portales por party": steamId -> ultimo ownerKey resuelto
        // (personal o "party:<id>") la ultima vez que se reviso. Usado por
        // CheckPartyMembershipChanged para detectar cuando un jugador
        // entra/sale/cambia de party (comparando contra el valor actual de
        // GetPortalKey) y disparar la migracion automatica de sus portales.
        private readonly Dictionary<string, string> _lastKnownPortalKey =
            new Dictionary<string, string>();

        // FIX real (Bug 2 — portal desaparecido al entrar al servidor):
        // steamId -> ownerKey nueva vista en la revision ANTERIOR pero
        // todavia no aplicada. CheckPartyMembershipChanged ya no migra
        // apenas ve una key distinta de _lastKnownPortalKey — exige verla
        // REPETIDA (misma key, dos revisiones seguidas) antes de mover nada.
        // PortalParty.TryGetPartyId es reflection sin confirmar contra el
        // DLL real (ver TODO CRITICO ahi): una lectura puntual inestable
        // (por ejemplo si el dato de party del jugador todavia no termino
        // de sincronizarse justo al conectarse, o si un candidato de
        // reflection matchea por error una propiedad no relacionada) ya
        // podia disparar una migracion real sobre la base de un unico
        // valor que se revertia en la revision siguiente — el portal
        // quedaba registrado bajo una ownerKey que ese jugador ya no vuelve
        // a resolver, indistinguible en el juego de "el portal desaparecio".
        private readonly Dictionary<string, string> _pendingPartyKey =
            new Dictionary<string, string>();

        private const int MaxPortalsPerTag = 2;
        public const float CooldownSeconds = 5f;

        private bool _dirty;

        /// <summary>
        /// "OwnerKey" (antes "SteamId"): identificador bajo el cual esta
        /// registrado el portal en "_portals". Feature "portales por party":
        /// ahora puede ser el steamId personal del jugador (solitario, sin
        /// party) O un ID de party con prefijo "party:" (ver
        /// PortalManager.GetPortalKey) — el nombre se actualizo para reflejar
        /// esto, aunque el TIPO sigue siendo un simple string usado como
        /// clave de diccionario.
        /// </summary>
        public struct PortalRef
        {
            public string OwnerKey;
            public string Tag;

            public PortalRef(string ownerKey, string tag)
            {
                OwnerKey = ownerKey;
                Tag = tag;
            }
        }

        public enum RegisterResult
        {
            Success,
            SuccessOrphan,
            TagFull,
            EmptyTag
        }

        /// <summary>Resultado de verificar si hay un portalBlock real en una posicion — ver CheckPortalBlockAt.</summary>
        private enum PortalBlockCheck
        {
            Present,
            Missing,
            Unknown
        }

        /// <summary>
        /// Verifica si en el mundo REAL hay un portalBlock (o alguna de sus
        /// variantes visuales, ver PortalBlockPatch.IsPortalBlock) en una
        /// posicion dada. Usado unicamente por Load() para descartar entradas
        /// obsoletas del archivo de persistencia (ver FIX real ahi).
        ///
        /// Devuelve "Unknown" (en vez de "Missing") si el chunk todavia no
        /// esta cargado: World.GetBlock() decompilado contra el
        /// Assembly-CSharp.dll real confirma que devuelve BlockValue.Air
        /// tanto para "aire real" como para "chunk sin cargar" (ChunkCache
        /// nulo) — sin distinguir ambos casos seria imposible diferenciar
        /// "aqui no hay portal" de "todavia no se sabe", y validar a ciegas
        /// borraria portales validos en chunks lejanos que el jugador aun no
        /// visito al cargar el mundo. World.IsChunkAreaLoaded(x,y,z) (real,
        /// confirmada por decompilacion) permite distinguir ambos casos.
        /// </summary>
        private static PortalBlockCheck CheckPortalBlockAt(World world, Vector3i pos)
        {
            if (world == null || !world.IsChunkAreaLoaded(pos.x, pos.y, pos.z))
            {
                return PortalBlockCheck.Unknown;
            }

            var blockValue = world.GetBlock(pos);
            return PortalBlockPatch.IsPortalBlock(blockValue) ? PortalBlockCheck.Present : PortalBlockCheck.Missing;
        }

        /// <summary>
        /// Resuelve el nombre de bioma REAL (ver PortalBiomes) en una
        /// posicion, usado al registrar un portal por primera vez (Feature
        /// "color y modelo por bioma"). API real confirmada por
        /// decompilacion: World.GetBiome(int x, int z) devuelve un
        /// BiomeDefinition cuyo campo publico "m_sBiomeName" (string) es el
        /// nombre real usado en Data/Config/biomes.xml (ej. "snow",
        /// "wasteland"). Igual que GetBlock, depende del chunk estar cargado
        /// (GetBiome internamente usa GetChunkFromWorldPos) — no es un
        /// problema aqui porque esto solo se llama al registrar un portal
        /// recien colocado, momento en el que el jugador esta fisicamente
        /// parado ahi y el chunk esta garantizado cargado.
        /// </summary>
        private static string ResolveBiomeName(Vector3i pos)
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            var biomeDef = world?.GetBiome(pos.x, pos.z);
            return biomeDef?.m_sBiomeName;
        }

        /// <summary>Bioma guardado para un portal ya registrado, o null si no se pudo resolver (usa la variante "default" — ver PortalBiomes).</summary>
        public string GetBiome(Vector3i pos)
        {
            lock (_lock)
            {
                return _biomes.TryGetValue(pos, out var biome) ? biome : null;
            }
        }

        /// <summary>
        /// Resuelve el estilo (Feature "Opcion A: 6 items separados") leyendo
        /// el bloque INACTIVO ya colocado en una posicion (el jugador elige
        /// el estilo al craftear/colocar el item — ver items.xml/blocks.xml
        /// — asi que en el momento de RegisterPortal el bloque en el mundo
        /// SIEMPRE es la variante inactiva de ese estilo, nunca una activa
        /// todavia). Mismo patron que ResolveBiomeName: depende del chunk
        /// estar cargado, seguro aqui porque el jugador esta fisicamente
        /// parado en esa posicion al registrar.
        /// </summary>
        private static string ResolveStyleName(Vector3i pos)
        {
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            var block = world?.GetBlock(pos).Block;
            return block != null ? PortalBiomes.GetStyleFromInactiveBlockName(block.GetBlockName()) : null;
        }

        /// <summary>Estilo guardado para un portal ya registrado, o null si no se pudo resolver (usa PortalBiomes.DefaultStyle — el estilo "legacy" original).</summary>
        public string GetStyle(Vector3i pos)
        {
            lock (_lock)
            {
                return _styles.TryGetValue(pos, out var style) ? style : null;
            }
        }

        // ========================================================================
        // FEATURE "PORTALES POR PARTY": resolucion de key + migracion.
        // ========================================================================

        private const string PartyKeyPrefix = "party:";

        /// <summary>True si "key" es un ID de party (no un steamId personal) — ver GetPortalKey.</summary>
        internal static bool IsPartyKey(string key)
        {
            return key != null && key.StartsWith(PartyKeyPrefix, StringComparison.Ordinal);
        }

        /// <summary>Version sin el flag "inParty" para llamadores que no lo necesitan (comparaciones de dueño, lookups).</summary>
        public string GetPortalKey(EntityPlayer player)
        {
            return GetPortalKey(player, out _);
        }

        /// <summary>
        /// Resuelve la clave bajo la que se registran/buscan los portales de
        /// un jugador: el ID de su party (prefijo "party:") si esta en una,
        /// o su steamId personal si no. Ver PortalParty.TryGetPartyId sobre
        /// por que esto usa reflection y por que puede simplemente no
        /// encontrar ninguna party (fallback seguro: todos solitarios).
        /// </summary>
        public string GetPortalKey(EntityPlayer player, out bool inParty)
        {
            inParty = false;

            var steamId = PortalIdentity.GetSteamId(player);
            if (string.IsNullOrEmpty(steamId))
            {
                return null;
            }

            if (PortalParty.TryGetPartyId(player, out var partyId) && !string.IsNullOrEmpty(partyId))
            {
                inParty = true;
                return PartyKeyPrefix + partyId;
            }

            return steamId;
        }

        /// <summary>
        /// Mueve todos los portales registrados bajo "fromKey" a "toKey".
        /// Si "onlyOriginalOwnerSteamId" no es null, SOLO migra las
        /// posiciones cuyo dueño original (ver _originalOwnerSteamId) sea
        /// exactamente ese steamId — usado para "salir de la party", donde
        /// el jugador que se va debe recuperar UNICAMENTE sus propios
        /// portales, no los de sus ex-compañeros de party.
        ///
        /// LIMITE CONOCIDO (documentado tambien en README): esto NO reaplica
        /// la regla de "maximo 2 portales por tag" (regla 5) sobre el
        /// resultado de la fusion. Si dos miembros de la misma party (o un
        /// jugador migrando hacia una party) ya tenian portales bajo el
        /// mismo tag por separado, tras migrar puede terminar habiendo 3+
        /// posiciones registradas para ese tag en la party. El sistema sigue
        /// funcionando (TryGetDestination toma el primer resultado que no
        /// sea el origen), pero deja de garantizar estrictamente el limite
        /// de 2. Rechazar la migracion en ese caso dejaria un bloque fisico
        /// ya colocado en el mundo sin ningun registro — se considero peor
        /// resultado que relajar el limite en este caso especifico.
        /// </summary>
        public void MigratePortals(string fromKey, string toKey, string onlyOriginalOwnerSteamId = null)
        {
            if (string.IsNullOrEmpty(fromKey) || string.IsNullOrEmpty(toKey) || fromKey == toKey)
            {
                return;
            }

            lock (_lock)
            {
                if (!_portals.TryGetValue(fromKey, out var fromTagMap))
                {
                    return;
                }

                var migratedCount = 0;
                var emptiedTags = new List<string>();

                foreach (var tagEntry in fromTagMap)
                {
                    var tag = tagEntry.Key;
                    var positions = tagEntry.Value;
                    var toMove = new List<Vector3i>();

                    foreach (var pos in positions)
                    {
                        if (onlyOriginalOwnerSteamId != null)
                        {
                            var isOwnedByThatPlayer = _originalOwnerSteamId.TryGetValue(pos, out var origOwner) &&
                                                       origOwner == onlyOriginalOwnerSteamId;
                            if (!isOwnedByThatPlayer)
                            {
                                continue;
                            }
                        }

                        toMove.Add(pos);
                    }

                    if (toMove.Count == 0)
                    {
                        continue;
                    }

                    if (!_portals.TryGetValue(toKey, out var toTagMap))
                    {
                        toTagMap = new Dictionary<string, List<Vector3i>>();
                        _portals[toKey] = toTagMap;
                    }

                    if (!toTagMap.TryGetValue(tag, out var destPositions))
                    {
                        destPositions = new List<Vector3i>();
                        toTagMap[tag] = destPositions;
                    }

                    foreach (var pos in toMove)
                    {
                        positions.Remove(pos);
                        destPositions.Add(pos);
                        _positionLookup[pos] = new PortalRef(toKey, tag);
                        migratedCount++;
                    }

                    if (positions.Count == 0)
                    {
                        emptiedTags.Add(tag);
                    }
                }

                foreach (var tag in emptiedTags)
                {
                    fromTagMap.Remove(tag);
                }

                if (fromTagMap.Count == 0)
                {
                    _portals.Remove(fromKey);
                }

                if (migratedCount > 0)
                {
                    _dirty = true;
                    API.Log($"[PortalMod] Migrando {migratedCount} portales de {fromKey} a {toKey}");
                }
            }
        }

        /// <summary>
        /// Detecta si la party de un jugador cambio desde la ultima vez que
        /// se revisó (union, salida, o cambio de una party a otra) y dispara
        /// la migracion automatica correspondiente. Pensado para llamarse
        /// periodicamente (ver PortalTeleport — throttleado ahi, no en cada
        /// frame) para cada jugador activo.
        ///
        /// DECISION DE DISEÑO — POLLING EN VEZ DE EVENTO: no se pudo
        /// confirmar contra el Assembly-CSharp.dll real ningun evento de
        /// "el jugador se unio/salio de una party" (candidatos que se
        /// hubieran probado: PlayerJoinedParty, OnPartyChanged,
        /// PartyManager). Referenciar un evento inexistente directo en el
        /// codigo (a diferencia de un patch de Harmony, que falla en
        /// RUNTIME con un mensaje claro) es un error de COMPILACION duro
        /// que tumbaria todo el mod. Este mod ya usa polling exitosamente
        /// para varios otros estados que cambian con el tiempo (cooldown,
        /// energia electrica del portal — ver PortalPower/PortalVisualFX),
        /// asi que se opto por el mismo patron aca: comparar el resultado
        /// de GetPortalKey contra el ultimo valor conocido, en vez de
        /// apostar a un nombre de evento sin confirmar.
        /// </summary>
        public void CheckPartyMembershipChanged(EntityPlayer player)
        {
            var steamId = PortalIdentity.GetSteamId(player);
            if (string.IsNullOrEmpty(steamId))
            {
                return;
            }

            var currentKey = GetPortalKey(player);
            if (string.IsNullOrEmpty(currentKey))
            {
                return;
            }

            lock (_lock)
            {
                if (!_lastKnownPortalKey.TryGetValue(steamId, out var lastKey))
                {
                    // Primera vez que se ve a este jugador en este proceso:
                    // solo registrar el estado inicial, no hay "cambio" que migrar.
                    _lastKnownPortalKey[steamId] = currentKey;
                    return;
                }

                if (lastKey == currentKey)
                {
                    // Estable de nuevo: descartar cualquier cambio pendiente
                    // que no llego a confirmarse dos veces seguidas.
                    _pendingPartyKey.Remove(steamId);
                    return;
                }

                // GUARD (Bug 2, ver comentario en _pendingPartyKey arriba):
                // no migrar en la PRIMERA revision que difiere de lastKey —
                // exigir que la MISMA key nueva se repita en la revision
                // siguiente (separadas por el throttle real de
                // PortalTeleport.PartyCheckThrottleSeconds) antes de mover
                // ningun portal.
                if (!_pendingPartyKey.TryGetValue(steamId, out var pendingKey) || pendingKey != currentKey)
                {
                    _pendingPartyKey[steamId] = currentKey;
                    API.LogWarning($"MigratePortals cancelada — key inestable detectada (pendingKey={currentKey}, observaciones=1/2)");
                    return;
                }

                _pendingPartyKey.Remove(steamId);

                if (lastKey == steamId)
                {
                    // Union a una party: la key anterior ERA su steamId
                    // personal, asi que TODO lo que tenia registrado bajo esa
                    // key es, por definicion, suyo — se migra completo.
                    MigratePortals(lastKey, currentKey);
                }
                else
                {
                    // Estaba en una party (lastKey) y ahora cambio (a otra
                    // party, o volvio a estar solo): migrar SOLO lo que este
                    // jugador coloco originalmente, dejando el resto con la
                    // party anterior (comportamiento esperado documentado en
                    // README: los portales que colocaron OTROS miembros se
                    // quedan con la party, no siguen a este jugador).
                    MigratePortals(lastKey, currentKey, onlyOriginalOwnerSteamId: steamId);
                }

                _lastKnownPortalKey[steamId] = currentKey;
            }
        }

        public void Init()
        {
            API.Log("PortalManager inicializado (persistencia en memoria + disco).");
        }

        // ========================================================================
        // REGISTRO / DESREGISTRO / RENOMBRADO
        // ========================================================================

        /// <summary>
        /// Registra un nuevo portal para un jugador bajo un tag determinado.
        /// Valida que no existan ya 2 portales con ese tag para el dueño
        /// resuelto (party del jugador, o su steamId personal si no esta en
        /// una — ver GetPortalKey).
        ///
        /// "knownStyle" (Feature "Opcion A: 6 items separados"): estilo YA
        /// resuelto por el llamador en un momento confiable (ver FIX real en
        /// Block_OnBlockPlaceBefore_Patch — el estilo se determina ahi, no
        /// aca). Si se pasa null (por ejemplo desde RenamePortal, re-
        /// registrando una posicion que ya tenia un bloque colocado hace
        /// rato), se cae al comportamiento anterior de re-derivarlo leyendo
        /// el bloque del mundo (ResolveStyleName) — seguro en ese caso
        /// porque no hay una colocacion recien iniciada de por medio.
        /// </summary>
        public RegisterResult RegisterPortal(EntityPlayer player, string tag, Vector3i pos, string knownStyle = null)
        {
            // AUDITORIA (NullReferenceException sin capturar), + Feature
            // "portales por party": antes este metodo tomaba un "steamId"
            // string ya resuelto por el llamador; ahora toma el EntityPlayer
            // directo para poder resolver tanto el ownerKey (steamId o
            // "party:<id>") como el steamId personal "de verdad" (necesario
            // para _originalOwnerSteamId, ver MigratePortals) en un unico lugar.
            var ownerKey = GetPortalKey(player, out var inParty);
            var originalSteamId = PortalIdentity.GetSteamId(player);

            API.Log("[PortalMod] Portal registrado para key: " + ownerKey + " (party: " + inParty + ")");

            if (ownerKey == null)
            {
                API.LogWarning("RegisterPortal llamado con un jugador invalido (GetPortalKey devolvio null); se ignora.");
                return RegisterResult.EmptyTag;
            }

            if (string.IsNullOrWhiteSpace(tag))
            {
                return RegisterResult.EmptyTag;
            }

            // AUDITORIA (persistencia — corrupcion de datos): Save() usa TAB
            // como separador de campo y NEWLINE como separador de linea en su
            // formato de texto plano (ver Save()/Load() mas abajo). Un tag
            // con un tab/newline embebido (posible via pegar texto en el
            // campo, no solo tipeandolo) corromperia el archivo de guardado
            // la proxima vez que se persista, mezclando campos de una linea
            // con la siguiente al recargar. Se sanean esos caracteres ANTES
            // de usar el tag para lo que sea, sin importar quien llame a este
            // metodo (UI, renombrado, etc.) — un unico punto de aplicacion.
            tag = SanitizeTag(tag);
            if (string.IsNullOrWhiteSpace(tag))
            {
                return RegisterResult.EmptyTag;
            }

            lock (_lock)
            {
                if (!_portals.TryGetValue(ownerKey, out var tagMap))
                {
                    tagMap = new Dictionary<string, List<Vector3i>>();
                    _portals[ownerKey] = tagMap;
                }

                if (!tagMap.TryGetValue(tag, out var positions))
                {
                    positions = new List<Vector3i>();
                    tagMap[tag] = positions;
                }

                if (positions.Count >= MaxPortalsPerTag)
                {
                    // Regla 5: maximo 2 portales por tag.
                    return RegisterResult.TagFull;
                }

                positions.Add(pos);
                _positionLookup[pos] = new PortalRef(ownerKey, tag);

                // Feature "portales por party": solo se fija si todavia no
                // hay un dueño original para esta posicion (no pisar el dato
                // en un re-registro por rename — ver RenamePortal, que
                // preserva esta entrada a proposito al desregistrar).
                if (!_originalOwnerSteamId.ContainsKey(pos))
                {
                    _originalOwnerSteamId[pos] = originalSteamId;
                }

                if (!_biomes.ContainsKey(pos))
                {
                    _biomes[pos] = ResolveBiomeName(pos);
                    API.Log($"[PortalMod] Bioma detectado para portal en {pos}: {_biomes[pos] ?? "(desconocido, usa variante default)"}");
                }

                if (!_styles.ContainsKey(pos))
                {
                    _styles[pos] = !string.IsNullOrEmpty(knownStyle) ? knownStyle : ResolveStyleName(pos);
                    API.Log($"[PortalMod] Estilo detectado para portal en {pos}: {_styles[pos] ?? "(desconocido, usa estilo default/legacy)"}");
                }

                _dirty = true;

                API.Log($"Portal registrado: ownerKey={ownerKey} tag='{tag}' pos={pos} (par actual: {positions.Count}/2)");

                if (positions.Count == MaxPortalsPerTag)
                {
                    // Par completo: aplicar el modelo/color del bioma en AMBOS
                    // portales del par, no solo en el recien colocado. Este swap
                    // de bloque SOLO ocurre aqui (evento de vinculacion, raro y
                    // deliberado) — nunca en el ambient tick ni segun el estado
                    // de energia, ver FIX real en PortalVisualFX.cs sobre por
                    // que un swap de bloque frecuente rompe la conexion de cable
                    // real (Feature "requiere electricidad").
                    foreach (var p in positions)
                    {
                        PortalVisualFX.RefreshBlockState(p, linked: true);
                    }

                    return RegisterResult.Success;
                }

                // Portal huerfano: asegurar que quede en estado visual inactivo
                // (relevante sobre todo al renombrar, donde el bloque pudo venir de
                // un estado activo previo).
                PortalVisualFX.RefreshBlockState(pos, linked: false);
                return RegisterResult.SuccessOrphan;
            }
        }

        /// <summary>
        /// Quita tabs/newlines/retornos de carro de un tag antes de usarlo
        /// (ver comentario en RegisterPortal) y recorta espacios sobrantes.
        /// </summary>
        private static string SanitizeTag(string tag)
        {
            return tag.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        /// <summary>
        /// Elimina un portal (por ejemplo al destruirse el bloque). El otro
        /// portal del par, si existe, queda automaticamente huerfano.
        /// "ownerKey" no se usa realmente para la busqueda (se resuelve
        /// fresco desde "_positionLookup"); se mantiene en la firma por
        /// claridad para quien llama.
        ///
        /// "clearOriginalOwner" (Feature "portales por party"): en true
        /// (default, destruccion real del bloque) tambien borra el registro
        /// de quien coloco fisicamente el portal. RenamePortal llama esto
        /// con "false" porque un renombrado hace un
        /// Unregister+Register interno en la MISMA posicion — el dueño
        /// original no deberia "reiniciarse" solo porque alguien le cambio
        /// el nombre al portal.
        /// </summary>
        public bool UnregisterPortal(string ownerKey, Vector3i pos, bool clearOriginalOwner = true)
        {
            lock (_lock)
            {
                if (!_positionLookup.TryGetValue(pos, out var portalRef))
                {
                    return false;
                }

                if (_portals.TryGetValue(portalRef.OwnerKey, out var tagMap) &&
                    tagMap.TryGetValue(portalRef.Tag, out var positions))
                {
                    var wasActivePair = positions.Count == MaxPortalsPerTag;
                    positions.RemoveAll(p => p.Equals(pos));

                    if (wasActivePair && positions.Count == 1)
                    {
                        // El par se rompio: el portal restante queda huerfano y
                        // debe volver al estado visual inactivo.
                        PortalVisualFX.RefreshBlockState(positions[0], linked: false);
                    }

                    if (positions.Count == 0)
                    {
                        tagMap.Remove(portalRef.Tag);
                    }

                    if (tagMap.Count == 0)
                    {
                        _portals.Remove(portalRef.OwnerKey);
                    }
                }

                _positionLookup.Remove(pos);
                _biomes.Remove(pos);
                _styles.Remove(pos);

                if (clearOriginalOwner)
                {
                    _originalOwnerSteamId.Remove(pos);
                }

                _dirty = true;

                API.Log($"Portal eliminado: ownerKey={portalRef.OwnerKey} tag='{portalRef.Tag}' pos={pos}");
                return true;
            }
        }

        /// <summary>
        /// Renombra el tag de un portal ya colocado. El portal se desvincula
        /// del tag anterior (dejando huerfano a su antiguo par, si lo tenia) y
        /// se intenta vincular al nuevo tag.
        /// </summary>
        public RegisterResult RenamePortal(EntityPlayer player, Vector3i pos, string newTag)
        {
            var ownerKey = GetPortalKey(player);

            // Ver comentario identico en RegisterPortal.
            if (ownerKey == null)
            {
                API.LogWarning("RenamePortal llamado con un jugador invalido (GetPortalKey devolvio null); se ignora.");
                return RegisterResult.EmptyTag;
            }

            if (string.IsNullOrWhiteSpace(newTag))
            {
                return RegisterResult.EmptyTag;
            }

            newTag = SanitizeTag(newTag); // ver comentario en RegisterPortal
            if (string.IsNullOrWhiteSpace(newTag))
            {
                return RegisterResult.EmptyTag;
            }

            lock (_lock)
            {
                if (!_positionLookup.TryGetValue(pos, out var currentRef))
                {
                    // No estaba registrado todavia: comportarse como un registro nuevo.
                    return RegisterPortal(player, newTag, pos);
                }

                if (currentRef.Tag == newTag)
                {
                    // Sin cambios reales.
                    return _portals[ownerKey][newTag].Count == MaxPortalsPerTag
                        ? RegisterResult.Success
                        : RegisterResult.SuccessOrphan;
                }

                // Verificar espacio en el tag destino ANTES de desvincular del actual,
                // para no dejar el portal "en el aire" si el nuevo tag ya esta lleno.
                if (_portals.TryGetValue(ownerKey, out var tagMapCheck) &&
                    tagMapCheck.TryGetValue(newTag, out var destPositions) &&
                    destPositions.Count >= MaxPortalsPerTag)
                {
                    return RegisterResult.TagFull;
                }

                // clearOriginalOwner: false — ver comentario en UnregisterPortal:
                // este Unregister es parte interna de un renombrado (misma
                // posicion, mismo bloque fisico), no una destruccion real, asi
                // que el dueño original NO debe reiniciarse aca.
                UnregisterPortal(ownerKey, pos, clearOriginalOwner: false);
                var result = RegisterPortal(player, newTag, pos);

                API.Log($"Portal renombrado: ownerKey={ownerKey} pos={pos} '{currentRef.Tag}' -> '{newTag}'");
                return result;
            }
        }

        // ========================================================================
        // TELETRANSPORTE
        // ========================================================================

        /// <summary>
        /// Busca el destino para un portal dado. Retorna false (destino no
        /// encontrado / portal huerfano) si no hay un segundo portal con el
        /// mismo tag todavia.
        /// </summary>
        public bool TryGetDestination(string ownerKey, string tag, Vector3i origin, out Vector3i destination)
        {
            lock (_lock)
            {
                destination = default(Vector3i);

                if (!_portals.TryGetValue(ownerKey, out var tagMap) || !tagMap.TryGetValue(tag, out var positions))
                {
                    return false;
                }

                if (positions.Count < MaxPortalsPerTag)
                {
                    // Portal huerfano: solo existe un portal con este tag.
                    return false;
                }

                foreach (var p in positions)
                {
                    if (!p.Equals(origin))
                    {
                        destination = p;
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Devuelve el tag asociado a una posicion de portal, o null si no esta registrada.</summary>
        public string GetTagAt(string ownerKey, Vector3i pos)
        {
            lock (_lock)
            {
                return _positionLookup.TryGetValue(pos, out var portalRef) && portalRef.OwnerKey == ownerKey
                    ? portalRef.Tag
                    : null;
            }
        }

        /// <summary>Version que no filtra por dueño, usada por los patches para saber "que hay aqui".</summary>
        public bool TryGetPortalRef(Vector3i pos, out PortalRef portalRef)
        {
            lock (_lock)
            {
                return _positionLookup.TryGetValue(pos, out portalRef);
            }
        }

        public bool IsPortalOrphan(string ownerKey, string tag)
        {
            lock (_lock)
            {
                return _portals.TryGetValue(ownerKey, out var tagMap) &&
                       tagMap.TryGetValue(tag, out var positions) &&
                       positions.Count < MaxPortalsPerTag;
            }
        }

        /// <summary>
        /// True si la posicion dada pertenece a un par de portales completo
        /// (vinculado). Usado por PortalVisualFX para decidir el estado visual
        /// (luz/particulas) a aplicar en cada ambient tick.
        /// </summary>
        public bool IsPositionActive(Vector3i pos)
        {
            lock (_lock)
            {
                if (!_positionLookup.TryGetValue(pos, out var portalRef))
                {
                    return false;
                }

                return _portals.TryGetValue(portalRef.OwnerKey, out var tagMap) &&
                       tagMap.TryGetValue(portalRef.Tag, out var positions) &&
                       positions.Count == MaxPortalsPerTag;
            }
        }

        // ========================================================================
        // COOLDOWN (regla 7: 5 segundos post-teletransporte para evitar loops)
        // ========================================================================

        public bool IsOnCooldown(string steamId)
        {
            lock (_lock)
            {
                return _cooldowns.TryGetValue(steamId, out var until) && Time.time < until;
            }
        }

        public void SetCooldown(string steamId)
        {
            lock (_lock)
            {
                _cooldowns[steamId] = Time.time + CooldownSeconds;
            }
        }

        public float GetRemainingCooldown(string steamId)
        {
            lock (_lock)
            {
                if (!_cooldowns.TryGetValue(steamId, out var until))
                {
                    return 0f;
                }

                return Mathf.Max(0f, until - Time.time);
            }
        }

        // ========================================================================
        // PERSISTENCIA (best-effort — ver comentario de clase)
        // ========================================================================

        private static string GetSaveFilePath()
        {
            // FIX real (portales apareciendo en posiciones random/flotando al
            // cargar un mundo): el archivo se guardaba en una ruta FIJA dentro
            // de la carpeta del MOD (misma ruta sin importar el mundo/slot de
            // guardado activo). Con eso, cargar un mundo B despues de haber
            // jugado un mundo A restauraba los portales de A sobre las
            // coordenadas de B — terreno generado distinto, mismas
            // coordenadas, portal "flotando en el aire" o dentro de un cerro.
            //
            // La API real de guardado por mundo, confirmada decompilando
            // GameIO contra el Assembly-CSharp.dll real: GetSaveGameDir()
            // (estatico, sin argumentos) resuelve internamente
            // GetSaveGameDir(GamePrefs.GetString(EnumGamePrefs.GameWorld),
            // GamePrefs.GetString(EnumGamePrefs.GameName),
            // (UserDataStorageType)GamePrefs.GetInt(EnumGamePrefs.GameSaveStorageType))
            // — es decir, la carpeta REAL del slot de guardado activo. Se usa
            // esa carpeta ahora; el fallback junto al mod solo aplica si
            // GameIO todavia no tiene un mundo activo resuelto (por ejemplo
            // si algo llamara a esto antes de tiempo).
            string saveDir;
            try
            {
                saveDir = GameIO.GetSaveGameDir();
            }
            catch (Exception e)
            {
                saveDir = null;
                API.LogWarning($"GameIO.GetSaveGameDir() fallo ({e.Message}), usando fallback junto al mod.");
            }

            if (string.IsNullOrEmpty(saveDir))
            {
                var baseDir = API.ModInstance != null ? API.ModInstance.Path : Path.GetTempPath();
                saveDir = Path.Combine(baseDir, "SaveData");
            }

            Directory.CreateDirectory(saveDir);
            return Path.Combine(saveDir, "portals.dat");
        }

        /// <summary>
        /// Guarda el estado actual de portales en disco usando un formato de
        /// texto plano simple y legible (sin dependencias externas de JSON):
        ///   steamId\ttag\tx,y,z,bioma,estilo\tx,y,z,bioma,estilo...
        /// Los campos "bioma" y "estilo" (Features "color y modelo por
        /// bioma" y "Opcion A: 6 items separados") se agregaron despues del
        /// formato original de 3 componentes por posicion; Load() sigue
        /// aceptando lineas viejas de 3 o 4 componentes (resuelve lo que
        /// falte de nuevo la primera vez que confirma el bloque en el mundo
        /// real).
        /// </summary>
        public void Save()
        {
            lock (_lock)
            {
                if (!_dirty)
                {
                    return;
                }

                try
                {
                    var sb = new StringBuilder();
                    foreach (var ownerEntry in _portals)
                    {
                        foreach (var tagEntry in ownerEntry.Value)
                        {
                            sb.Append(ownerEntry.Key).Append('\t').Append(tagEntry.Key);
                            foreach (var pos in tagEntry.Value)
                            {
                                var biome = GetBiome(pos) ?? string.Empty;
                                var style = GetStyle(pos) ?? string.Empty;
                                sb.Append('\t').Append(pos.x).Append(',').Append(pos.y).Append(',').Append(pos.z).Append(',').Append(biome).Append(',').Append(style);
                            }
                            sb.Append('\n');
                        }
                    }

                    // AUDITORIA (persistencia — corrupcion de datos): escribir
                    // directo sobre "portals.dat" con File.WriteAllText no es
                    // atomico. Si el proceso muere a mitad de la escritura
                    // (crash del servidor, kill -9, corte de energia), el
                    // archivo queda truncado/corrupto, y el PROXIMO Load()
                    // podia perder de golpe los portales de TODOS los
                    // jugadores (ver FIX adicional en Load(): ahora tolera
                    // lineas individuales corruptas, pero es mejor evitar la
                    // corrupcion de entrada). Se escribe primero a un archivo
                    // temporal y recien al final se reemplaza el archivo real
                    // (File.Replace, o File.Move si todavia no existe un
                    // "portals.dat" previo) — un crash a mitad de camino deja
                    // el ".tmp" a medio escribir pero el "portals.dat" real
                    // (la ultima version buena conocida) queda intacto.
                    var path = GetSaveFilePath();
                    var tempPath = path + ".tmp";
                    File.WriteAllText(tempPath, sb.ToString(), Encoding.UTF8);

                    if (File.Exists(path))
                    {
                        File.Replace(tempPath, path, null);
                    }
                    else
                    {
                        File.Move(tempPath, path);
                    }

                    _dirty = false;
                    API.Log("Portales guardados en disco.");
                }
                catch (Exception e)
                {
                    API.LogWarning($"No se pudo guardar el estado de portales en disco: {e.Message}. " +
                                    "Los portales quedaran unicamente en memoria hasta el proximo guardado exitoso.");
                }
            }
        }

        // AUDITORIA (persistencia — perdida de datos en crash duro): el
        // unico punto de guardado antes de esto era
        // GameManager_OnApplicationQuit_Patch (ver API.cs), que NUNCA se
        // ejecuta en un crash duro del proceso (kill -9, OOM, corte de
        // energia, "Detener" abrupto de un panel de hosting) — en ese
        // escenario se pierden TODOS los portales creados/modificados desde
        // el ultimo guardado exitoso. Se agrega un autoguardado periodico
        // (ver MaybeAutoSave, llamado desde PortalTeleport.Tick) para acotar
        // esa ventana de perdida; Save() ya es un no-op barato si no hay
        // cambios pendientes (_dirty), asi que llamarlo seguido no tiene
        // costo real la mayor parte del tiempo.
        private const float AutoSaveIntervalSeconds = 300f; // 5 minutos
        private float _nextAutoSaveTime;

        /// <summary>Guarda en disco si ya pasaron AutoSaveIntervalSeconds desde el ultimo intento Y hay cambios pendientes. Ver comentario de clase arriba.</summary>
        public void MaybeAutoSave()
        {
            if (Time.time < _nextAutoSaveTime)
            {
                return;
            }

            _nextAutoSaveTime = Time.time + AutoSaveIntervalSeconds;
            Save();
        }

        public void Load()
        {
            lock (_lock)
            {
            var path = GetSaveFilePath();
            if (!File.Exists(path))
            {
                return;
            }

            // Usado para validar cada posicion cargada contra el mundo real
            // antes de registrarla (ver FIX real mas abajo).
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;

            try
            {
                _portals.Clear();
                _positionLookup.Clear();
                _biomes.Clear();
                _styles.Clear();
                // AUDITORIA (multijugador — datos residuales entre mundos):
                // _cooldowns NO se limpiaba aqui. Si un jugador queda en
                // cooldown en el Mundo A y, sin reiniciar el proceso del
                // juego, se carga el Mundo B (mismo cliente, misma
                // instancia de PortalManager por ser singleton de proceso),
                // el cooldown viejo seguia aplicando en el Mundo B para
                // cualquier steamId (entityId) que coincidiera. No es un
                // riesgo de crash/corrupcion, pero es estado incorrecto que
                // no tiene motivo para sobrevivir a un cambio de mundo.
                _cooldowns.Clear();

                // Feature "portales por party": limpiar tambien el indice de
                // dueño original y el cache de "ultima key conocida" — igual
                // que el resto de las colecciones, son datos del mundo/
                // proceso anterior que no deben sobrevivir a cargar otro mundo.
                _originalOwnerSteamId.Clear();
                _lastKnownPortalKey.Clear();
                _pendingPartyKey.Clear();

                var discardedCount = 0;
                var skippedLineCount = 0;

                foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                  // AUDITORIA (persistencia — perdida de datos): antes, una
                  // sola linea corrupta (por ejemplo un int.Parse fallando
                  // sobre un campo numerico truncado por un crash a mitad de
                  // escritura, ver FIX de escritura atomica en Save()) hacia
                  // que la excepcion escapara hasta el catch EXTERIOR de todo
                  // Load(), descartando de un tiron los portales de TODOS los
                  // jugadores ya procesados en ese mismo Load(), no solo la
                  // linea mala. Cada linea ahora se procesa en su propio
                  // try/catch: si una linea especifica falla, se loguea y se
                  // salta, pero el resto del archivo se sigue cargando normal.
                  try
                  {
                    var line = rawLine.Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    var parts = line.Split('\t');
                    if (parts.Length < 3)
                    {
                        continue;
                    }

                    var ownerKey = parts[0];
                    var tag = parts[1];
                    var positions = new List<Vector3i>();

                    for (var i = 2; i < parts.Length; i++)
                    {
                        var coords = parts[i].Split(',');
                        // 3 componentes = formato viejo (sin bioma ni estilo,
                        // de antes de esas Features); 4 = con bioma pero sin
                        // estilo (de antes de "Opcion A: 6 items separados");
                        // 5 = formato actual (x,y,z,bioma,estilo). Se aceptan
                        // los tres.
                        if (coords.Length != 3 && coords.Length != 4 && coords.Length != 5)
                        {
                            continue;
                        }

                        var pos = new Vector3i(
                            int.Parse(coords[0], CultureInfo.InvariantCulture),
                            int.Parse(coords[1], CultureInfo.InvariantCulture),
                            int.Parse(coords[2], CultureInfo.InvariantCulture));

                        var savedBiome = coords.Length >= 4 && coords[3].Length > 0 ? coords[3] : null;
                        var savedStyle = coords.Length == 5 && coords[4].Length > 0 ? coords[4] : null;

                        // FIX real (portales apareciendo en posiciones random al
                        // cargar, incluso flotando en el aire): antes de
                        // registrar una posicion guardada, verificar que en el
                        // mundo REAL exista un portalBlock (o alguna de sus
                        // variantes visuales) ahi. Entradas obsoletas -que ya
                        // no correspondian a un bloque real, por ejemplo por
                        // venir de otro mundo antes del FIX de
                        // GetSaveFilePath() de arriba, o por haberse destruido
                        // el bloque sin pasar por
                        // Block_OnBlockDestroyedBy_Patch (explosion, comando de
                        // consola, terreno regenerado)- se descartan en vez de
                        // registrarse.
                        var check = CheckPortalBlockAt(world, pos);
                        var existsLabel = check == PortalBlockCheck.Missing
                            ? "false"
                            : check == PortalBlockCheck.Present
                                ? "true"
                                : "true (chunk no cargado, sin confirmar)";
                        API.Log($"[PortalMod] Validando portal cargado en pos {pos} - bloque existe: {existsLabel}");

                        if (check == PortalBlockCheck.Missing)
                        {
                            discardedCount++;
                            _dirty = true;
                            continue;
                        }

                        // Formato viejo sin bioma/estilo guardado: si ya se
                        // confirmo que el bloque existe (chunk cargado),
                        // aprovechar y resolverlos ahora — Save() los
                        // persistira en formato nuevo la proxima vez que haya
                        // cambios. NOTA sobre estilo: ResolveStyleName solo
                        // reconoce el bloque INACTIVO de cada estilo; si el
                        // portal ya esta vinculado (bloque activo puesto),
                        // no podra determinarse aqui y cae al estilo default
                        // ("legacy") — correcto de todas formas para
                        // cualquier portal creado antes de esta Feature, ya
                        // que "legacy" era el unico estilo que existia.
                        if (check == PortalBlockCheck.Present)
                        {
                            // FIX real (Bug — portales viejos sin
                            // TileEntityPowered pese a haber funcionado
                            // antes, ver comentario detallado en
                            // PortalPower.cs): portalBlock es
                            // MultiBlockDim="1,2,1" y la celda HIJA reusa el
                            // MISMO nombre de bloque que la madre (confirmado
                            // decompilando Block.MultiBlockArray.AddChilds),
                            // asi que CheckPortalBlockAt de arriba no puede
                            // distinguirlas — solo la celda madre
                            // (BlockValue.ischild == false) tiene el
                            // TileEntity real. Si la posicion guardada
                            // resulta ser la hija, se corrige aca de forma
                            // PERMANENTE a la madre real (BlockValue.parent,
                            // offset confirmado decompilando
                            // Block.MultiBlockArray.GetParentPos) antes de
                            // registrarla — asi PortalPower.HasNearbyPower ya
                            // no necesita resolverla en cada chequeo.
                            var blockValueAtPos = world.GetBlock(pos);
                            if (blockValueAtPos.ischild)
                            {
                                var masterPos = pos + blockValueAtPos.parent;
                                API.LogWarning($"[PortalMod] Portal cargado en {pos} apuntaba a la celda hija del multiblock — corrigiendo a la celda madre real {masterPos}.");
                                pos = masterPos;
                                _dirty = true;
                            }

                            if (savedBiome == null)
                            {
                                savedBiome = ResolveBiomeName(pos);
                                _dirty = true;
                            }

                            if (savedStyle == null)
                            {
                                savedStyle = ResolveStyleName(pos);
                                _dirty = true;
                            }
                        }

                        _biomes[pos] = savedBiome;
                        _styles[pos] = savedStyle;
                        positions.Add(pos);
                        _positionLookup[pos] = new PortalRef(ownerKey, tag);

                        // Feature "portales por party": ver comentario en el
                        // campo de la clase — no se persiste el dueño
                        // original real, asi que se usa como fallback SOLO
                        // para claves personales (donde "ownerKey" YA ES
                        // literalmente el steamId de su unico dueño posible).
                        // Para claves de party no se puede reconstruir sin
                        // haberlo guardado — queda sin asignar (Migrate al
                        // salir de party simplemente no movera ese portal en
                        // particular hasta que se reinicie su atribucion).
                        if (!IsPartyKey(ownerKey) && !_originalOwnerSteamId.ContainsKey(pos))
                        {
                            _originalOwnerSteamId[pos] = ownerKey;
                        }
                    }

                    if (positions.Count == 0)
                    {
                        // Todas las posiciones de este tag resultaron invalidas:
                        // no dejar una entrada vacia en _portals.
                        continue;
                    }

                    if (!_portals.TryGetValue(ownerKey, out var tagMap))
                    {
                        tagMap = new Dictionary<string, List<Vector3i>>();
                        _portals[ownerKey] = tagMap;
                    }

                    tagMap[tag] = positions;
                  }
                  catch (Exception lineEx)
                  {
                    skippedLineCount++;
                    _dirty = true;
                    API.LogWarning($"Linea corrupta/invalida en portals.dat, se salta ({lineEx.Message}): '{rawLine}'");
                  }
                }

                API.Log($"Portales cargados desde disco ({_positionLookup.Count} en total, {discardedCount} descartados por no tener bloque real, {skippedLineCount} lineas corruptas saltadas).");
            }
            catch (Exception e)
            {
                API.LogWarning($"No se pudo cargar el estado de portales desde disco: {e.Message}. " +
                                "Se comenzara con un registro de portales vacio.");
            }
            }
        }

        /// <summary>
        /// Todas las posiciones de portal conocidas, usadas por PortalTeleport
        /// para el chequeo de colision por tick y por PortalVisualFX para el
        /// ambient tick.
        /// AUDITORIA (seguridad en multijugador): antes devolvia
        /// "_positionLookup.Keys" directamente — una VISTA en vivo sobre el
        /// diccionario, no una copia. Un llamador que enumera esa vista con un
        /// "foreach" mientras otro hilo (o el mismo hilo, en una llamada
        /// reentrante) modifica "_positionLookup" en simultaneo dispara
        /// "InvalidOperationException: Collection was modified" — y aunque
        /// todos los metodos de escritura de esta clase ya estan protegidos
        /// con "lock (_lock)" arriba, ese lock NO cubre la enumeracion externa
        /// que hace el llamador despues de que este metodo ya retorno. Se
        /// devuelve una copia (snapshot) para que la enumeracion del llamador
        /// sea segura sin importar que pase despues con el diccionario real.
        /// </summary>
        public List<Vector3i> GetAllPortalPositions()
        {
            lock (_lock)
            {
                return new List<Vector3i>(_positionLookup.Keys);
            }
        }
    }
}
