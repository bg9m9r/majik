using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Bot-side helper that surfaces the alternative casting costs (CR 118.9)
/// a card is eligible for in the current game state. Decouples
/// <see cref="HeuristicBotAgent"/> from the per-card binder pipeline:
/// instead of the bot needing to know how to recognize flashback /
/// spectacle / evoke / pitch on every card, callers inject a probe that
/// reads whatever card metadata is available (oracle text, compiled
/// templates, hand-rolled rules) and returns concrete
/// <see cref="IAlternativeCost"/> instances.
///
/// The bot filters returned candidates through
/// <see cref="IAlternativeCost.CanCastFor"/> itself, but probes may
/// pre-filter for efficiency. Returning an empty enumeration is the
/// "card has no alt cost in this context" answer.
/// </summary>
public interface IAlternativeCostProbe
{
    IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx);
}
