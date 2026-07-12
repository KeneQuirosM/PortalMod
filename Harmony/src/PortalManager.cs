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
    /// por tag compartida, sin jerarquia madre/hijo. Cada jugador (identificado
    /// por "steamId" — en realidad EntityPlayer.entityId.ToString(), ver
    /// PortalIdentity.GetSteamId en PortalUtils.cs; NO es estable entre
    /// sesiones, ver TODO ahi) gestiona su propio set de portales de forma
    /// totalmente independiente al de los demas jugadores, lo que hace que el
    /// sistema funcione correctamente en multijugador.
    ///
    /// Estructura de datos principal:
    ///   steamId -> tag -> lista de posiciones (maximo 2 elementos == un par).
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

        // steamId -> timestamp (Time.time) en el que termina el cooldown de 5s.
        private readonly Dictionary<string, float> _cooldowns = new Dictionary<string, float>();

        private const int MaxPortalsPerTag = 2;
        public const float CooldownSeconds = 5f;

        private bool _dirty;

        public struct PortalRef
        {
            public string SteamId;
            public string Tag;

            public PortalRef(string steamId, string tag)
            {
                SteamId = steamId;
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
            return _biomes.TryGetValue(pos, out var biome) ? biome : null;
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
        /// Valida que no existan ya 2 portales con ese tag para ese jugador.
        /// </summary>
        public RegisterResult RegisterPortal(string steamId, string tag, Vector3i pos)
        {
            API.Log("[PortalMod] RegisterPortal llamado - steamId: " + steamId + " tag: " + tag + " pos: " + pos);

            if (string.IsNullOrWhiteSpace(tag))
            {
                return RegisterResult.EmptyTag;
            }

            tag = tag.Trim();

            if (!_portals.TryGetValue(steamId, out var tagMap))
            {
                tagMap = new Dictionary<string, List<Vector3i>>();
                _portals[steamId] = tagMap;
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
            _positionLookup[pos] = new PortalRef(steamId, tag);

            if (!_biomes.ContainsKey(pos))
            {
                _biomes[pos] = ResolveBiomeName(pos);
                API.Log($"[PortalMod] Bioma detectado para portal en {pos}: {_biomes[pos] ?? "(desconocido, usa variante default)"}");
            }

            _dirty = true;

            API.Log($"Portal registrado: steamId={steamId} tag='{tag}' pos={pos} (par actual: {positions.Count}/2)");

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

        /// <summary>
        /// Elimina un portal (por ejemplo al destruirse el bloque). El otro
        /// portal del par, si existe, queda automaticamente huerfano.
        /// </summary>
        public bool UnregisterPortal(string steamId, Vector3i pos)
        {
            if (!_positionLookup.TryGetValue(pos, out var portalRef))
            {
                return false;
            }

            if (_portals.TryGetValue(portalRef.SteamId, out var tagMap) &&
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
                    _portals.Remove(portalRef.SteamId);
                }
            }

            _positionLookup.Remove(pos);
            _biomes.Remove(pos);
            _dirty = true;

            API.Log($"Portal eliminado: steamId={portalRef.SteamId} tag='{portalRef.Tag}' pos={pos}");
            return true;
        }

        /// <summary>
        /// Renombra el tag de un portal ya colocado. El portal se desvincula
        /// del tag anterior (dejando huerfano a su antiguo par, si lo tenia) y
        /// se intenta vincular al nuevo tag.
        /// </summary>
        public RegisterResult RenamePortal(string steamId, Vector3i pos, string newTag)
        {
            if (string.IsNullOrWhiteSpace(newTag))
            {
                return RegisterResult.EmptyTag;
            }

            newTag = newTag.Trim();

            if (!_positionLookup.TryGetValue(pos, out var currentRef))
            {
                // No estaba registrado todavia: comportarse como un registro nuevo.
                return RegisterPortal(steamId, newTag, pos);
            }

            if (currentRef.Tag == newTag)
            {
                // Sin cambios reales.
                return _portals[steamId][newTag].Count == MaxPortalsPerTag
                    ? RegisterResult.Success
                    : RegisterResult.SuccessOrphan;
            }

            // Verificar espacio en el tag destino ANTES de desvincular del actual,
            // para no dejar el portal "en el aire" si el nuevo tag ya esta lleno.
            if (_portals.TryGetValue(steamId, out var tagMapCheck) &&
                tagMapCheck.TryGetValue(newTag, out var destPositions) &&
                destPositions.Count >= MaxPortalsPerTag)
            {
                return RegisterResult.TagFull;
            }

            UnregisterPortal(steamId, pos);
            var result = RegisterPortal(steamId, newTag, pos);

            API.Log($"Portal renombrado: steamId={steamId} pos={pos} '{currentRef.Tag}' -> '{newTag}'");
            return result;
        }

        // ========================================================================
        // TELETRANSPORTE
        // ========================================================================

        /// <summary>
        /// Busca el destino para un portal dado. Retorna false (destino no
        /// encontrado / portal huerfano) si no hay un segundo portal con el
        /// mismo tag todavia.
        /// </summary>
        public bool TryGetDestination(string steamId, string tag, Vector3i origin, out Vector3i destination)
        {
            destination = default(Vector3i);

            if (!_portals.TryGetValue(steamId, out var tagMap) || !tagMap.TryGetValue(tag, out var positions))
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

        /// <summary>Devuelve el tag asociado a una posicion de portal, o null si no esta registrada.</summary>
        public string GetTagAt(string steamId, Vector3i pos)
        {
            return _positionLookup.TryGetValue(pos, out var portalRef) && portalRef.SteamId == steamId
                ? portalRef.Tag
                : null;
        }

        /// <summary>Version que no filtra por steamId, usada por los patches para saber "que hay aqui".</summary>
        public bool TryGetPortalRef(Vector3i pos, out PortalRef portalRef)
        {
            return _positionLookup.TryGetValue(pos, out portalRef);
        }

        public bool IsPortalOrphan(string steamId, string tag)
        {
            return _portals.TryGetValue(steamId, out var tagMap) &&
                   tagMap.TryGetValue(tag, out var positions) &&
                   positions.Count < MaxPortalsPerTag;
        }

        /// <summary>
        /// True si la posicion dada pertenece a un par de portales completo
        /// (vinculado). Usado por PortalVisualFX para decidir el estado visual
        /// (luz/particulas) a aplicar en cada ambient tick.
        /// </summary>
        public bool IsPositionActive(Vector3i pos)
        {
            if (!_positionLookup.TryGetValue(pos, out var portalRef))
            {
                return false;
            }

            return _portals.TryGetValue(portalRef.SteamId, out var tagMap) &&
                   tagMap.TryGetValue(portalRef.Tag, out var positions) &&
                   positions.Count == MaxPortalsPerTag;
        }

        // ========================================================================
        // COOLDOWN (regla 7: 5 segundos post-teletransporte para evitar loops)
        // ========================================================================

        public bool IsOnCooldown(string steamId)
        {
            return _cooldowns.TryGetValue(steamId, out var until) && Time.time < until;
        }

        public void SetCooldown(string steamId)
        {
            _cooldowns[steamId] = Time.time + CooldownSeconds;
        }

        public float GetRemainingCooldown(string steamId)
        {
            if (!_cooldowns.TryGetValue(steamId, out var until))
            {
                return 0f;
            }

            return Mathf.Max(0f, until - Time.time);
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
        ///   steamId\ttag\tx,y,z,bioma\tx,y,z,bioma...
        /// El campo "bioma" (Feature "color y modelo por bioma") se agrego
        /// despues del formato original de 3 componentes por posicion;
        /// Load() sigue aceptando lineas viejas sin bioma (lo resuelve de
        /// nuevo la primera vez que confirma el bloque en el mundo real).
        /// </summary>
        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            try
            {
                var sb = new StringBuilder();
                foreach (var playerEntry in _portals)
                {
                    foreach (var tagEntry in playerEntry.Value)
                    {
                        sb.Append(playerEntry.Key).Append('\t').Append(tagEntry.Key);
                        foreach (var pos in tagEntry.Value)
                        {
                            var biome = GetBiome(pos) ?? string.Empty;
                            sb.Append('\t').Append(pos.x).Append(',').Append(pos.y).Append(',').Append(pos.z).Append(',').Append(biome);
                        }
                        sb.Append('\n');
                    }
                }

                File.WriteAllText(GetSaveFilePath(), sb.ToString(), Encoding.UTF8);
                _dirty = false;
                API.Log("Portales guardados en disco.");
            }
            catch (Exception e)
            {
                API.LogWarning($"No se pudo guardar el estado de portales en disco: {e.Message}. " +
                                "Los portales quedaran unicamente en memoria hasta el proximo guardado exitoso.");
            }
        }

        public void Load()
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

                var discardedCount = 0;

                foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
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

                    var steamId = parts[0];
                    var tag = parts[1];
                    var positions = new List<Vector3i>();

                    for (var i = 2; i < parts.Length; i++)
                    {
                        var coords = parts[i].Split(',');
                        // 3 componentes = formato viejo (sin bioma, de antes de
                        // la Feature "color y modelo por bioma); 4 = formato
                        // actual (x,y,z,bioma). Se aceptan ambos.
                        if (coords.Length != 3 && coords.Length != 4)
                        {
                            continue;
                        }

                        var pos = new Vector3i(
                            int.Parse(coords[0], CultureInfo.InvariantCulture),
                            int.Parse(coords[1], CultureInfo.InvariantCulture),
                            int.Parse(coords[2], CultureInfo.InvariantCulture));

                        var savedBiome = coords.Length == 4 && coords[3].Length > 0 ? coords[3] : null;

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

                        // Formato viejo sin bioma guardado: si ya se confirmo
                        // que el bloque existe (chunk cargado), aprovechar y
                        // resolverlo ahora — Save() lo persistira en formato
                        // nuevo la proxima vez que haya cambios.
                        if (savedBiome == null && check == PortalBlockCheck.Present)
                        {
                            savedBiome = ResolveBiomeName(pos);
                            _dirty = true;
                        }

                        _biomes[pos] = savedBiome;
                        positions.Add(pos);
                        _positionLookup[pos] = new PortalRef(steamId, tag);
                    }

                    if (positions.Count == 0)
                    {
                        // Todas las posiciones de este tag resultaron invalidas:
                        // no dejar una entrada vacia en _portals.
                        continue;
                    }

                    if (!_portals.TryGetValue(steamId, out var tagMap))
                    {
                        tagMap = new Dictionary<string, List<Vector3i>>();
                        _portals[steamId] = tagMap;
                    }

                    tagMap[tag] = positions;
                }

                API.Log($"Portales cargados desde disco ({_positionLookup.Count} en total, {discardedCount} descartados por no tener bloque real).");
            }
            catch (Exception e)
            {
                API.LogWarning($"No se pudo cargar el estado de portales desde disco: {e.Message}. " +
                                "Se comenzara con un registro de portales vacio.");
            }
        }

        /// <summary>Todas las posiciones de portal conocidas, usadas por PortalTeleport para el chequeo de colision por tick.</summary>
        public IEnumerable<Vector3i> GetAllPortalPositions()
        {
            return _positionLookup.Keys;
        }
    }
}
