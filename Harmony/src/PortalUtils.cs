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
        /// <summary>
        /// Devuelve un identificador unico por jugador usado como clave primaria
        /// en PortalManager para separar los portales de cada jugador.
        /// NO es estable entre sesiones (ver comentario dentro del metodo) — es
        /// un fallback deliberado hasta confirmar el miembro de plataforma real
        /// en el Assembly-CSharp.dll de V3.0.
        /// </summary>
        public static string GetSteamId(EntityPlayer player)
        {
            if (player == null)
            {
                return null;
            }

            // "PlatformUserIdentifierAbs" NO existe en EntityPlayer del
            // Assembly-CSharp.dll real de V3.0 (error de compilacion confirmado).
            // No se tuvo acceso al DLL para confirmar el reemplazo correcto
            // (candidatos sin verificar: PlatformId/UserIdentifier expuestos via
            // ClientInfo en vez de EntityPlayer), asi que se usa el fallback
            // seguro entityId.ToString(), garantizado a compilar y existir en
            // toda la jerarquia Entity/EntityAlive/EntityPlayer.
            //
            // TRADEOFF: a diferencia de un identificador de plataforma real,
            // entityId NO es estable entre reconexiones/sesiones — el mismo
            // jugador puede recibir un entityId distinto la proxima vez que se
            // conecte, lo que rompe la asociacion de sus portales guardados en
            // PortalManager. Reemplazar por un identificador de plataforma real
            // en cuanto se confirme el miembro correcto en el DLL decompilado.
            return player.entityId.ToString();
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
    }
}
