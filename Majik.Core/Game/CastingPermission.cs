using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.StateMachine;

namespace Majik.Core.Game;

/// <summary>
/// CR 117.1 — when can a card be cast? Sorceries need:
///   - the caster has priority
///   - it is the caster's turn
///   - the current phase is a Main phase
///   - the stack is empty
/// Instants ignore those restrictions (any priority window works).
///
/// Lands aren't spells (CR 305) — see <see cref="LandDropTracker"/>.
/// </summary>
public static class CastingPermission
{
    public static bool CanCast(
        ICard card,
        Player caster,
        Player activePlayer,
        PhaseStateType currentPhase,
        bool stackIsEmpty,
        out string reason)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));

        if (card.HasType(CardType.Land))
        {
            reason = "lands aren't cast as spells (see LandDropTracker)";
            return false;
        }

        if (card.HasType(CardType.Sorcery))
        {
            if (!ReferenceEquals(caster, activePlayer))
            {
                reason = "sorceries can only be cast on your turn";
                return false;
            }
            if (currentPhase != PhaseStateType.Main)
            {
                reason = "sorceries can only be cast during a main phase";
                return false;
            }
            if (!stackIsEmpty)
            {
                reason = "sorceries can only be cast when the stack is empty";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }
}
