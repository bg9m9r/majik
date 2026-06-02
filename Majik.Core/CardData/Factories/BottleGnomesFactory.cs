using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bottle Gnomes (Tempest, {3}).
///
/// Artifact Creature — Gnome 1/3. Oracle text (Scryfall, verified 2026-06-02):
///   "Sacrifice this creature: You gain 3 life."
///
/// A cheap colorless blocker that cashes itself in for a burst of life — pure
/// damage mitigation in any deck that can spare an artifact slot.
///
/// ## Shape source
/// Card identity (name, {3}, 1/3, Artifact Creature — Gnome) is loaded from
/// <c>Majik.Core/CardData/Cards/bottle-gnomes.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same JSON-driven posture as
/// <see cref="GoldhoundFactory"/>. The single sacrifice-for-life activated
/// ability is attached in code below.
///
/// ## Implemented (v1)
/// - 1/3 Artifact Creature — Gnome at {3}, owner/controller stamped. The
///   Creature ctor sets the primary Creature type; the JSON's <c>types</c>
///   array also carries Artifact (CR 205.2a — a permanent can have multiple
///   card types) so artifact-matters effects see it.
/// - <b>Sacrifice this creature: You gain 3 life</b> — a single
///   <see cref="ActivatedAbility"/> whose ONLY cost is
///   <see cref="AdditionalCost.Sacrifice"/> on the gnomes itself (CR 602.1 —
///   activated ability; CR 118.5 — sacrifice as a cost). There is NO mana
///   pip and NO {T} pip in the printed line, so a creature can activate this
///   the turn it enters the battlefield (summoning sickness gates only
///   <see cref="AdditionalCost.Tap"/> / {T} per CR 302.6, not sacrifice).
///   The resolution closure sacrifices the gnomes (battlefield -> owner's
///   graveyard, CR 701.16) and gains the controller 3 life via
///   <see cref="Player.GainLife"/> (CR 119.3 — lifegain is a discrete event;
///   routed through the player's replacement bus when present).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is a no-op stub, so the
///   resolution closure performs the zone move directly — same posture as
///   <see cref="BurnishedHartFactory"/> / Expedition Map / Mind Stone.
/// </summary>
[CardName("Bottle Gnomes")]
public static class BottleGnomesFactory
{
    public const string CardName = "Bottle Gnomes";
    public const int LifeGain = 3;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("bottle-gnomes");

    /// <summary>
    /// Construct Bottle Gnomes owned and controlled by <paramref name="owner"/>.
    /// The single "Sacrifice this creature: You gain 3 life" activated ability
    /// is attached structurally. Single-arg dispatcher path — suitable for
    /// shape, dispatcher, and unit-test usage.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Sacrifice this creature: You gain 3 life."
        // CR 602.1 — activated ability; CR 118.5 — sacrifice as a cost;
        // CR 119.3 — lifegain is a discrete event. No mana, no {T}.
        // ----------------------------------------------------------------
        var effect = new Effect(
            $"{CardName}: sacrifice self + gain {LifeGain} life",
            () =>
            {
                var controller = card.Controller ?? owner;
                SacrificeSelf(card, owner, controller);

                // CR 119.3 — lifegain fired unconditionally per the printed
                // "You gain 3 life". The gnomes is already gone (sacrifice
                // was paid as a cost), so the controller still resolves it.
                controller.GainLife(LifeGain);
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { effect });

        card.AddAbility(ability);

        return card;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent / defensive against the gnomes already
    /// having left the battlefield.
    /// </summary>
    private static void SacrificeSelf(Creature card, Player owner, Player controller)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        controller.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
