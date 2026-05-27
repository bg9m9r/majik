using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skithiryx, the Blight Dragon (Mirrodin Besieged,
/// {3}{B}{B}).
///
/// Legendary Creature — Skeleton Dragon 4/4. Oracle text:
///   "Flying.
///    Haste.
///    Infect (This creature deals damage to creatures in the form of
///    -1/-1 counters and to players in the form of poison counters.)
///    {B}: Regenerate Skithiryx, the Blight Dragon."
///
/// ## Implemented (v1)
///
/// - 4/4 Legendary <see cref="Creature"/> at {3}{B}{B} with subtypes
///   Skeleton, Dragon.
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker
///   "Flying". The combat block-restriction is read by the combat system
///   through the keyword catalog.
/// - <b>Haste (CR 702.10)</b>: <see cref="KeywordAbility"/> marker
///   "Haste". Summoning-sickness gate (CR 302.1) consults this marker
///   so the creature can attack / tap-activate the turn it enters
///   (same wiring as <see cref="BloodbraidElfFactory"/> /
///   <see cref="GoblinChieftainFactory"/>).
/// - <b>Infect (CR 702.90)</b>: <see cref="KeywordAbility"/> marker
///   "Infect". The combat-damage replacement is deferred at the
///   primitive level; the marker surfaces the keyword so a downstream
///   Infect primitive picks Skithiryx up without re-touching the
///   factory (same posture as <see cref="PhyrexianCrusaderFactory"/> /
///   <see cref="BlightedAgentFactory"/> / <see cref="PlagueMyrFactory"/>).
/// - <b>{B}: Regenerate self (CR 701.18 / 701.15a)</b>: wired as an
///   <see cref="ActivatedAbility"/> with a single
///   <see cref="ManaCostCost"/> <c>{B}</c>. Resolution calls
///   <see cref="Permanent.AddRegenerationShield"/> on Skithiryx — the
///   next time it would be destroyed this turn the shield consumes
///   the destroy, taps Skithiryx, and clears damage (CR 701.15c).
///   Shields stack across multiple activations and clear during
///   cleanup (CR 514.2). Mirrors <see cref="MortivoreFactory"/>'s
///   {B}-regenerate wiring exactly.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Infect damage-replacement</b>: poison counter tracking on
///   <see cref="Player"/> + the layered combat replacement land in a
///   follow-up infrastructure PR. Skithiryx's Infect marker becomes
///   live behaviour for free at that point.
/// </summary>
[CardName("Skithiryx, the Blight Dragon")]
public static class SkithiryxTheBlightDragonFactory
{
    public const string CardName = "Skithiryx, the Blight Dragon";
    public const string PrintedManaCost = "{3}{B}{B}";
    public const int Power = 4;
    public const int Toughness = 4;

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[]
            {
                CardSubtype.Skeleton,
                CardSubtype.Dragon,
            });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. Keyword marker.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.10 — Haste. Keyword marker; summoning-sickness gate
        // (CR 302.1) consults this for attacks + tap-activations.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // CR 702.90 — Infect. Keyword marker; combat-damage replacement
        // is deferred (see class xmldoc).
        card.AddAbility(new KeywordAbility("Infect", card, owner));

        // ----------------------------------------------------------------
        // {B}: Regenerate Skithiryx, the Blight Dragon.
        // CR 701.18 — "Regenerate [self]" = create a regeneration shield
        // on the target (CR 701.15a). Activated ability, regular speed,
        // any number of times per turn (shields stack and clear at EOT).
        // Mirrors MortivoreFactory's {B}-regenerate wiring.
        // ----------------------------------------------------------------
        var regenerateEffect = new Effect(
            $"{CardName}: regenerate self",
            () => card.AddRegenerationShield());

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{B}") },
            effects: new IEffect[] { regenerateEffect }));

        return card;
    }
}
