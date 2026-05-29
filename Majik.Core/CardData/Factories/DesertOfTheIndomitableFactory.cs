using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for <b>Desert of the Indomitable</b> (Hour of Devastation)
/// — the green sibling of the Desert of the Fervent monocolour tapped
/// cycling-land cycle. Oracle text verified against Scryfall (type line
/// "Land — Desert"):
/// <code>
///   This land enters tapped.
///   {T}: Add {G}.
///   Cycling {1}{G} ({1}{G}, Discard this card: Draw a card.)
/// </code>
///
/// Shares the shape of the Onslaught cycling-land cycle
/// (<see cref="OnslaughtCyclingLandFactory"/>) — tapped ETB + a single
/// {T}: Add {color} mana ability + cycling — except:
/// <list type="bullet">
///   <item>the printed land subtype is <b>Desert</b> (CR 205.3i), not a
///   basic-land subtype; and</item>
///   <item>the cycling cost is <b>{1}{G}</b> (two mana) rather than the
///   Onslaught cycle's single coloured pip.</item>
/// </list>
///
/// ## Implemented
/// - <b>Land — Desert</b> (CR 205.3i). The Desert subtype grants no
///   intrinsic mana; the printed {T}: Add {G} mana ability is declared
///   inline.
/// - <b>Enters-tapped replacement (CR 614.1c)</b> — unconditional
///   "This land enters tapped." Registered via
///   <see cref="EntersTappedReplacement"/> when a
///   <see cref="ReplacementBus"/> is supplied. Shape-only path (no bus)
///   skips the registration — same posture as
///   <see cref="OnslaughtCyclingLandFactory"/>.
/// - <b>{T}: Add {G}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1 —
///   mana abilities don't use the stack).
/// - <b>Cycling {1}{G}</b> (CR 702.32) — wired through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>(<c>"1G"</c>). When the bus is supplied the
///   cycling resolve publishes <see cref="CardCycledEvent"/> (CR 702.32d)
///   so cycling-triggers fire.
/// </summary>
[CardName("Desert of the Fervent's sibling Desert of the Indomitable")]
public static class DesertOfTheIndomitableFactory
{
    /// <summary>Cycling cost: {1}{G} (CR 702.32 — the printed cost).</summary>
    private const string CyclingCost = "1G";

    /// <summary>Produced mana: {G} (CR 605.1).</summary>
    private const string ProducedColor = "G";

    /// <summary>
    /// Construct Desert of the Indomitable owned and controlled by
    /// <paramref name="owner"/>. Single-arg path — no bus wiring (shape
    /// observability only; enters-tapped is omitted and cycling does not
    /// publish <see cref="CardCycledEvent"/>). Same posture as the
    /// Onslaught cycle's shape-only overload.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null, replacements: null);

    /// <summary>
    /// Construct Desert of the Indomitable with full bus wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Optional event bus the cycling resolve
    /// publishes <see cref="CardCycledEvent"/> against (CR 702.32d).</param>
    /// <param name="replacements">Optional replacement bus the
    /// enters-tapped restriction (CR 614.1c) is registered against.</param>
    public static Land Create(Player owner, IEventBus? eventBus, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            "Desert of the Fervent's sibling Desert of the Indomitable",
            supertypes: null,
            subtypes: new[] { CardSubtype.Desert });
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // "This land enters tapped." — CR 614.1c. Unconditional.
        // Shape-only path (no ReplacementBus) skips registration; the
        // land then enters untapped. Same posture as the Onslaught cycle.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {G}. CR 605.1 — mana ability (no stack).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse(ProducedColor)));

        // ----------------------------------------------------------------
        // Cycling {1}{G}. CR 702.32 — "{1}{G}, Discard this card: Draw a
        // card." Cycle cost is ManaCostCost("1G"); the primitive appends
        // the DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d).
        // ----------------------------------------------------------------
        CyclingFactory.Build(land, new ManaCostCost(CyclingCost), eventBus);

        return land;
    }
}
