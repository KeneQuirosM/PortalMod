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

        // Estilo del portal (Feature "Opcion A: 6 items separados"), resuelto
        // por PortalBlockPatch en el momento de la colocacion real (ver FIX
        // real en Block_OnBlockPlaceBefore_Patch) y llevado hasta aca para
        // pasarselo directo a RegisterPortal — evita que PortalManager tenga
        // que re-derivarlo mas tarde leyendo el bloque del mundo, lectura que
        // puede correr antes de que la colocacion haya terminado de
        // propagarse. Null para el modo Rename (no aplica: el portal ya
        // existe y ya tiene un estilo guardado).
        private static string _pendingStyle;

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

        /// <summary>Abre la ventana para asignar tag a un portal recien colocado. "style" viene ya resuelto desde el momento de la colocacion (ver Block_OnBlockPlaceBefore_Patch) — puede ser null si no se reconocio ningun estilo conocido.</summary>
        public static void OpenForNewPortal(EntityPlayer player, Vector3i blockPos, string style)
        {
            _pendingMode = Mode.NewPortal;
            _pendingPlayer = player;
            _pendingBlockPos = blockPos;
            _pendingCurrentTag = string.Empty;
            _pendingStyle = style;
            OpenWindow(player);
        }

        /// <summary>Abre la ventana para renombrar un portal ya existente.</summary>
        public static void OpenForRename(EntityPlayer player, Vector3i blockPos, string currentTag)
        {
            _pendingMode = Mode.Rename;
            _pendingPlayer = player;
            _pendingBlockPos = blockPos;
            _pendingCurrentTag = currentTag;
            _pendingStyle = null;
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
        // AUDITORIA (manejo de errores): este metodo lo invoca directamente
        // un callback de click de UI (OnPressed, ver WireButtons) — una
        // excepcion sin capturar aca no tiene ningun try/catch "de arriba"
        // que la contenga (a diferencia de la logica de PortalTeleport.Tick,
        // que ahora si esta protegida en varias capas), asi que se envuelve
        // todo el cuerpo.
        public void Confirm()
        {
            try
            {
                API.Log("[PortalMod] Confirm() ejecutado - tag: " + (_tagInput != null ? _tagInput.Text : "null"));

                // "_pendingPlayer" es un campo ESTATICO (ver comentario
                // arriba) que se llena en OpenForNewPortal/OpenForRename y se
                // lee aca, potencialmente frames/segundos despues (mientras
                // el jugador escribe el tag). Si el jugador se desconecta con
                // la ventana abierta, o el objeto Unity subyacente ya fue
                // destruido, "_pendingPlayer" puede quedar null (el operador
                // == de UnityEngine.Object trata los objetos destruidos como
                // null). Sin este chequeo, PortalIdentity.GetSteamId(null)
                // devuelve null, y PortalManager.RegisterPortal/RenamePortal
                // terminan llamando Dictionary.TryGetValue(null, ...) — eso
                // lanza ArgumentNullException.
                if (_pendingPlayer == null)
                {
                    API.LogWarning("Confirm() llamado sin un jugador pendiente valido (_pendingPlayer es null, probablemente se desconecto); cerrando ventana sin registrar.");
                    CloseWindow();
                    return;
                }

                var tag = _tagInput != null ? _tagInput.Text?.Trim() : null;

                if (string.IsNullOrEmpty(tag))
                {
                    PortalHud.ShowEmptyTagMessage(_pendingPlayer);
                    return;
                }

                // FIX real de causa raiz 3 (ver PortalNetSync.cs / TESTING.md
                // seccion 12.3/13): "PortalManager.Instance" solo es
                // autoritativo del lado servidor — un cliente remoto NUNCA
                // debe mutarlo directo (eso era exactamente el bug: el
                // registro se quedaba atrapado en la copia local del
                // cliente, sin que el servidor se enterara jamas). Si este
                // proceso ES el servidor (dedicado, host de listen server, o
                // singleplayer — ver ConnectionManager.Instance.IsServer),
                // se sigue aplicando directo, sin red, exactamente igual que
                // antes de este fix (mismo resultado, mismo mensaje, mismo
                // "_pendingStyle" resuelto en el momento de la colocacion).
                if (ConnectionManager.Instance != null && ConnectionManager.Instance.IsServer)
                {
                    var result = _pendingMode == Mode.NewPortal
                        ? PortalManager.Instance.RegisterPortal(_pendingPlayer, tag, _pendingBlockPos, _pendingStyle)
                        : PortalManager.Instance.RenamePortal(_pendingPlayer, _pendingBlockPos, tag);

                    ApplyResult(_pendingPlayer, result, tag, _pendingMode == Mode.Rename);
                    return;
                }

                // Cliente remoto: la validacion/registro real tiene que
                // pasar por el servidor. Se manda la solicitud y se espera
                // la respuesta (NetPackagePortalRequestResult ->
                // HandleServerResult, mas abajo) antes de mostrar cualquier
                // mensaje o cerrar la ventana — nunca se asume exito de
                // forma optimista.
                var package = NetPackageManager.GetPackage<NetPackagePortalRequest>().Setup(tag, _pendingBlockPos);
                ConnectionManager.Instance.SendToServer(package);
            }
            catch (Exception e)
            {
                API.LogError($"Excepcion en XUiPortalTag.Confirm(): {e}");
            }
        }

        /// <summary>
        /// Respuesta ya resuelta (local en el servidor, o recibida por red
        /// en un cliente remoto — ver HandleServerResult) para un intento de
        /// nombrar/renombrar: mismo mensaje de HUD y misma decision de
        /// cerrar o no la ventana que el codigo tenia antes del fix de
        /// sincronizacion por red (ver comentario de clase).
        /// </summary>
        private void ApplyResult(EntityPlayer player, PortalManager.RegisterResult result, string tag, bool wasRename)
        {
            if (!ShowResultMessage(player, result, tag, wasRename))
            {
                // Regla 5 (TagFull) o tag vacio: no cerrar la ventana, dejar
                // que el jugador corrija el tag.
                return;
            }

            CloseWindow();
        }

        /// <summary>
        /// Invocado desde NetPackagePortalRequestResult.ProcessPackage
        /// (cliente remoto, ver PortalNetSync.cs) cuando llega la respuesta
        /// real del servidor a un NetPackagePortalRequest mandado desde
        /// Confirm(). Estatico y sin depender de la instancia de
        /// XUiPortalTag que abrio la ventana originalmente (podria ya no
        /// existir/haberse recreado) — resuelve el jugador local y su XUi
        /// de nuevo, mismo patron que OpenWindow.
        /// </summary>
        internal static void HandleServerResult(PortalManager.RegisterResult result, string tag, bool wasRename)
        {
            var localPlayer = GameManager.Instance != null && GameManager.Instance.World != null
                ? GameManager.Instance.World.GetPrimaryPlayer()
                : null;

            if (localPlayer == null)
            {
                return;
            }

            if (!ShowResultMessage(localPlayer, result, tag, wasRename))
            {
                return;
            }

            var xui = (localPlayer as EntityPlayerLocal)?.PlayerUI?.xui;
            xui?.playerUI?.windowManager?.Close(WindowName);
        }

        /// <summary>
        /// Muestra el mensaje de HUD correspondiente a "result" (mismo
        /// switch que tenia Confirm() antes de este fix). Devuelve true si
        /// la ventana debe cerrarse (exito, huerfano, o el mensaje extra de
        /// "renombrado"), false si debe quedar abierta para que el jugador
        /// corrija el tag (TagFull/EmptyTag).
        /// </summary>
        private static bool ShowResultMessage(EntityPlayer player, PortalManager.RegisterResult result, string tag, bool wasRename)
        {
            switch (result)
            {
                case PortalManager.RegisterResult.Success:
                    PortalHud.ShowActiveMessage(player, tag);
                    break;
                case PortalManager.RegisterResult.SuccessOrphan:
                    PortalHud.ShowOrphanMessage(player, tag);
                    break;
                case PortalManager.RegisterResult.TagFull:
                    // Regla 5: no permitir un tercer portal con el mismo tag.
                    PortalHud.ShowTagInUseMessage(player);
                    return false;
                case PortalManager.RegisterResult.EmptyTag:
                    PortalHud.ShowEmptyTagMessage(player);
                    return false;
            }

            if (wasRename)
            {
                PortalHud.ShowRenamedMessage(player, tag);
            }

            return true;
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
