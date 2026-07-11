using System;
using UnityEngine;

namespace PortalMod
{
    /// <summary>
    /// Controller XUi para la ventana "windowPortalTag" (ver
    /// Config/XUi_InGame/windows.xml). Usa el sistema de binding de XUi
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
        private const string ConfirmButtonId = "confirmButton";
        private const string CancelButtonId = "cancelButton";

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
        private bool _buttonsWired;

        public override void Init()
        {
            base.Init();
            API.Log("[PortalMod] XUiPortalTag.Init() llamado");

            // XUiC_TextInput confirmado por reflection contra el
            // Assembly-CSharp.dll real como la clase controller real para el
            // tag <textfield> de windows.xml (unico tipo que matchea
            // "TextField"/"TextInput" en todo el ensamblado).
            _tagInput = GetChildById(TextInputId) as XUiC_TextInput;

            WireButtons();
        }

        // FIX real (Confirm() nunca se ejecutaba): decompilando
        // XUiController.Init() se confirmo que es virtual void Init(), y que
        // la propia base.Init() ya recorre "children" e invoca Init() en cada
        // hijo — por diseño, cuando esta clase llama a base.Init() los hijos
        // directos YA deberian existir (se registran solos via el setter de
        // "Parent", que hace parent.children.Add(this), durante el parseo
        // recursivo de windows.xml, ANTES de que XUi llame a ningun Init()).
        // No existe "CreateChildren()" en XUiController (confirmado por
        // reflection: solo "Init" y "OnOpen" son puntos de extension
        // reales). Aun asi, para blindarse contra cualquier caso en que
        // Init() corriera antes de que los botones (expandidos desde el
        // template <simplebutton> de XUi_Common/templates.xml) esten
        // listos, se reintenta la conexion en OnOpen() — que se dispara
        // recien cuando la ventana se muestra de verdad, momento en el que
        // el arbol completo ya esta garantizado. _buttonsWired evita
        // suscribir el evento OnPress dos veces si Init() ya funciono bien.
        private void WireButtons()
        {
            if (_buttonsWired)
            {
                return;
            }

            // Confirmado por reflection/decompile contra Assembly-CSharp.dll real:
            //   - GetChildById(string) NO es generico (devuelve XUiController
            //     base) — el cast "as XUiC_SimpleButton" de abajo es
            //     obligatorio, "GetChildById<T>(...)" no existe.
            //   - FIX real (Confirm() no se ejecutaba pese a que los botones
            //     SI se encontraban): XUiC_SimpleButton declara su PROPIO
            //     evento "OnPressed" (no el "OnPress" heredado de
            //     XUiController, que nunca se dispara para este control). El
            //     click real en el <button name="clickable"> interno del
            //     template <simplebutton> se maneja en
            //     XUiC_SimpleButton.Btn_OnPress, que literalmente hace
            //     "this.OnPressed?.Invoke(this, _mouseButton);" — nunca toca
            //     "OnPress". Mismo tipo de delegado que antes
            //     (XUiEvent_OnPressEventHandler: void(XUiController _sender,
            //     int _mouseButton)), asi que el lambda no cambia.
            var confirmBtn = GetChildById(ConfirmButtonId) as XUiC_SimpleButton;
            API.Log("[PortalMod] confirmButton encontrado: " + (confirmBtn != null));

            var cancelBtn = GetChildById(CancelButtonId) as XUiC_SimpleButton;
            API.Log("[PortalMod] cancelButton encontrado: " + (cancelBtn != null));

            if (confirmBtn == null || cancelBtn == null)
            {
                // Todavia no existen (por ejemplo si esto corrio desde
                // Init()); se reintenta en OnOpen().
                return;
            }

            confirmBtn.OnPressed += (_sender, _mouseButton) => Confirm();
            cancelBtn.OnPressed += (_sender, _mouseButton) => Cancel();
            _buttonsWired = true;
        }

