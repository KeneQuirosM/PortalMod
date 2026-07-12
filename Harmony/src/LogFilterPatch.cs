using HarmonyLib;

namespace PortalMod
{
    /// <summary>
    /// Suprime del log el spam inofensivo "Unknown particle effect: &lt;id&gt;"
    /// que dispara el modelo gupFuturePortal6.unity3d (particulas internas
    /// rotas del propio AssetBundle, no controladas por este mod — ver FIX
    /// real en blocks.xml sobre por que se dejo de usar ese modelo como
    /// Model de portalBlockActive). Confirmado que no afecta el rendimiento
    /// (112 FPS estable en pruebas), solo ensucia el log.
    ///
    /// INTENTO DESCARTADO (pedido originalmente): Application.logMessageReceived,
    /// la API nativa de Unity para observar logs, NO PUEDE suprimir este
    /// mensaje. Decompilando ParticleEffect.GetDynamicTransform contra el
    /// Assembly-CSharp.dll real se confirmo que el mensaje se emite con
    /// Log.Error(string) — la clase propia del juego (LogLibrary.dll), NO
    /// UnityEngine.Debug directamente. Decompilando a su vez
    /// Log.Error/Log.masterLogStandalone se confirmo que en un build
    /// standalone (Application.isEditor=false — el caso real de cualquier
    /// partida jugada, a diferencia del editor de Unity) ese metodo escribe
    /// la linea directo a disco (WriteLine) y notifica sus propios listeners,
    /// SIN pasar nunca por UnityEngine.Debug.LogError/LogWarning/Log.
    /// Application.logMessageReceived solo se dispara para mensajes que SI
    /// pasan por el pipeline nativo de Unity, asi que nunca ve estos logs —
    /// no existe forma de suscribirse y filtrarlos por ese medio.
    ///
    /// FIX real: interceptar directamente la llamada con un Harmony Prefix
    /// sobre Log.Error(string), devolviendo false (cancela la ejecucion del
    /// metodo original, incluida la escritura a disco) cuando el texto
    /// contiene "Unknown particle effect". Mismo mecanismo de Harmony que ya
    /// usa el resto del mod, aplicado aqui sobre una clase del juego en vez
    /// de sobre Block/GameManager. Se confirmo (busqueda del string literal
    /// exacto en todo Assembly-CSharp.dll) que esta frase la genera un UNICO
    /// punto del codigo — este filtro no puede tragarse silenciosamente un
    /// error de particulas no relacionado.
    ///
    /// DEPENDENCIA OPCIONAL CON 0-SCore: si 0-SCore esta cargado, sus
    /// propias particulas SI existen/se resuelven (no se pudo verificar el
    /// mecanismo interno de SCore contra este Assembly-CSharp.dll vanilla,
    /// ya que SCore es un mod externo no instalado en este entorno de
    /// referencia — esto se toma como dado por el pedido del usuario), asi
    /// que este filtro NO debe registrarse en ese caso (suprimirlo
    /// ocultaria un error real y distinto en vez del ruido inofensivo de
    /// gupFuturePortal6 sin SCore). Por eso esta clase ya NO lleva
    /// [HarmonyPatch] automatico (PatchAll la ignora): se registra
    /// manualmente via Register(), llamado desde API.InitMod solo cuando
    /// ModManager.ModLoaded("0-SCore") es false — ver API.cs.
    /// </summary>
    internal static class Log_Error_ParticleFilter_Patch
    {
        /// <summary>Aplica el patch manualmente. Ver nota de la clase sobre por que no usa [HarmonyPatch] + PatchAll.</summary>
        internal static void Register(Harmony harmony)
        {
            var original = AccessTools.Method(typeof(global::Log), "Error", new[] { typeof(string) });
            var prefix = new HarmonyMethod(typeof(Log_Error_ParticleFilter_Patch), nameof(Prefix));
            harmony.Patch(original, prefix);
        }

        private static bool Prefix(string _txt)
        {
            return _txt == null || !_txt.Contains("Unknown particle effect");
        }
    }
}
