using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Flecha de direccion usada por PortalPlacementGhost (unica y
    /// exclusivamente durante la colocacion, el "ghost" — a pedido explicito
    /// del usuario NO se muestra en el portal ya colocado; existio una
    /// version anterior, PortalExitIndicator, que se elimino): UNA sola
    /// malla plana con forma de flecha simple (vastago + punta triangular),
    /// igual al patron visual de las puertas del juego al colocarlas (una
    /// unica flecha clara indicando el lado de apertura/salida). Se busco
    /// por reflection en Assembly-CSharp.dll un asset/sistema de flecha
    /// reutilizable de las puertas (BlockPlacementDoor/BlockDoor) para
    /// clonar el mismo mecanismo, pero esas clases solo contienen logica de
    /// ROTACION de colocacion, no un componente visual de flecha separado —
    /// la flecha que se ve en las puertas es parte del propio
    /// modelo/textura 3D de la puerta, no un sistema generico enganchable.
    /// Por eso esta clase construye su PROPIA flecha (sin depender de
    /// ningun asset): una unica malla, pegada CONTRA la superficie (el piso
    /// del marco) con un offset minimo solo para evitar z-fighting, asi se
    /// lee como un decal integrado al ghost y no como piezas sueltas en el
    /// aire (defecto reportado de un primer intento con dos barras en forma
    /// de chevron ">" flotando lejos del modelo).
    /// </summary>
    internal static class PortalDirectionArrow
    {
        // Mismo violeta que TintColor="8033FF" (portalBlockActive, ver
        // blocks.xml) para que el indicador combine con el estado vinculado.
        private static readonly Color ArrowColor = new Color(0.5f, 0.2f, 1f, 1f);

        /// <summary>
        /// Crea la flecha ya lista para pegarse "al piso": la malla vive en
        /// el plano XZ (normal +Y, hacia arriba) con la punta apuntando
        /// hacia el eje local +Z. Como PortalOrientation.ForwardOffset usa
        /// exactamente ese mismo eje (+Z = Norte, ver PortalOrientation),
        /// basta con que el padre tenga la rotacion de mundo/local correcta
        /// (PortalOrientation.ToQuaternion / Quaternion.LookRotation) para
        /// que la flecha apunte al lado "frente" real sin rotacion extra
        /// aca.
        /// </summary>
        public static GameObject Build(float size)
        {
            var go = new GameObject("PortalMod_DirectionArrow");

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.mesh = BuildArrowMesh(size);

            var meshRenderer = go.AddComponent<MeshRenderer>();
            ApplyUnlitColor(meshRenderer, ArrowColor);

            return go;
        }

        private static Mesh BuildArrowMesh(float size)
        {
            var headWidth = size;
            var headHeight = size * 0.6f;
            var shaftWidth = size * 0.36f;
            var shaftHeight = size * 0.5f;

            var vertices = new[]
            {
                // Vastago (rectangulo), extremo trasero centrado en el origen.
                new Vector3(-shaftWidth * 0.5f, 0f, -shaftHeight), // 0
                new Vector3(shaftWidth * 0.5f, 0f, -shaftHeight),  // 1
                new Vector3(shaftWidth * 0.5f, 0f, 0f),            // 2
                new Vector3(-shaftWidth * 0.5f, 0f, 0f),           // 3
                // Punta (triangulo), apuntando hacia +Z.
                new Vector3(-headWidth * 0.5f, 0f, 0f),            // 4
                new Vector3(headWidth * 0.5f, 0f, 0f),             // 5
                new Vector3(0f, 0f, headHeight),                   // 6 (punta)
            };

            // Triangulos duplicados con winding invertido: no se pudo
            // confirmar contra el juego real desde que angulo exacto queda
            // visible la cara del decal una vez aplicada la rotacion del
            // ghost/modelo, asi que se renderizan ambas caras en vez de
            // arriesgar una flecha invisible desde ciertos angulos.
            var triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 6, 5,
                0, 1, 2, 0, 2, 3,
                4, 5, 6,
            };

            var mesh = new Mesh { name = "PortalMod_ArrowMesh" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Aplica un material simple y sin sombreado (visible incluso en
        /// interiores/de noche). "Shader.Find" puede devolver null si el
        /// shader pedido fue eliminado del build por stripping (riesgo real
        /// de Unity con shaders no referenciados por ningun asset del juego,
        /// no confirmable sin el build real) — en ese caso se prueban
        /// alternativas y, si ninguna resuelve, se deja el material por
        /// defecto (se ve igual pero opaco/sin tinte, mejor que romper la
        /// creacion de la flecha entera).
        /// </summary>
        private static void ApplyUnlitColor(Renderer renderer, Color color)
        {
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
