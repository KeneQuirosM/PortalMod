using System;
using System.Linq;
using System.Reflection;
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

        public override void Init()
        {
            base.Init();

            // XUiC_TextInput confirmado por reflection contra el
            // Assembly-CSharp.dll real como la clase controller real para el
            // tag <textfield> de windows.xml (unico tipo que matchea
            // "TextField"/"TextInput" en todo el ensamblado).
            _tagInput = GetChildById(TextInputId) as XUiC_TextInput;

            // Conecta los botones <simplebutton> de windows.xml (ver FIX real
            // #2 documentado ahi) a Confirm()/Cancel(). Confirmado por
            // reflection contra Assembly-CSharp.dll real:
            //   - GetChildById(string) NO es generico (devuelve XUiController
            //     base) — el cast "as XUiC_SimpleButton" de abajo es
            //     obligatorio, "GetChildById<T>(...)" no existe.
            //   - XUiC_SimpleButton.OnPress es un
            //     XUiEvent_OnPressEventHandler: void(XUiController _sender,
            //     int _mouseButton).
            var confirmBtn = GetChildById(ConfirmButtonId) as XUiC_SimpleButton;
            if (confirmBtn != null)
            {
                confirmBtn.OnPress += (_sender, _mouseButton) => Confirm();
            }

            var cancelBtn = GetChildById(CancelButtonId) as XUiC_SimpleButton;
            if (cancelBtn != null)
            {
                cancelBtn.OnPress += (_sender, _mouseButton) => Cancel();
            }
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
                API.LogWarning($"No se encontro la ventana XUi '{WindowName}'. Revisa UIFrames/XUi_InGame/windows.xml.");
                return;
            }

            var controller = window.Controller as XUiPortalTag;
            if (controller?._tagInput != null)
            {
                controller._tagInput.Text = _pendingCurrentTag ?? string.Empty;
            }

            OpenWindowGroupSafely(xui.playerUI.windowManager, WindowName);
        }

        /// <summary>
        /// El compilador real (Assembly-CSharp.dll V3.0) reporto que
        /// "Open(string, bool, bool, bool)" (4 argumentos) ya no existe en el
        /// window manager, pero no se tuvo acceso al DLL para confirmar la
        /// firma correcta (candidatos sin verificar: XUiWindowGroupManager.Open
        /// / XUiManager.Open con distinta cantidad/orden de parametros).
        /// En vez de adivinar una segunda firma a ciegas, se invoca "Open" por
        /// reflection: se toma el primer metodo publico llamado "Open" cuyo
        /// primer parametro sea un string (el nombre de la ventana), y se
        /// completan los parametros restantes con sus valores por defecto
        /// (o el default del tipo si no tienen uno). Esto compila
        /// incondicionalmente y sigue funcionando aunque cambie la cantidad
        /// exacta de argumentos.
        /// TODO: reemplazar por una llamada directa en cuanto se confirme la
        /// firma real (decompilar XUiWindowGroupManager con ILSpy/dnSpy).
        /// </summary>
        private static void OpenWindowGroupSafely(object windowManager, string windowName)
        {
            if (windowManager == null)
            {
                API.LogWarning($"windowManager es null; no se pudo abrir '{windowName}'.");
                return;
            }

            var method = windowManager.GetType().GetMethods()
                .FirstOrDefault(m => m.Name == "Open" &&
                                      m.GetParameters().Length > 0 &&
                                      m.GetParameters()[0].ParameterType == typeof(string));

            if (method == null)
            {
                API.LogWarning($"No se encontro un metodo Open(string, ...) en {windowManager.GetType().Name}; no se pudo abrir '{windowName}'.");
                return;
            }

            var parameters = method.GetParameters();
            var args = new object[parameters.Length];
            args[0] = windowName;
            for (var i = 1; i < parameters.Length; i++)
            {
                var p = parameters[i];
                args[i] = p.HasDefaultValue ? p.DefaultValue
                    : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType)
                    : null;
            }

            method.Invoke(windowManager, args);
        }

        // Invocado desde el boton "confirmButton" (<simplebutton> en
        // windows.xml) via el OnPress conectado en Init() de esta clase.
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

        // Invocado desde el boton "cancelButton" via el OnPress conectado en Init().
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
