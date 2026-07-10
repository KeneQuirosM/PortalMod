using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Controller XUi para la ventana "windowPortalTag" (ver
    /// UIFrames/XUi_InGame/windows.xml). Usa el sistema de binding de XUi
    /// V3.0: la ventana usa el atributo "visible" (no "force_hide") y los
    /// valores se resuelven via GetBindingValue en vez del antiguo sistema de
    /// controls.xml.
    ///
    /// Flujo:
    ///   1. PortalBlockPatch llama OpenForNewPortal / OpenForRename, que
    ///      guarda el contexto pendiente (jugador + posicion + modo) y le pide
    ///      al XUiManager del jugador que muestre la ventana.
    ///   2. El jugador escribe un tag y pulsa "Confirmar" -> Confirm().
    ///   3. Confirm() llama a PortalManager.RegisterPortal / RenamePortal y
    ///      muestra el resultado en el HUD.
    /// </summary>
    public class XUiPortalTag : XUiController
    {
        private const string WindowName = "windowPortalTag";
        private const string TextInputId = "portalTagInput";

        private enum Mode
        {
            NewPortal,
            Rename
        }

        // Contexto de la operacion pendiente. Al ser un mod single-assembly con
        // una ventana por jugador local, un unico contexto estatico es
        // suficiente: solo el jugador local puede tener el foco de la UI.
        // TODO: en un servidor dedicado con multiples clientes, este estado
        // debe vivir por-cliente (por ejemplo indexado por entityId) en vez de
        // ser estatico global, ya que cada cliente renderiza su propio XUi.
        private static Mode _pendingMode;
        private static Vector3i _pendingBlockPos;
        private static EntityPlayer _pendingPlayer;
        private static string _pendingCurrentTag;

        private XUiC_TextInput _tagInput;

        public override void Init()
        {
            base.Init();

            // TODO: verificar en Assembly-CSharp V3.0 el tipo concreto del
            // control de texto en XUi (XUiC_TextInput es el nombre historico;
            // en V3.0 podria haberse renombrado dentro del namespace XUi).
            _tagInput = GetChildById(TextInputId) as XUiC_TextInput;
        }

        /// <summary>Abre la ventana para asignar tag a un portal recien colocado.</summary>
        public static void OpenForNewPortal(EntityPlayer player, Vector3i blockPos)
        {
            _pendingMode = Mode.NewPortal;
            _pendingPlayer = player;
            _pendingBlockPos = blockPos;
            _pendingCurrentTag = string.Empty;
            OpenWindow(player);
        }

        /// <summary>Abre la ventana para renombrar un portal ya existente.</summary>
        public static void OpenForRename(EntityPlayer player, Vector3i blockPos, string currentTag)
        {
            _pendingMode = Mode.Rename;
            _pendingPlayer = player;
            _pendingBlockPos = blockPos;
            _pendingCurrentTag = currentTag;
            OpenWindow(player);
        }

        private static void OpenWindow(EntityPlayer player)
        {
            var localPlayer = player as EntityPlayerLocal;
            if (localPlayer == null)
            {
                // Solo tiene sentido abrir UI para el jugador local que esta
                // ejecutando este cliente; en un jugador remoto la apertura
                // real deberia dispararse via NetPackage en su propio cliente.
                // TODO: implementar NetPackage dedicado (p.ej. NetPackagePortalOpenTagUI)
                // para servidores dedicados con multiples clientes.
                return;
            }

            // TODO: verificar en Assembly-CSharp V3.0 el acceso correcto al
            // XUiManager del jugador local. Camino historico:
            //   localPlayer.PlayerUI.xui.GetWindow(WindowName)
            var xui = localPlayer.PlayerUI?.xui;
            var window = xui?.GetWindow(WindowName);
            if (window == null)
            {
                API.LogWarning($"No se encontro la ventana XUi '{WindowName}'. Revisa UIFrames/XUi_InGame/windows.xml.");
                return;
            }

            var controller = window.Controller as XUiPortalTag;
            if (controller?._tagInput != null)
            {
                controller._tagInput.Text = _pendingCurrentTag ?? string.Empty;
            }

            xui.playerUI.windowManager.Open(WindowName, true, false, true);
        }

        // Invocado desde el binding de eventos del boton "Confirmar" en
        // windows.xml (ver <button ... controller="xuiPortalTag" onpress="Confirm" />).
        public void Confirm()
        {
            var tag = _tagInput != null ? _tagInput.Text?.Trim() : null;

            if (string.IsNullOrEmpty(tag))
            {
                PortalHud.ShowEmptyTagMessage(_pendingPlayer);
                return;
            }

            var steamId = PortalIdentity.GetSteamId(_pendingPlayer);
            var result = _pendingMode == Mode.NewPortal
                ? PortalManager.Instance.RegisterPortal(steamId, tag, _pendingBlockPos)
                : PortalManager.Instance.RenamePortal(steamId, _pendingBlockPos, tag);

            switch (result)
            {
                case PortalManager.RegisterResult.Success:
                    PortalHud.ShowActiveMessage(_pendingPlayer, tag);
                    break;
                case PortalManager.RegisterResult.SuccessOrphan:
                    PortalHud.ShowOrphanMessage(_pendingPlayer, tag);
                    break;
                case PortalManager.RegisterResult.TagFull:
                    // Regla 5: no permitir un tercer portal con el mismo tag.
                    PortalHud.ShowTagInUseMessage(_pendingPlayer);
                    return; // No cerrar la ventana: dejar que el jugador corrija el tag.
                case PortalManager.RegisterResult.EmptyTag:
                    PortalHud.ShowEmptyTagMessage(_pendingPlayer);
                    return;
            }

            if (_pendingMode == Mode.Rename && result != PortalManager.RegisterResult.TagFull)
            {
                PortalHud.ShowRenamedMessage(_pendingPlayer, tag);
            }

            CloseWindow();
        }

        // Invocado desde el binding de eventos del boton "Cancelar".
        public void Cancel()
        {
            CloseWindow();
        }

        private void CloseWindow()
        {
            xui.playerUI.windowManager.Close(WindowName);
        }

        // ========================================================================
        // Binding V3.0: expone valores dinamicos a windows.xml via atributos
        // tipo value="{portaltag.title}" en lugar del binding por indice de A19.
        // ========================================================================
        public override bool GetBindingValue(ref string value, string bindingName)
        {
            switch (bindingName)
            {
                case "portaltag.title":
                    value = Localization.Get(_pendingMode == Mode.Rename ? "xuiPortalTagRenameTitle" : "xuiPortalTagTitle");
                    return true;
                case "portaltag.placeholder":
                    value = Localization.Get("xuiPortalTagPlaceholder");
                    return true;
                case "portaltag.confirmlabel":
                    value = Localization.Get("xuiPortalTagConfirm");
                    return true;
                case "portaltag.cancellabel":
                    value = Localization.Get("xuiPortalTagCancel");
                    return true;
            }

            return base.GetBindingValue(ref value, bindingName);
        }
    }
}
