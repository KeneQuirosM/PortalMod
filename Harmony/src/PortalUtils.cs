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
        /// Devuelve un identificador estable y unico por jugador, valido tanto
        /// en Steam como en otras plataformas (Xbox/PS/EGS), usado como clave
        /// primaria en PortalManager para separar los portales de cada jugador.
        /// </summary>
        public static string GetSteamId(EntityPlayer player)
        {
            if (player == null)
            {
                return null;
            }

            // TODO: verificar en Assembly-CSharp V3.0 el nombre exacto del
            // miembro. En builds anteriores EntityPlayer expone
            // "PlatformUserIdentifierAbs" (PlatformUserIdentifierAbs.CombinedString)
            // como identificador cross-platform estable; "InputFromPlayerData"
            // y "playerId" tambien han existido en distintas versiones.
            var platformId = player.PlatformUserIdentifierAbs;
            if (platformId != null && !string.IsNullOrEmpty(platformId.CombinedString))
            {
                return platformId.CombinedString;
            }

            // Fallback: entityId no es estable entre sesiones pero evita null
            // en escenarios de testing local / single player sin plataforma.
            return "entity_" + player.entityId;
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
    }
}
