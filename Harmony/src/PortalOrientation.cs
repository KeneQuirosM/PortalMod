using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Fuente unica de verdad para la Feature "rotacion real del portal +
    /// indicador de salida": convierte entre "hacia donde miraba el jugador
    /// al colocar el portal" y el valor que se escribe en
    /// "BlockValue.rotation" (ya usado, sin re-asignar, por PortalVisualFX.cs
    /// — ver "newBv.rotation = currentBv.rotation" ahi), y de vuelta a un
    /// vector de mundo ("hacia donde da el frente del portal") para calcular
    /// el punto de salida (PortalTeleport.FindLandingBlockPos) y el
    /// indicador visual (PortalExitIndicator).
    ///
    /// ADVERTENCIA DE CONFIANZA (mismo espiritu que el resto del mod, ver
    /// comentarios "TODO: verificar en Assembly-CSharp V3.0..." en otros
    /// archivos): NO se pudo confirmar contra el Assembly-CSharp.dll real
    /// que mapeo interno usa el motor entre "BlockValue.rotation" (0-3) y la
    /// orientacion visual real que le aplica a un bloque "Shape=ModelEntity"
    /// como portalBlock. Este archivo define su PROPIA convencion (rotation
    /// 0=Norte, 1=Este, 2=Sur, 3=Oeste, sentido horario, ver
    /// "ForwardOffset"/"ToQuaternion" abajo) que es INTERNAMENTE consistente
    /// (todo el mod lee y escribe rotacion usando estas mismas funciones), lo
    /// cual garantiza que el portal siempre rota junto con el jugador que lo
    /// coloca sin importar si esta convencion coincide con la real del motor
    /// — lo unico que puede quedar desalineado si NO coincide es el sentido
    /// visual exacto (por ejemplo que el indicador de salida/el aterrizaje
    /// "al frente" terminen mirando 90/180 grados distinto de lo que el
    /// modelo 3D muestra visualmente). Si al probar en el juego real el
    /// indicador/aterrizaje no coincide con el frente visual del modelo, el
    /// arreglo es unicamente ACA: ajustar el orden Norte/Este/Sur/Oeste (o
    /// el signo) de "ForwardOffset"/"ToQuaternion" — ver TESTING.md seccion
    /// "Feature 2" para el procedimiento de calibracion.
    /// </summary>
    internal static class PortalOrientation
    {
        public const byte RotationNorth = 0;
        public const byte RotationEast = 1;
        public const byte RotationSouth = 2;
        public const byte RotationWest = 3;

        /// <summary>
        /// Convierte el yaw (grados, eje Y de Transform/EntityAlive.rotation)
        /// del jugador en el momento de colocar el bloque a uno de los 4
        /// valores de rotacion de arriba, redondeando al cardinal mas
        /// cercano (igual criterio "snap a 90 grados" que usa cualquier
        /// sistema de colocacion con rotacion discreta).
        /// </summary>
        public static byte ComputeRotationFromPlayerFacing(EntityAlive player)
        {
            if (player == null)
            {
                return RotationNorth;
            }

            var yaw = NormalizeYaw(player.rotation.y);

            if (yaw >= 315f || yaw < 45f)
            {
                return RotationNorth;
            }

            if (yaw < 135f)
            {
                return RotationEast;
            }

            if (yaw < 225f)
            {
                return RotationSouth;
            }

            return RotationWest;
        }

        private static float NormalizeYaw(float yaw)
        {
            yaw %= 360f;
            if (yaw < 0f)
            {
                yaw += 360f;
            }

            return yaw;
        }

        /// <summary>
        /// Desplazamiento de UN bloque en la direccion "frente" del portal
        /// para esta rotacion — usado tanto para el punto de aterrizaje
        /// (PortalTeleport.FindLandingBlockPos) como para posicionar el
        /// indicador de salida (PortalExitIndicator).
        /// </summary>
        public static Vector3i ForwardOffset(byte rotation)
        {
            switch (rotation & 0x03)
            {
                case RotationNorth:
                    return new Vector3i(0, 0, 1);
                case RotationEast:
                    return new Vector3i(1, 0, 0);
                case RotationSouth:
                    return new Vector3i(0, 0, -1);
                default:
                    return new Vector3i(-1, 0, 0);
            }
        }

        /// <summary>Version en Vector3 (mundo) del mismo desplazamiento — usado para orientar el indicador/ghost.</summary>
        public static Vector3 ForwardVector(byte rotation)
        {
            var offset = ForwardOffset(rotation);
            return new Vector3(offset.x, offset.y, offset.z);
        }

        /// <summary>Rotacion de mundo equivalente, usada por el ghost de colocacion (PortalPlacementGhost) para orientar el preview.</summary>
        public static Quaternion ToQuaternion(byte rotation)
        {
            switch (rotation & 0x03)
            {
                case RotationNorth:
                    return Quaternion.Euler(0f, 0f, 0f);
                case RotationEast:
                    return Quaternion.Euler(0f, 90f, 0f);
                case RotationSouth:
                    return Quaternion.Euler(0f, 180f, 0f);
                default:
                    return Quaternion.Euler(0f, 270f, 0f);
            }
        }

        /// <summary>
        /// Reescribe UNICAMENTE el campo "rotation" del BlockValue ya
        /// colocado en "pos" (sin tocar ningun otro campo del BlockValue —
        /// se clona la struct completa antes de mutar, a diferencia de
        /// PortalVisualFX.SetBlockState, que SI necesita reconstruir un
        /// BlockValue nuevo porque ahi cambia de "type"/bloque). Mismo
        /// mecanismo (World.SetBlockRPC) ya usado y confirmado en
        /// PortalVisualFX.cs. Como el "type" (ID de bloque) queda IGUAL,
        /// esto NO dispara Block.OnBlockRemoved (el gate real, confirmado
        /// por decompilacion en Chunk.SetBlock, compara "type" — ver FIX
        /// real en PortalVisualFX.cs), asi que no corta ningun cableado
        /// electrico ya conectado (no debería aplicar en colocacion recien
        /// hecha, pero es la misma garantia que ya se aprovecha ahi).
        /// </summary>
        public static void ApplyPlayerFacingRotation(World world, Vector3i pos, EntityAlive player)
        {
            if (world == null)
            {
                return;
            }

            var currentBv = world.GetBlock(pos);
            if (currentBv.Block == null)
            {
                return;
            }

            var desiredRotation = ComputeRotationFromPlayerFacing(player);
            if (currentBv.rotation == desiredRotation)
            {
                return;
            }

            var newBv = currentBv;
            newBv.rotation = desiredRotation;
            world.SetBlockRPC(pos, newBv);

            API.Log($"[PortalMod] Rotacion aplicada en {pos}: rotation={desiredRotation} (jugador mirando yaw={player?.rotation.y:F1}).");
        }
    }
}