        public override void OnOpen()
        {
            base.OnOpen();
            WireButtons();
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
            if (xui == null)
            {
                API.LogWarning("localPlayer.PlayerUI.xui es null; no se pudo abrir 'windowPortalTag'.");
                return;
            }

            // FIX real (problema de timing confirmado con reflection contra
            // Assembly-CSharp.dll real, XUi.loadAsync): "Parsing all window
            // groups completed" en el log NO significa que XUi ya termino de
            // inicializarse — ese mensaje se loguea DESPUES de parsear el XML
            // pero ANTES de llamar Init() en cada XUiWindowGroup (un bucle
            // que puede abarcar varios frames, con "yield return null" entre
            // grupos). Recien al terminar ese bucle completo XUi pone
            // IsReady=true, y el setter de IsReady dispara OnBuilt (un
            // System.Action, confirmado por reflection) exactamente una vez
            // en ese momento. Si un jugador coloca/activa un portalBlock
            // antes de eso, GetWindow(WindowName) puede devolver null aunque
            // la ventana SI este definida en windows.xml. En vez de fallar
            // ahi, si xui.IsReady es false nos suscribimos a OnBuilt y
            // reintentamos la apertura real cuando XUi avise que ya esta listo.
            if (xui.IsReady)
            {
                OpenWindowNow(xui);
            }
            else
            {
                API.Log("XUi todavia no esta listo (IsReady=false); esperando XUi.OnBuilt para abrir 'windowPortalTag'.");
                xui.OnBuilt += OnXuiBuilt;

                void OnXuiBuilt()
                {
                    xui.OnBuilt -= OnXuiBuilt;
                    OpenWindowNow(xui);
                }
            }
        }

        private static void OpenWindowNow(XUi xui)
        {
            var window = xui.GetWindow(WindowName);
            if (window == null)
            {
                API.LogWarning($"No se encontro la ventana XUi '{WindowName}'. Revisa Config/XUi_InGame/windows.xml.");
                return;
            }

            var controller = window.Controller as XUiPortalTag;
            if (controller?._tagInput != null)
            {
                controller._tagInput.Text = _pendingCurrentTag ?? string.Empty;
            }

            // FIX real (el cursor del mouse no aparecia para poder hacer click
            // en los botones): decompilando GameManager.gmUpdate contra el
            // Assembly-CSharp.dll real se confirmo que la visibilidad del
            // cursor depende cada frame de isAnyCursorWindowOpen(), que
            // revisa windowManager.IsModalWindowOpen() ||
            // windowManager.IsCursorWindowOpen(). La segunda rama requiere el
            // flag "alwaysUsesMouseCursor" en true, pero se confirmo (busqueda
            // en TODO el ensamblado) que NINGUN window group de XUi lo pone en
            // true jamas — solo dos ventanas legacy no relacionadas
            // (GUIWindowConsole, GUIWindowScreenshotText) lo usan. La unica
            // rama real que aplica a ventanas XUi normales es
            // IsModalWindowOpen(), que exige abrir la ventana con
            // "_bModal: true". Confirma esto XUiC_MessageBoxWindowGroup.ShowOk
            // (el popup de confirmacion vanilla mas parecido al nuestro), que
            // usa "bool _modal = true" por defecto. Antes se abria con el
            // valor por defecto de bool relleno por reflection (false), asi
            // que la ventana nunca quedaba "modal" y el cursor nunca se
            // habilitaba aunque los botones se vieran perfectamente.
            // GUIWindowManager.Open(string, bool) ya esta confirmado por
            // reflection, asi que ya no hace falta invocar "Open" via
            // reflection como antes.
            xui.playerUI.windowManager.Open(WindowName, true);
        }

        // Invocado desde el boton "confirmButton" (<simplebutton> en
        // windows.xml) via el OnPressed conectado en WireButtons().
        public void Confirm()
        {
            API.Log("[PortalMod] Confirm() ejecutado - tag: " + (_tagInput != null ? _tagInput.Text : "null"));

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

        // Invocado desde el boton "cancelButton" via el OnPressed conectado en WireButtons().
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
        //
        // El compilador real confirmo que XUiController.GetBindingValue ya NO es
        // virtual en V3.0 (CS0506/CS0115), asi que "override" no compila. Se usa
        // "new" para poder seguir declarando un metodo con el mismo nombre.
        // RIESGO FUNCIONAL: "new" oculta el metodo en vez de sobrescribirlo — si
        // el sistema de binding interno de XUi invoca GetBindingValue a traves de
        // una referencia tipada como "XUiController" (muy probable, ya que ese es
        // el tipo generico que maneja el resolvedor de bindings), esa llamada
        // ejecutara la implementacion BASE, no esta, y los bindings
        // "portaltag.*" de abajo nunca se resolveran en tiempo real aunque el
        // codigo compile. Verificar en el juego si el titulo/placeholder de la
        // ventana se actualizan correctamente; si no, la propiedad tiene que
        // exponerse por otro mecanismo (por ejemplo un ViewComponent/binding
        // distinto, o revisando si XUiController expone un punto de extension
        // diferente en V3.0 para esto).
        // ========================================================================
        public new bool GetBindingValue(ref string value, string bindingName)
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
