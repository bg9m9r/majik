using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blight Mamba (New Phyrexia, {1}{G}).
///
/// Creature — Phyrexian Snake 1/1. Oracle text (verified against Scryfall):
///   "Infect (This creature deals damage to creatures in the form of -1/-1
///    counters and to players in the form of poison counters.)
///    {1}{G}: Regenerate this creature."
///
/// ## Implemented
/// - <b>Creature — Phyrexian Snake {1}{G} 1/1</b>, owner / controller wired.
/// - <b>Infect (CR 702.90)</b> — <see cref="KeywordAbility"/> marker "Infect".
///   The combat-damage replacement (poison counters on players, -1/-1 counters
///   on creatures) is engine-side; this factory surfaces a structurally correct
///   marker so the downstream Infect primitive picks Blight Mamba up without
///   re-touching the factory (same posture as <see cref="GlistenerElfFactory"/>
///   / <see cref="SkithiryxTheBlightDragonFactory"/>).
/// - <b>"{1}{G}: Regenerate this creature." (CR 701.18 / 701.15a)</b> — an
///   <see cref="ActivatedAbility"/> whose sole cost is {1}{G}; on resolve a
///   regeneration shield is created on Blight Mamba via
///   <see cref="Permanent.AddRegenerationShield"/>, consumed by the next destroy
///   this turn (tap, remove from combat, heal damage — CR 701.18). Same shield
///   primitive River Boa / Skithiryx use. Regular speed; any number of times per
///   turn (shields stack, clear at end of turn per CR 514.2).
///
/// CR rule references: 205.3m (Phyrexian / Snake creature subtypes), 701.15a /
/// 701.18 (regeneration), 702.90 (Infect).
/// </summary>
[CardName("Blight Mamba")]
public static class BlightMambaFactory
{
    public const string CardName = "Blight Mamba";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const string RegenerateCost = "{1}{G}";

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[]
            {
                CardSubtype.Phyrexian,
                CardSubtype.Snake,
            });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.90 — Infect. Keyword marker; the combat-damage replacement
        // (poison to players, -1/-1 counters to creatures) is deferred at the
        // primitive level and consults this marker once it lands.
        card.AddAbility(new KeywordAbility("Infect", card, owner));

        // ----------------------------------------------------------------
        // {1}{G}: Regenerate this creature.
        // CR 701.18 — "Regenerate [self]" = create a regeneration shield on
        // Blight Mamba (CR 701.15a), consumed by the next destroy this turn (tap,
        // remove from combat, heal damage). Mirrors River Boa / Skithiryx.
        //
        // RE-SOURCE-SAFE (agatha-bespoke-factory-resolutioncontext-source-
        // migration): shields the live ResolutionContext.Source (the ability's
        // own Source at resolution) rather than capturing `card`, falling back to
        // `card` only on the context-less legacy sync path. Marked RebindSafe so
        // Agatha's Soul Cauldron re-homes the REAL regenerate ability to a
        // counter-bearing bearer via ActivatedAbility.RebindTo (CR 707.2 /
        // 613.1f), rather than reconstructing it from oracle text.
        // ----------------------------------------------------------------
        var regenerateEffect = new Effect(
            $"{CardName}: regenerate self (CR 701.18)",
            ctx =>
            {
                var subject = (ctx.Source as Permanent) ?? card;
                subject.AddRegenerationShield();
                return ValueTask.CompletedTask;
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(RegenerateCost) },
            effects: new IEffect[] { regenerateEffect },
            rebindSafe: true));

        return card;
    }
}
