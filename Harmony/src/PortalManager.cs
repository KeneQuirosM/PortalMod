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

            // AUDITORIA (NullReferenceException/ArgumentNullException sin
            // capturar): Dictionary<string, T>.TryGetValue lanza
            // ArgumentNullException si la clave es null. steamId==null puede
            // llegar aca desde un caller con datos invalidos (ya se blindo el
            // camino conocido — XUiPortalTag.Confirm — pero este metodo es
            // publico y otro codigo podria llamarlo directo). Fallar
            // silenciosamente con EmptyTag (reutilizando el resultado mas
            // parecido) es mejor que tumbar al llamador con una excepcion.
            if (steamId == null)
            {
                API.LogWarning("RegisterPortal llamado con steamId null; se ignora.");
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

                if (!_styles.ContainsKey(pos))
                {
                    _styles[pos] = ResolveStyleName(pos);
                    API.Log($"[PortalMod] Estilo detectado para portal en {pos}: {_styles[pos] ?? "(desconocido, usa estilo default/legacy)"}");
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
        /// </summary>
        public bool UnregisterPortal(string steamId, Vector3i pos)
        {
            lock (_lock)
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
                _styles.Remove(pos);
                _dirty = true;

                API.Log($"Portal eliminado: steamId={portalRef.SteamId} tag='{portalRef.Tag}' pos={pos}");
                return true;
            }
        }

        /// <summary>
        /// Renombra el tag de un portal ya colocado. El portal se desvincula
        /// del tag anterior (dejando huerfano a su antiguo par, si lo tenia) y
        /// se intenta vincular al nuevo tag.
        /// </summary>
        public RegisterResult RenamePortal(string steamId, Vector3i pos, string newTag)
        {
            // Ver comentario identico en RegisterPortal.
            if (steamId == null)
            {
                API.LogWarning("RenamePortal llamado con steamId null; se ignora.");
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
            lock (_lock)
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
        }

        /// <summary>Devuelve el tag asociado a una posicion de portal, o null si no esta registrada.</summary>
        public string GetTagAt(string steamId, Vector3i pos)
        {
            lock (_lock)
            {
                return _positionLookup.TryGetValue(pos, out var portalRef) && portalRef.SteamId == steamId
                    ? portalRef.Tag
                    : null;
            }
        }

        /// <summary>Version que no filtra por steamId, usada por los patches para saber "que hay aqui".</summary>
        public bool TryGetPortalRef(Vector3i pos, out PortalRef portalRef)
        {
            lock (_lock)
            {
                return _positionLookup.TryGetValue(pos, out portalRef);
            }
        }

        public bool IsPortalOrphan(string steamId, string tag)
        {
            lock (_lock)
            {
                return _portals.TryGetValue(steamId, out var tagMap) &&
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

                return _portals.TryGetValue(portalRef.SteamId, out var tagMap) &&
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
                    foreach (var playerEntry in _portals)
                    {
                        foreach (var tagEntry in playerEntry.Value)
                        {
                            sb.Append(playerEntry.Key).Append('\t').Append(tagEntry.Key);
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

                    var steamId = parts[0];
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
