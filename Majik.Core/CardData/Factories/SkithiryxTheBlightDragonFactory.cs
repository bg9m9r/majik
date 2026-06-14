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
/// Legendary Creature — Phyrexian Dragon Skeleton 4/4. Current oracle text
/// (verified against Scryfall — supersedes the original printing's static
/// Haste + {B}-regenerate):
///   "Flying.
///    Infect (This creature deals damage to creatures in the form of
///    -1/-1 counters and to players in the form of poison counters.)
///    {B}: Skithiryx, the Blight Dragon gains haste until end of turn.
///    {B}{B}: Regenerate Skithiryx, the Blight Dragon."
///
/// ## Implemented (v1)
///
/// - 4/4 Legendary <see cref="Creature"/> at {3}{B}{B} with subtypes
///   Phyrexian, Skeleton, Dragon.
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker
///   "Flying". The combat block-restriction is read by the combat system
///   through the keyword catalog.
/// - <b>Infect (CR 702.90)</b>: <see cref="KeywordAbility"/> marker
///   "Infect". The combat-damage replacement is deferred at the
///   primitive level; the marker surfaces the keyword so a downstream
///   Infect primitive picks Skithiryx up without re-touching the
///   factory (same posture as <see cref="PhyrexianCrusaderFactory"/> /
///   <see cref="BlightedAgentFactory"/> / <see cref="PlagueMyrFactory"/>).
/// - <b>{B}: gains Haste until end of turn (CR 702.10 / 613.1f)</b>: wired
///   as an <see cref="ActivatedAbility"/> with a single
///   <see cref="ManaCostCost"/> <c>{B}</c>. Resolution registers a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> "Haste" on Skithiryx's
///   own <see cref="Creature.ActiveEffects"/> (Layer 6 keyword grant,
///   expires at cleanup per CR 514.2). The summoning-sickness gate
///   (CR 302.1) reads the post-Layer-6 effective keyword set, so the
///   granted Haste lets it attack / tap-activate the turn it enters once
///   the ability resolves. Mirrors the
///   <see cref="OracleActivatedAbilityBinder"/> self-keyword-grant shape
///   (the printed line names the creature, which the binder's
///   "this creature" form can't reconstruct from text).
/// - <b>{B}{B}: Regenerate self (CR 701.18 / 701.15a)</b>: wired as an
///   <see cref="ActivatedAbility"/> with a single
///   <see cref="ManaCostCost"/> <c>{B}{B}</c>. Resolution calls
///   <see cref="Permanent.AddRegenerationShield"/> on Skithiryx — the
///   next time it would be destroyed this turn the shield consumes
///   the destroy, taps Skithiryx, and clears damage (CR 701.15c).
///   Shields stack across multiple activations and clear during
///   cleanup (CR 514.2).
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
                CardSubtype.Phyrexian,
                CardSubtype.Skeleton,
                CardSubtype.Dragon,
            });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. Keyword marker.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.90 — Infect. Keyword marker; combat-damage replacement
        // is deferred (see class xmldoc).
        card.AddAbility(new KeywordAbility("Infect", card, owner));

        // ----------------------------------------------------------------
        // {B}: Skithiryx, the Blight Dragon gains haste until end of turn.
        // CR 702.10 — Haste; CR 613.1f Layer 6 keyword grant; CR 514.2 EOT
        // expiry. Activated ability, regular speed, any number of times per
        // turn. Resolution registers a GrantKeywordUntilEndOfTurnEffect on the
        // creature's own ActiveEffects so the summoning-sickness gate (CR 302.1,
        // which reads the post-Layer-6 effective keyword set) sees Haste once
        // it resolves. The current printing has NO static Haste — it is bought
        // each turn with {B}.
        //
        // RE-SOURCE-SAFE (agatha-bespoke-factory-resolutioncontext-source-
        // migration): the grant targets the live ResolutionContext.Source (the
        // ability's own Source at resolution) rather than capturing `card`,
        // falling back to `card` only on the context-less legacy sync path
        // (ResolutionContext.Legacy, where Source is null). Marked RebindSafe so
        // Agatha's Soul Cauldron's group-grant re-homes this ability to each
        // counter-bearing creature via ActivatedAbility.RebindTo (CR 707.2 /
        // 613.1f) — necessary here because the printed line names the creature
        // ("Skithiryx ... gains haste"), which the OracleActivatedAbilityBinder
        // self-grant's "this creature" form cannot reconstruct from text. The
        // null-ActiveEffects shape-only path silently no-ops, the same posture
        // as the binder's self-keyword grant.
        // ----------------------------------------------------------------
        var gainHasteEffect = new Effect(
            $"{CardName}: gains haste until end of turn (CR 702.10 / 613.1f)",
            ctx =>
            {
                var subject = (ctx.Source as Creature) ?? card;
                subject.ActiveEffects?.Register(
                    new GrantKeywordUntilEndOfTurnEffect(subject, "Haste"));
                return ValueTask.CompletedTask;
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{B}") },
            effects: new IEffect[] { gainHasteEffect },
            rebindSafe: true));

        // ----------------------------------------------------------------
        // {B}{B}: Regenerate Skithiryx, the Blight Dragon.
        // CR 701.18 — "Regenerate [self]" = create a regeneration shield
        // on the target (CR 701.15a). Activated ability, regular speed,
        // any number of times per turn (shields stack and clear at EOT).
        // The current printing costs {B}{B} (the original printing's {B}
        // was errata'd to {B}{B}).
        //
        // RE-SOURCE-SAFE (agatha-bespoke-factory-resolutioncontext-source-
        // migration): the effect shields the live ResolutionContext.Source
        // (the ability's own Source at resolution) rather than capturing
        // `card`, falling back to `card` only on the context-less legacy sync
        // path (ResolutionContext.Legacy, where Source is null). Marked
        // RebindSafe so Agatha's Soul Cauldron's group-grant re-homes the REAL
        // regenerate ability to each counter-bearing creature via
        // ActivatedAbility.RebindTo (CR 707.2 / 613.1f) — necessary here because
        // Skithiryx's printed line names the creature ("Regenerate Skithiryx"),
        // which the OracleActivatedAbilityBinder fallback's
        // "Regenerate this creature" form cannot reconstruct from text.
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
            costs: new ICost[] { new ManaCostCost("{B}{B}") },
            effects: new IEffect[] { regenerateEffect },
            rebindSafe: true));

        return card;
    }
}
