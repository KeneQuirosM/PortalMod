using System;
using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Feature 2 ("indicador de salida"): agrega una pequena flecha/chevron
    /// (">"), construida ENTERAMENTE con primitivas de Unity (sin depender
    /// de ningun AssetBundle/prefab nuevo — no hay ningun asset de flecha en
    /// Resources/, ver README.md), que apunta hacia el lado "frente" del
    /// portal (el mismo lado que usa PortalTeleport.FindLandingBlockPos para
    /// el aterrizaje — ver PortalOrientation, fuente unica de la convencion
    /// de rotacion) — asi el jugador puede ver, mirando el portal, exacta y
    /// visualmente por donde va a salir al usarlo.
    ///
    /// Se engancha en el MISMO hook que ya usa
    /// Block_OnBlockEntityTransformAfterActivated_Patch para activar las
    /// particulas embebidas del modelo (PortalBlockPatch.cs) — el unico
    /// punto ya confirmado en este mod donde el GameObject real del modelo
    /// del portal esta instanciado y activo en la escena. Como ese hook se
    /// dispara cada vez que el chunk del portal vuelve a quedar visible (no
    /// solo la primera vez, GameObject reciclado via GameObjectPool — ver
    /// comentario extenso ahi), "EnsureIndicator" chequea por nombre de hijo
    /// para no duplicar la flecha en cada re-activacion.
    ///
    /// LIMITACION CONOCIDA: es un indicador APROXIMADO (dos barras finas en
    /// forma de "&gt;"), no una flecha modelada a mano — construirla como
    /// asset real requeriria el pipeline de AssetBundles de SCore, fuera del
    /// alcance de este cambio. Tampoco se confirmo contra el juego real el
    /// pivote/origen exacto de "modelTransform" para cada uno de los 6
    /// modelos (gupFuturePortal1-6.unity3d) — el offset usado abajo es una
    /// aproximacion razonable (altura de pecho, centrado en la celda
    /// inferior del marco) que puede necesitar ajuste fino una vez probado
    /// en juego (ver TESTING.md).
    /// </summary>
    internal static class PortalExitIndicator
    {
        private const string IndicatorRootName = "PortalMod_ExitArrow";

        private const float BarLength = 0.55f;
        private const float BarThickness = 0.07f;
        private const float ChevronHalfAngleDeg = 35f;

        // Mismo violeta que TintColor="8033FF" (portalBlockActive, ver
        // blocks.xml) para que el indicador combine con el estado vinculado.
        private static readonly Color ArrowColor = new Color(0.5f, 0.2f, 1f, 1f);

        public static void EnsureIndicator(Transform modelTransform, byte rotation)
        {
            if (modelTransform == null)
            {
                return;
            }

            try
            {
                if (modelTransform.Find(IndicatorRootName) != null)
                {
                    // Ya existe (re-activacion por pooling de chunk, ver
                    // comentario de la clase) — nada que hacer.
                    return;
                }

                var forward = PortalOrientation.ForwardVector(rotation);
                var root = BuildArrow();
                root.name = IndicatorRootName;

                root.transform.SetParent(modelTransform, worldPositionStays: false);

                // Centrado horizontalmente sobre la celda inferior del marco
                // (MultiBlockDim="1,2,1", ver blocks.xml), a la altura
                // aproximada del pecho de un jugador parado, desplazado
                // hacia el lado "frente" para que quede claramente afuera de
                // la silueta del modelo.
                root.transform.localPosition = new Vector3(0.5f, 1.1f, 0.5f) + forward * 0.65f;

                // "forward" es un vector de MUNDO (compass, ver
                // PortalOrientation) mientras que "localRotation" es
                // relativo al padre — se resuelve con TransformDirection
                // inverso implicito usando rotacion de mundo y despues
                // convirtiendo a local, para no asumir que "modelTransform"
                // ya esta alineado 1:1 con los ejes de mundo.
                root.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
            catch (Exception e)
            {
                // Puramente decorativo/cosmetico: nunca debe romper el
                // renderizado del resto del modelo/particulas del portal.
                API.LogWarning($"PortalExitIndicator: fallo agregando el indicador de salida ({e.Message}).");
            }
        }

        private static GameObject BuildArrow()
        {
            var root = new GameObject("PortalMod_ExitArrowRoot");

            var tip = new Vector3(0f, 0f, BarLength * 0.5f);
            CreateChevronBar(root.transform, tip, ChevronHalfAngleDeg);
            CreateChevronBar(root.transform, tip, -ChevronHalfAngleDeg);

            return root;
        }

        /// <summary>
        /// Una de las dos barras del chevron ">": arranca en "tip" (la punta,
        /// hacia donde apunta el indicador) y se extiende hacia atras en un
        /// angulo de +-ChevronHalfAngleDeg respecto al eje central — las dos
        /// juntas (una a cada lado) forman la flecha.
        /// </summary>
        private static void CreateChevronBar(Transform parent, Vector3 tip, float angleDeg)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "ExitArrowBar";

            var collider = bar.GetComponent<Collider>();
            if (collider != null)
            {
                // Puramente decorativo: un collider real interferiria con el
                // raycast de mira/colocacion (EntityPlayerLocal.HitInfo, ver
                // PortalHoverFX/PortalPlacementGhost) sobre el portal.
                UnityEngine.Object.Destroy(collider);
            }

            ApplyUnlitColor(bar, ArrowColor);

            var barRotation = Quaternion.Euler(0f, angleDeg, 0f);
            var barForward = barRotation * Vector3.forward;

            bar.transform.SetParent(parent, false);
            bar.transform.localRotation = barRotation;
            bar.transform.localScale = new Vector3(BarThickness, BarThickness, BarLength);
            bar.transform.localPosition = tip - barForward * (BarLength * 0.5f);
        }

        /// <summary>
        /// Aplica un material simple y sin sombreado (visible incluso en
        /// interiores/de noche) al renderer de "go". "Shader.Find" puede
        /// devolver null si el shader pedido fue eliminado del build por
        /// stripping (riesgo real de Unity con shaders no referenciados por
        /// ningun asset del juego, no confirmable sin el build real) — en
        /// ese caso se prueban alternativas y, si ninguna resuelve, se deja
        /// el material por defecto de la primitiva (se ve igual pero opaco/
        /// sin tinte, mejor que romper la creacion del indicador entero).
        /// </summary>
        private static void ApplyUnlitColor(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Legacy Shaders/VertexLit") ?? Shader.Find("Diffuse");
            if (shader == null)
            {
                return;
            }

            var material = new Material(shader);
            if (material.HasProperty("_Color"))
            {
                material.color = color;
            }

            renderer.material = material;
        }
    }
}
