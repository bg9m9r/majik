using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Narnam Renegade (Aether Revolt, {G}).
///
/// Creature — Elf Warrior 1/2. Oracle text (Scryfall, verified):
///   "Deathtouch
///    Revolt — This creature enters with a +1/+1 counter on it if a permanent
///    left the battlefield under your control this turn."
///
/// ## Shape source
///
/// Card identity (name, {G}, 1/2, Creature — Elf Warrior, green) is loaded
/// from <c>Majik.Core/CardData/Cards/narnam-renegade.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same data-driven identity pattern as
/// <see cref="WindingConstrictorFactory"/>. The Deathtouch marker and the
/// Revolt enters-with-counter replacement are wired in code below.
///
/// ## Implemented (v1)
/// - {G} 1/2 Creature — Elf Warrior, green, owner / controller stamped.
/// - <b>Deathtouch (CR 702.2)</b> — attached as a <see cref="KeywordAbility"/>
///   marker, same shape as <see cref="DeadlyRecluseFactory"/>. CombatAbilities
///   consumes the marker for lethal-damage determination.
/// - <b>Revolt enters-with-counter (CR 702.104a + CR 614.1d)</b> — wired
///   through the reusable <see cref="RevoltEntersWithCountersReplacement"/>.
///   When a permanent the controller controlled left the battlefield this turn
///   (<see cref="TurnState.RevoltActive"/>), the creature enters with one
///   +1/+1 counter (a 2/3); otherwise a vanilla 1/2. This generalizes the
///   Bloodthirst conditional-ETB-counter shape (Gorehorn Minotaurs / Bloodrage
///   Vampire) by swapping the opponent-damaged gate for the revolt gate.
///
/// The Revolt predicate is null-safe: when no <see cref="TurnState"/> is wired
/// (shape / dispatcher tests, or the single-arg overload), revolt is treated
/// as inactive and the creature enters vanilla.
/// </summary>
[CardName("Narnam Renegade")]
public static class NarnamRenegadeFactory
{
    public const string CardName = "Narnam Renegade";

    /// <summary>N — Narnam Renegade enters with this many +1/+1 counters when
    /// Revolt is active.</summary>
    public const int RevoltCounterAmount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("narnam-renegade");

    /// <summary>
    /// Construct Narnam Renegade with card identity + Deathtouch only — no
    /// enters-with-counter replacement registered (revolt always inactive on
    /// this path). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, turnStateResolver: null);

    /// <summary>
    /// Construct Narnam Renegade with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied (together with a non-null
    /// <paramref name="turnStateResolver"/>), a
    /// <see cref="RevoltEntersWithCountersReplacement"/> is registered so the
    /// creature enters with one +1/+1 counter when Revolt is active at ETB.</param>
    /// <param name="turnStateResolver">Callback returning the live
    /// <see cref="TurnState"/> evaluated as the creature would enter. When the
    /// callback is null or returns null, revolt is treated as inactive and the
    /// creature enters vanilla (CR 702.104a — no permanent left your control).</param>
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

        // CR 702.104a + CR 614.1d — Revolt enters-with-counter. Only wired when
        // both a replacement bus and a turn-state resolver are supplied; the
        // predicate reads the controller's per-turn permanent-left tally at the
        // moment the creature would enter.
        if (replacements != null && turnStateResolver != null)
        {
            replacements.Register<ZoneMoveIntent>(
                new RevoltEntersWithCountersReplacement(
                    card,
                    RevoltCounterAmount,
                    () => IsRevoltActive(card.Controller ?? owner, turnStateResolver)));
        }

        return card;
    }

    /// <summary>
    /// CR 702.104a — revolt is active for <paramref name="controller"/> when at
    /// least one permanent they controlled left the battlefield this turn.
    /// Null-safe: when no <see cref="TurnState"/> is wired, revolt is inactive.
    /// </summary>
    private static bool IsRevoltActive(
        Player controller,
        Func<TurnState?> turnStateResolver)
    {
        var turnState = turnStateResolver();
        return turnState != null && turnState.RevoltActive(controller);
    }
}
