using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Steel Overseer (Magic 2011, {2}).
///
/// Artifact Creature — Construct 1/1. Oracle text:
///   "{T}: Put a +1/+1 counter on each artifact creature you control."
///
/// ## Implementation
///
/// - 1/1 Artifact Creature — Construct with printed mana cost {2}. The
///   Artifact type is layered on via <see cref="Card.AddCardType"/>;
///   Steel Overseer counts as one of its own targets when it taps (the
///   activated ability puts a counter on "each artifact creature you
///   control" — Steel Overseer is one of those, so it pumps itself if
///   it weren't tapped — except CR 605 ordering means the counter still
///   lands while it's tapped: tapping is a cost, the effect resolves
///   afterwards and includes Steel Overseer in the scan).
/// - <b>Activated {T} — "Put a +1/+1 counter on each artifact creature
///   you control." (CR 605.1)</b>: wired as an
///   <see cref="ActivatedAbility"/> with a single
///   <see cref="AdditionalCost.Tap"/> cost. On resolution the effect
///   enumerates every artifact creature on the controller's battlefield
///   and places one +1/+1 counter on each, routed through
///   <see cref="CountersService.Add"/> so Hardened Scales / Doubling
///   Season replacements observe the placement (CR 614 / CR 121.2).
///   When no <see cref="ReplacementBus"/> is supplied the counter is
///   placed directly via <see cref="Permanent.Counters"/>.
///
/// ## Lifecycle
///
/// The two-arg <see cref="Create(Player, ReplacementBus?)"/> overload
/// wires the replacement bus so the activated ability honours
/// Hardened-Scales-shaped bumps. The single-arg overload returns a
/// shape-only card (no bus) — counter placements fall through to a
/// direct add.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Order-of-operations</b>: when multiple replacements interact
///   (Hardened Scales + Doubling Season + Vorinclex), the replacement
///   bus already orders them per CR 616 — Steel Overseer just calls
///   <see cref="CountersService.Add"/> once per artifact creature and
///   the bus does the right thing.
/// - <b>Token-token-shaped Constructs</b>: Steel Overseer's effect
///   doesn't carve out token artifact creatures; the v1
///   battlefield-scan picks every artifact creature, tokens included.
///   This matches the printed wording.
/// </summary>
[CardName("Steel Overseer")]
public static class SteelOverseerFactory
{
    public const string CardName = "Steel Overseer";
    public const string PrintedManaCost = "{2}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Steel Overseer with no live replacement bus. Counter
    /// placements from the activated ability fall through to a direct
    /// add (Hardened Scales / Doubling Season etc. won't bump). Suitable
    /// for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Steel Overseer with an optional
    /// <see cref="ReplacementBus"/>. When supplied, the activated
    /// ability routes its +1/+1 counter placements through
    /// <see cref="CountersService.Add"/> so replacement effects can
    /// rewrite or cancel the placement before it commits.
    /// </summary>
    public static Creature Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Construct });

        // CR 301.1 / 302.1 — Artifact Creature.
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Put a +1/+1 counter on each artifact creature you control.
        // (CR 605.1). At resolve time the controller's battlefield is
        // scanned for artifact creatures; each receives one +1/+1
        // counter via CountersService.Add so Hardened Scales / Doubling
        // Season can intercept.
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            $"{CardName}: +1/+1 counter on each artifact creature you control",
            () =>
            {
                foreach (var target in FindArtifactCreaturesControlled(card.Controller ?? owner))
                {
                    CountersService.Add(target, CounterType.PlusOnePlusOne, 1, replacements);
                }
            });

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }

    /// <summary>
    /// Enumerate the controller's battlefield artifact creatures. Used
    /// by the activated ability at resolve time so the set reflects the
    /// current battlefield (not the set at activation time — CR 608.2
    /// resolves with current game state).
    /// </summary>
    private static IEnumerable<Creature> FindArtifactCreaturesControlled(Player controller) =>
        controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.HasType(CardType.Artifact));
}
