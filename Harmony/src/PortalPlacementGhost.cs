using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Feature 1 ("modo fantasma al colocar el portal"): mientras el jugador
    /// local tiene equipado alguno de los items que colocan un portalBlock
    /// (ver "PortalItemNames" abajo), se muestra un preview semitransparente
    /// del marco (1x2x1, ver MultiBlockDim en blocks.xml) siguiendo la mira,
    /// con la MISMA rotacion que se aplicaria al confirmar la colocacion
    /// real (PortalOrientation.ComputeRotationFromPlayerFacing — la misma
    /// funcion que usa Block_OnBlockPlaceBefore_Patch al colocar de verdad,
    /// fuente unica de verdad para que el ghost y la colocacion final nunca
    /// queden desincronizados).
    ///
    /// LIMITACION CONOCIDA (documentada tambien en README.md/TESTING.md,
    /// seccion "Feature 1"): NO es un hook sobre el sistema NATIVO de
    /// preview de colocacion del juego (el que usan la mayoria de los
    /// bloques vanilla mientras estan "en mano" — clase interna de
    /// "ItemActionPlaceBlock"/similar). No se pudo confirmar contra el
    /// Assembly-CSharp.dll real que metodo especifico corre ese preview
    /// cuadro a cuadro para poder engancharse ahi de forma segura (a
    /// diferencia del resto de los patches de Harmony de este mod, que
    /// apuntan a metodos ya confirmados por error real de carga — ver
    /// PortalBlockPatch.cs). Enganchar Harmony a un metodo de motor
    /// adivinado arriesga que TODO el mod falle al cargar si la firma no
    /// coincide (ver comentario extenso en PortalBlockPatch.cs sobre este
    /// riesgo real ya vivido varias veces en este mismo mod), asi que en vez
    /// de eso este archivo construye su PROPIO ghost, enteramente con
    /// primitivas de Unity (GameObject.CreatePrimitive — no depende de
    /// ningun AssetBundle/prefab del mod) y el MISMO raycast de mira ya
    /// confirmado y usado por PortalHoverFX (EntityPlayerLocal.HitInfo). Es
    /// una caja traslucida del tamaño del marco, NO un clon del modelo 3D
    /// real de cada estilo (platform/grid/claws/cylinder/wings/arch) — ver
    /// README.md para el detalle completo de esta limitacion y como
    /// mejorarla mas adelante si se confirma la API real de la preview
    /// nativa/del prefab de cada estilo. Ademas del cubo, el ghost incluye
    /// una flecha simple pegada al piso (ver PortalDirectionArrow) que
    /// indica el lado "frente" — mismo patron visual que el juego usa al
    /// colocar puertas (una unica flecha clara integrada al ghost, no
    /// piezas flotando por separado). A pedido explicito del usuario esta
    /// flecha vive UNICAMENTE aca, durante la colocacion — no se muestra en
    /// el portal ya colocado (existio una version anterior,
    /// PortalExitIndicator, que se elimino).
    ///
    /// La deteccion de "que item tiene equipado el jugador local" tambien es
    /// best-effort por reflection (mismo patron defensivo que
    /// PortalIdentity/PortalParty: varios nombres de miembro candidatos,
    /// nunca una referencia directa a un miembro sin confirmar — evita un
    /// error de COMPILACION si el nombre real difiere en el
    /// Assembly-CSharp.dll de V3.0). Si ningun candidato resuelve, el ghost
    /// simplemente no se muestra nunca — el mod sigue funcionando
    /// exactamente igual que antes de esta Feature, nunca peor.
    /// </summary>
    internal static class PortalPlacementGhost
    {
        private static readonly HashSet<string> PortalItemNames = new HashSet<string>
        {
            "portalBlockItem",
            "portalBlock_platformItem",
            "portalBlock_gridItem",
            "portalBlock_clawsItem",
            "portalBlock_cylinderItem",
            "portalBlock_wingsItem",
            "portalBlock_archItem",
        };

        // Candidatos de nombre de propiedad/campo en "Inventory" (tipo real
        // de EntityAlive.inventory, ya usado en otras partes del mod — ver
        // PortalTeleport.ApplyTravelBuff/player.Buffs, mismo patron de
        // confianza para miembros de EntityAlive) que expondria el
        // ItemValue actualmente equipado en la mano del jugador. Se prueban
        // en orden — mismo patron defensivo que
        // PortalIdentity.StableIdPropertyCandidates.
        private static readonly string[] HoldingItemValueCandidates =
        {
            "holdingItemItemValue", "HoldingItemItemValue", "rightItem"
        };

        // Candidatos alternativos, por si la propiedad de arriba expone
        // directamente un ItemClass en vez de un ItemValue.
        private static readonly string[] HoldingItemClassCandidates =
        {
            "holdingItem", "HoldingItem"
        };

        // Candidatos de nombre de miembro DENTRO del ItemClass resuelto que
        // contendria el nombre interno del item (ej. "portalBlockItem").
        private static readonly string[] ItemNameMemberCandidates =
        {
            "Name", "name"
        };

        private static readonly Dictionary<Type, MemberInfo> _holdingValueMemberCache = new Dictionary<Type, MemberInfo>();
        private static readonly Dictionary<Type, MemberInfo> _holdingClassMemberCache = new Dictionary<Type, MemberInfo>();
        private static readonly Dictionary<Type, MemberInfo> _itemNameMemberCache = new Dictionary<Type, MemberInfo>();
        private static readonly Dictionary<Type, MethodInfo> _itemNameMethodCache = new Dictionary<Type, MethodInfo>();

        // Se resuelve una unica vez (o hasta que un intento falle) si la
        // deteccion de item equipado esta disponible en este
        // Assembly-CSharp.dll -- evita repetir un escaneo por reflection
        // completo en CADA frame para un jugador que nunca tiene el ghost
        // activo (mayoria del tiempo de juego).
        private static bool _detectionUnavailableLogged;

        private static GameObject _ghostRoot;
        private static bool _ghostVisible;
        private static byte _lastRotation = 255; // valor invalido a proposito, fuerza la primera actualizacion.

        /// <summary>Se invoca una vez por frame desde PortalTeleport.Tick() — mismo patron que PortalHoverFX.Tick().</summary>
        public static void Tick(World world)
        {
            if (GameManager.IsDedicatedServer || world == null)
            {
                HideGhost();
                return;
            }

            var localPlayer = world.GetPrimaryPlayer();
            if (localPlayer == null || !TryGetHeldPortalItemName(localPlayer, out _))
            {
                HideGhost();
                return;
            }

            var hitInfo = localPlayer.HitInfo;
            if (hitInfo == null || !hitInfo.bHitValid)
            {
                HideGhost();
                return;
            }

            // Aproximacion (ver limitacion documentada en la clase): se
            // previsualiza directamente sobre la celda que la mira esta
            // señalando, la misma que ya usa PortalHoverFX/la activacion con
            // tecla E (EntityPlayerLocal.HitInfo.hit.blockPos, confirmado
            // real y ya en uso en este mod) — no se intento reproducir el
            // desplazamiento exacto hacia la cara/normal de colocacion que
            // usa el sistema nativo, asi que la celda exacta puede diferir
            // en 1 bloque de donde el juego realmente colocaria el portal;
            // ajustar aca si se confirma en el juego real (ver TESTING.md).
            var previewPos = hitInfo.hit.blockPos;
            var rotation = PortalOrientation.ComputeRotationFromPlayerFacing(localPlayer);

            ShowGhostAt(previewPos, rotation);
        }

        private static void ShowGhostAt(Vector3i blockPos, byte rotation)
        {
            try
            {
                if (_ghostRoot == null)
                {
                    _ghostRoot = BuildGhost();
                }

                _ghostRoot.SetActive(true);
                _ghostVisible = true;

                // Ancla del ghost: esquina inferior de la celda apuntada
                // (mismo origen de coordenadas que usa el resto del mod para
                // "blockPos", ver por ejemplo PortalVisualFX.SpawnAmbientParticle).
                var origin = new Vector3(blockPos.x + 0.5f, blockPos.y, blockPos.z + 0.5f);
                _ghostRoot.transform.position = origin;

                if (rotation != _lastRotation)
                {
                    _lastRotation = rotation;
                    _ghostRoot.transform.rotation = PortalOrientation.ToQuaternion(rotation);
                }
            }
            catch (Exception e)
            {
                API.LogWarning($"PortalPlacementGhost: fallo mostrando el ghost de colocacion ({e.Message}); se desactiva por el resto de la sesion.");
                HideGhost();
                DestroyGhost();
            }
        }

        private static void HideGhost()
        {
            if (_ghostVisible && _ghostRoot != null)
            {
                _ghostRoot.SetActive(false);
            }

            _ghostVisible = false;
        }

        private static void DestroyGhost()
        {
            if (_ghostRoot != null)
            {
                UnityEngine.Object.Destroy(_ghostRoot);
                _ghostRoot = null;
            }
        }

        // Tamano de la flecha de direccion (ver PortalDirectionArrow).
        private const float ArrowSize = 0.6f;

        /// <summary>
        /// Caja traslucida del tamaño exacto del marco (1 ancho x 2 alto x 1
        /// profundo, ver MultiBlockDim en blocks.xml), con una flecha simple
        /// pegada al piso (mismo patron visual que usa el juego al colocar
        /// puertas: una unica flecha clara indicando el lado de
        /// apertura/salida, integrada al ghost) que indica hacia donde va a
        /// salir el jugador al usar el portal — ver limitacion de fidelidad
        /// documentada en el comentario de la clase.
        /// </summary>
        private static GameObject BuildGhost()
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "PortalMod_PlacementGhost";

            var collider = box.GetComponent<Collider>();
            if (collider != null)
            {
                // Puramente visual: un collider real interferiria con el
                // propio raycast de colocacion/mira del jugador.
                UnityEngine.Object.Destroy(collider);
            }

            box.transform.localScale = new Vector3(1f, 2f, 1f);

            var renderer = box.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Transparent/Diffuse")
                    ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
                    ?? Shader.Find("Unlit/Transparent");

                if (shader != null)
                {
                    var material = new Material(shader);
                    // Mismo tinte gris-azulado del estado inactivo
                    // (TintColor="8CA0B4" en blocks.xml) para que el ghost
                    // se sienta coherente con como se va a ver el portal
                    // recien colocado, con transparencia baja para que se
                    // note claramente que es un preview y no el bloque real.
                    material.color = new Color(0.55f, 0.63f, 0.71f, 0.35f);
                    renderer.material = material;
                }
                // Si ningun shader transparente resolvio (mismo riesgo de
                // stripping documentado en PortalDirectionArrow.
                // ApplyUnlitColor), se deja el material opaco por defecto de
                // la primitiva — el ghost se ve como una caja gris solida en
                // vez de translucida, degradacion aceptable antes que romper
                // la Feature entera.
            }

            // El pivote de un Cube de Unity es su CENTRO — se ajusta con un
            // offset local para que "transform.position" (fijado en
            // ShowGhostAt a la esquina inferior de la celda) represente la
            // base del marco, igual que "blockPos" en el resto del mod.
            box.transform.localPosition = Vector3.zero;
            var wrapper = new GameObject("PortalMod_PlacementGhostRoot");
            box.transform.SetParent(wrapper.transform, false);
            box.transform.localPosition = new Vector3(0f, 1f, 0f);

            // Flecha pegada al piso del ghost (ver PortalDirectionArrow):
            // al vivir en el plano local XZ con la punta hacia +Z, y
            // "wrapper" ya rotado segun PortalOrientation.ToQuaternion
            // (mismo eje +Z=Norte, ver PortalOrientation), apunta correcto
            // sin rotacion extra aca.
            var arrow = PortalDirectionArrow.Build(ArrowSize);
            arrow.transform.SetParent(wrapper.transform, false);
            arrow.transform.localPosition = new Vector3(0f, 0.03f, 0f);

            wrapper.SetActive(false);
            return wrapper;
        }

        /// <summary>
        /// Best-effort: intenta resolver el nombre interno del item que el
        /// jugador local tiene equipado y confirma si es uno de los que
        /// colocan un portalBlock (ver "PortalItemNames"). Nunca lanza: ante
        /// cualquier fallo de reflection devuelve false (el ghost
        /// simplemente no se muestra ese frame/sesion).
        /// </summary>
        private static bool TryGetHeldPortalItemName(EntityPlayerLocal player, out string itemName)
        {
            itemName = null;

            try
            {
                var inventory = player.inventory;
                if (inventory == null)
                {
                    return false;
                }

                var invType = inventory.GetType();

                var resolvedName = TryResolveViaItemValue(inventory, invType) ?? TryResolveViaItemClass(inventory, invType);
                if (string.IsNullOrEmpty(resolvedName))
                {
                    return false;
                }

                if (!PortalItemNames.Contains(resolvedName))
                {
                    return false;
                }

                itemName = resolvedName;
                return true;
            }
            catch (Exception e)
            {
                if (!_detectionUnavailableLogged)
                {
                    _detectionUnavailableLogged = true;
                    API.LogWarning($"PortalPlacementGhost: fallo resolviendo el item equipado por reflection ({e.Message}); el ghost de colocacion no se mostrara en esta sesion.");
                }

                return false;
            }
        }

        private static string TryResolveViaItemValue(object inventory, Type invType)
        {
            if (!_holdingValueMemberCache.TryGetValue(invType, out var member))
            {
                member = FindFirstMember(invType, HoldingItemValueCandidates);
                _holdingValueMemberCache[invType] = member;
            }

            if (member == null)
            {
                return null;
            }

            var rawValue = GetMemberValue(member, inventory);
            if (rawValue == null)
            {
                return null;
            }

            // ItemValue es una struct real (ya en uso conceptualmente
            // paralelo a BlockValue, que este mod SI referencia directo —
            // ver PortalVisualFX.cs); se extrae el ItemClass asociado y de
            // ahi el nombre por el mismo camino generico de abajo, todo por
            // reflection para no atarse a la firma exacta de "ItemClass"
            // como propiedad de "ItemValue".
            var valueType = rawValue.GetType();
            var itemClassProp = valueType.GetProperty("ItemClass", BindingFlags.Public | BindingFlags.Instance)
                ?? valueType.GetProperty("itemClass", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var itemClass = itemClassProp?.GetValue(rawValue);
            return itemClass != null ? ExtractItemName(itemClass) : null;
        }

        private static string TryResolveViaItemClass(object inventory, Type invType)
        {
            if (!_holdingClassMemberCache.TryGetValue(invType, out var member))
            {
                member = FindFirstMember(invType, HoldingItemClassCandidates);
                _holdingClassMemberCache[invType] = member;
            }

            if (member == null)
            {
                return null;
            }

            var rawClass = GetMemberValue(member, inventory);
            return rawClass != null ? ExtractItemName(rawClass) : null;
        }

        private static string ExtractItemName(object itemClass)
        {
            var type = itemClass.GetType();

            if (!_itemNameMemberCache.TryGetValue(type, out var member))
            {
                member = FindFirstMember(type, ItemNameMemberCandidates);
                _itemNameMemberCache[type] = member;
            }

            if (member != null)
            {
                var value = GetMemberValue(member, itemClass) as string;
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            if (!_itemNameMethodCache.TryGetValue(type, out var method))
            {
                method = type.GetMethod("GetItemName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                _itemNameMethodCache[type] = method;
            }

            if (method != null && method.ReturnType == typeof(string))
            {
                return method.Invoke(itemClass, null) as string;
            }

            return null;
        }

        private static MemberInfo FindFirstMember(Type type, string[] candidates)
        {
            foreach (var name in candidates)
            {
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null && prop.GetIndexParameters().Length == 0)
                {
                    return prop;
                }

                var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        private static object GetMemberValue(MemberInfo member, object instance)
        {
            return member is PropertyInfo propInfo
                ? propInfo.GetValue(instance)
                : ((FieldInfo)member).GetValue(instance);
        }
    }
}
