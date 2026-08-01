using System;
using System.Globalization;

namespace PortalMod
{
    /// <summary>
    /// Resuelve el ID de "party"/grupo de un jugador, si tiene uno (Feature
    /// "portales por party" — ver PortalManager.GetPortalKey).
    ///
    /// CONFIRMADO CONTRA Assembly-CSharp.dll REAL (analisis de causa raiz del
    /// bug reportado "los companeros de party no pueden usar mis portales" /
    /// "ninguno puede usar los portales del otro" — ver TESTING.md, seccion
    /// "Multiplayer / servidores dedicados"): a diferencia de la version
    /// anterior de este archivo (que probaba varios nombres de propiedad por
    /// reflection, sin haber podido confirmar NINGUNO contra el DLL real —
    /// ver historial en git), esta vez se inspecciono el
    /// Assembly-CSharp.dll instalado directamente. El sistema de party SI
    /// existe en V3.0.1 y es simple y publico:
    ///   - EntityPlayer.Party (propiedad publica, tipo "Party", declarada
    ///     directo en EntityPlayer — no en una clase base — null si el
    ///     jugador no esta en ninguna party).
    ///   - Party.PartyID (campo publico, Int32) — el identificador real.
    ///
    /// LA VERSION ANTERIOR FALLABA SIEMPRE (para CUALQUIER jugador, en
    /// CUALQUIER party real, no un caso raro) por DOS bugs distintos, ambos
    /// confirmados ahora contra el DLL real:
    ///   1) SI encontraba la propiedad "Party" (estaba en la lista de
    ///      candidatos y existe de verdad), pero despues buscaba el ID
    ///      DENTRO de ese objeto con los candidatos "PartyId"/"Id"/
    ///      "GroupId"/"TeamId"/"partyId"/"id" — el campo real es "PartyID"
    ///      (mayuscula "ID", no "Id"). Type.GetField/GetProperty son
    ///      case-SENSITIVE por defecto: "PartyId" nunca hace match contra
    ///      "PartyID", asi que la extraccion del ID fallaba SIEMPRE incluso
    ///      cuando el objeto Party real ya se habia encontrado.
    ///   2) El fallback por "PartyManager" (clase estatica/singleton) SI
    ///      encontraba la clase real "PartyManager" y su metodo real
    ///      "GetParty(Int32)", pero luego fallaba resolviendo la instancia
    ///      singleton: buscaba una propiedad o campo estatico llamado
    ///      "Instance" — el real es la propiedad "Current" (el campo interno
    ///      es "instance", minuscula). Como GetParty(Int32) no es estatico,
    ///      sin poder resolver la instancia el candidato se descartaba en
    ///      silencio.
    /// Resultado neto: TryGetPartyId devolvia false SIEMPRE — GetPortalKey
    /// nunca devolvia "party:X" para nadie, todos los jugadores se trataban
    /// como solitarios sin importar su party real. Esto explica los
    /// sintomas reportados de forma COMPLETA y DETERMINISTICA (nunca
    /// funciono para nadie, no era una condicion de carrera intermitente).
    ///
    /// Con el miembro real confirmado, ya no hace falta ninguna capa de
    /// reflection (ni su cache): se llama directo, mas simple y sin el
    /// costo de reflection en un metodo que corre potencialmente cada pocos
    /// segundos por jugador (ver PortalManager.CheckPartyMembershipChanged).
    /// </summary>
    internal static class PortalParty
    {
        /// <summary>
        /// Devuelve true y el PartyID real (como string) si el jugador esta
        /// en una party; false si no. Ver comentario de la clase sobre la
        /// llamada directa confirmada contra el Assembly-CSharp.dll real.
        /// </summary>
        internal static bool TryGetPartyId(EntityPlayer player, out string partyId)
        {
            partyId = null;

            if (player == null)
            {
                return false;
            }

            try
            {
                var party = player.Party;
                if (party == null)
                {
                    return false;
                }

                partyId = party.PartyID.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception e)
            {
                // Puramente best-effort: un fallo aca NUNCA debe impedir que
                // el resto del mod funcione, solo degrada a "sin party".
                API.LogWarning($"PortalParty.TryGetPartyId fallo ({e.Message}); se trata al jugador como solitario.");
                return false;
            }
        }
    }
}
