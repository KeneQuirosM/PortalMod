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
    /// por su steamId / PlatformUserIdentifierAbs) gestiona su propio set de
    /// portales de forma totalmente independiente al de los demas jugadores,
    /// lo que hace que el sistema funcione correctamente en multijugador.
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
    /// TODO: verificar en Assembly-CSharp V3.0 si existe una API oficial tipo
    /// "SaveGameManager" / "GameIO.GetSaveGameDir()" para atar este archivo al
    /// mundo/slot de guardado correcto en vez de a una ruta fija junto al mod.
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
            _dirty = true;

            API.Log($"Portal registrado: steamId={steamId} tag='{tag}' pos={pos} (par actual: {positions.Count}/2)");

            if (positions.Count == MaxPortalsPerTag)
            {
                // Par completo: activar el estado visual (luz+particulas
                // intensas) en AMBOS portales del par, no solo en el recien colocado.
                foreach (var p in positions)
                {
                    PortalVisualFX.SetBlockState(p, PortalVisualFX.BlockState.Active);
                }

                return RegisterResult.Success;
            }

            // Portal huerfano: asegurar que quede en estado visual inactivo
            // (relevante sobre todo al renombrar, donde el bloque pudo venir de
            // un estado activo previo).
            PortalVisualFX.SetBlockState(pos, PortalVisualFX.BlockState.Inactive);
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
                    PortalVisualFX.SetBlockState(positions[0], PortalVisualFX.BlockState.Inactive);
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
            // TODO: verificar en Assembly-CSharp V3.0 la forma correcta de obtener
            // la carpeta de guardado del mundo activo (candidatos conocidos en
            // builds anteriores: GameIO.GetSaveGameDir(), GameUtils.GetSaveGameDir(),
            // GameManager.Instance.World.ChunkCache.ChunkCluster...). Mientras tanto
            // se persiste junto al mod para no depender de esa API.
            var baseDir = API.ModInstance != null ? API.ModInstance.Path : Path.GetTempPath();
            var saveDir = Path.Combine(baseDir, "SaveData");
            Directory.CreateDirectory(saveDir);
            return Path.Combine(saveDir, "portals.dat");
        }

        /// <summary>
        /// Guarda el estado actual de portales en disco usando un formato de
        /// texto plano simple y legible (sin dependencias externas de JSON):
        ///   steamId\ttag\tx,y,z\tx,y,z...
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
                            sb.Append('\t').Append(pos.x).Append(',').Append(pos.y).Append(',').Append(pos.z);
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

            try
            {
                _portals.Clear();
                _positionLookup.Clear();

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
                        if (coords.Length != 3)
                        {
                            continue;
                        }

                        var pos = new Vector3i(
                            int.Parse(coords[0], CultureInfo.InvariantCulture),
                            int.Parse(coords[1], CultureInfo.InvariantCulture),
                            int.Parse(coords[2], CultureInfo.InvariantCulture));

                        positions.Add(pos);
                        _positionLookup[pos] = new PortalRef(steamId, tag);
                    }

                    if (!_portals.TryGetValue(steamId, out var tagMap))
                    {
                        tagMap = new Dictionary<string, List<Vector3i>>();
                        _portals[steamId] = tagMap;
                    }

                    tagMap[tag] = positions;
                }

                API.Log($"Portales cargados desde disco ({_positionLookup.Count} en total).");
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
