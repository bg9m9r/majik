using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cackling Slasher (Duskmourn: House of Horror, {3}{B}).
///
/// Creature — Human Assassin 3/3. Oracle text (Scryfall, verified):
///   "Deathtouch
///    This creature enters with a +1/+1 counter on it if a creature died this turn."
///
/// ## Shape source
///
/// Card identity (name, {3}{B}, 3/3, Creature — Human Assassin, black) is
/// loaded from <c>Majik.Core/CardData/Cards/cackling-slasher.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same data-driven identity pattern as
/// <see cref="NarnamRenegadeFactory"/>. The Deathtouch marker and the
/// conditional enters-with-counter replacement are wired in code below.
///
/// ## Implemented (v1)
/// - {3}{B} 3/3 Creature — Human Assassin, black, owner / controller stamped.
/// - <b>Deathtouch (CR 702.2)</b> — attached as a <see cref="KeywordAbility"/>
///   marker, same shape as <see cref="NarnamRenegadeFactory"/> /
///   <see cref="DeadlyRecluseFactory"/>. CombatAbilities consumes the marker
///   for lethal-damage determination.
/// - <b>Enters-with-counter (CR 614.1d)</b> — wired through the reusable
///   <see cref="EntersWithCountersReplacement"/> dynamic-count overload. The
///   gate is the GLOBAL "a creature died this turn" question (CR 700.4 — any
///   creature, any controller), read from
///   <see cref="TurnState.CreaturesDiedThisTurn"/>. When that tally is &gt; 0 at
///   the moment the creature would enter, it enters with one +1/+1 counter
///   (a 4/4); otherwise a vanilla 3/3.
///
/// This differs from <see cref="NarnamRenegadeFactory"/> (Revolt) only in the
/// gating predicate: Narnam Renegade asks "did a permanent leave YOUR control
/// this turn?" (controller-scoped revolt, CR 702.104a) whereas Cackling Slasher
/// asks the unscoped "did ANY creature die this turn?" (CR 700.4).
///
/// The predicate is null-safe: when no <see cref="TurnState"/> is wired (shape /
/// dispatcher tests, or the single-arg overload), the gate is treated as false
/// and the creature enters vanilla.
/// </summary>
[CardName("Cackling Slasher")]
public static class CacklingSlasherFactory
{
    public const string CardName = "Cackling Slasher";

    /// <summary>N — number of +1/+1 counters Cackling Slasher enters with when
    /// a creature died this turn.</summary>
    public const int DeathCounterAmount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("cackling-slasher");

    /// <summary>
    /// Construct Cackling Slasher with card identity + Deathtouch only — no
    /// enters-with-counter replacement registered (the gate is always false on
    /// this path). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, turnStateResolver: null);

    /// <summary>
    /// Construct Cackling Slasher with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied (together with a non-null
    /// <paramref name="turnStateResolver"/>), an
    /// <see cref="EntersWithCountersReplacement"/> is registered so the creature
    /// enters with one +1/+1 counter when a creature died this turn at ETB.</param>
    /// <param name="turnStateResolver">Callback returning the live
    /// <see cref="TurnState"/> evaluated as the creature would enter. When the
    /// callback is null or returns null, the gate is treated as false and the
    /// creature enters vanilla (CR 614.1d — condition not met at entry).</param>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements,
        Func<TurnState?>? turnStateResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.2 — Deathtouch marker. CombatAbilities.HasDeathtouch consumes
        // this for lethal-damage determination.
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        // CR 614.1d + CR 700.4 — conditional enters-with-counter. Only wired
        // when both a replacement bus and a turn-state resolver are supplied;
        // the dynamic count is evaluated at the moment the creature would enter
        // and yields one counter iff ANY creature died this turn.
        if (replacements != null && turnStateResolver != null)
        {
            replacements.Register<ZoneMoveIntent>(
                new EntersWithCountersReplacement(
                    card,
                    CounterType.PlusOnePlusOne,
                    () => CreatureDiedThisTurn(turnStateResolver) ? DeathCounterAmount : 0));
        }

        return card;
    }

    /// <summary>
    /// CR 700.4 — true when at least one creature died this turn (any creature,
    /// any controller). Null-safe: when no <see cref="TurnState"/> is wired the
    /// gate is false.
    /// </summary>
    private static bool CreatureDiedThisTurn(Func<TurnState?> turnStateResolver)
    {
        var turnState = turnStateResolver();
        return turnState != null && turnState.CreaturesDiedThisTurn > 0;
    }
}
