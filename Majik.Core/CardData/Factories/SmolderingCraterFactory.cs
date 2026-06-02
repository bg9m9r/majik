using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Smoldering Crater (Mirage and reprints — the
/// red member of the Mirage "{2}-cycling" tapland cycle, distinct from the
/// Onslaught monocolour cycling cycle handled by
/// <see cref="OnslaughtCyclingLandFactory"/>).
///
/// Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {R}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// The structural difference from the Onslaught cycle is two-fold, which is
/// why this is its own factory rather than another row on
/// <see cref="OnslaughtCyclingLandFactory"/>:
/// - <b>Cycling cost is generic {2}</b> (CR 702.32), not a coloured pip that
///   matches the produced colour. The Onslaught factory hard-couples cycle
///   cost to the produced colour ({R} for Forgotten Cave); that invariant
///   does not hold here.
/// - <b>No printed land subtype</b> — Smoldering Crater is a plain nonbasic
///   "Land", like <see cref="BojukaBogFactory"/>.
///
/// ## Implemented (v1)
/// - <b>Land</b> with no printed subtype.
/// - <b>Enters-tapped replacement (CR 614.1c)</b> — unconditional
///   "This land enters tapped." Registered via
///   <see cref="EntersTappedReplacement"/> on a supplied
///   <see cref="ReplacementBus"/>. Shape-only path (no
///   <see cref="ReplacementBus"/>) skips registration and the land enters
///   untapped — same posture as every other always-tapped factory.
/// - <b>{T}: Add {R}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1 —
///   mana abilities don't use the stack).
/// - <b>Cycling {2}</b> (CR 702.32) — wired through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>(<c>{2}</c>). When the bus is supplied,
///   cycling resolve publishes <see cref="CardCycledEvent"/>
///   (CR 702.32d) so Lightning Rift / Astral Slide triggers fire.
/// </summary>
[CardName("Smoldering Crater")]
public static class SmolderingCraterFactory
{
    public const string CardName = "Smoldering Crater";

    /// <summary>
    /// Construct Smoldering Crater. Single-arg path — no bus wiring (shape
    /// observability only; enters-tapped is omitted and cycling does not
    /// publish <see cref="CardCycledEvent"/>).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null, replacements: null);

    /// <summary>
    /// Construct Smoldering Crater with full bus wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Optional event bus the cycling resolve
    /// publishes <see cref="CardCycledEvent"/> against (CR 702.32d).</param>
    /// <param name="replacements">Optional replacement bus the
    /// enters-tapped restriction (CR 614.1c) is registered against.</param>
    public static Land Create(
        Player owner,
        IEventBus? eventBus,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Smoldering Crater is just "Land" — no printed subtype.
        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Enters-tapped — CR 614.1c. Unconditional.
        // Shape-only path (no ReplacementBus) skips registration; the
        // land then enters untapped. Same posture as BojukaBogFactory.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {R}. CR 605.1 — mana ability (no stack).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));

        // ----------------------------------------------------------------
        // Cycling {2}. CR 702.32 — "{2}, Discard this card: Draw a card."
        // Cycle cost is generic ManaCostCost("2"); the primitive appends
        // the DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d).
        // ----------------------------------------------------------------
        CyclingFactory.Build(land, new ManaCostCost("2"), eventBus);

        return land;
    }
}
