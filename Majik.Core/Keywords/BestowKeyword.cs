using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.ValueObjects;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.103 — Bestow. A declarative helper that wires the bestow
/// keyword onto an Enchantment Creature.
///
/// ## What bestow is
///
/// A bestow card is printed as an Enchantment Creature with an alternative
/// "Bestow {cost}" casting option (CR 702.103a). Two cast modes:
///   - <b>Cast normally</b> — it resolves as an ordinary creature spell and
///     enters as a creature.
///   - <b>Cast for its bestow cost</b> (CR 702.103b) — it becomes an Aura
///     spell with "enchant creature", targets a creature, and enters the
///     battlefield attached to that creature. While attached this way it is
///     <i>not</i> a creature (CR 702.103e) — it is only an Aura — and grants
///     the enchanted creature its "Enchanted creature gets +X/+X" boost.
///
/// CR 702.103f — "If a permanent with bestow stops being attached to a
/// permanent, it becomes a creature." That is the detach state-transition:
/// the bestow card stops being an Aura and reverts to a creature in place.
///
/// ## How it is modelled (built on existing layer-system primitives)
///
/// Rather than mutate the printed type set in place (which the CR-613 layer
/// pipeline would not see), bestow is two continuous effects keyed off the
/// card's live <see cref="Permanent.AttachedTo"/> slot:
///
///   1. <see cref="Layer4TypeStripEffect"/> (Layer 4, CR 205.2 / 613.1d) —
///      while the bestow card is on the battlefield AND attached to a
///      creature, strip <see cref="CardType.Creature"/> from its own
///      characteristics. The predicate reads <see cref="Permanent.AttachedTo"/>
///      every Compute pass, so the moment it stops being attached
///      (<c>AttachedTo == null</c>) the strip lifts and it is a creature
///      again — exactly CR 702.103e / 702.103f, with no explicit
///      re-classing call.
///
///   2. <see cref="AttachedBoostEffect"/> (Layer 7c, CR 613) — the
///      "Enchanted creature gets +X/+X" boost on whichever creature the
///      bestow card is attached to, gated (by the effect's own
///      <c>IsActive</c>) on the card being on the battlefield AND attached.
///
/// Both effects are source-anchored and lift automatically when the bestow
/// card leaves the battlefield, so no teardown wiring is needed.
///
/// ## Cast-as-bestow spell shape
///
/// <see cref="BuildBestowSpellDefinition"/> produces the Aura-cast
/// <see cref="SpellDefinition"/> (single creature target, auto-attach on
/// resolution) via the shared <see cref="AuraSpellDefinitionBuilder"/> — the
/// same machinery Rancor / Ethereal Armor use. The caller pays the bestow
/// cost (an alternative cost, CR 702.103b) through the normal cast flow; the
/// Aura subtype need not be printed because while bestowed the card is "an
/// Aura spell with enchant creature" (CR 702.103b) — its Aura-ness is the
/// attach relationship, and the +X/+X is supplied by
/// <see cref="AttachedBoostEffect"/>, not by an Aura-subtype-gated rule.
/// </summary>
public static class BestowKeyword
{
    /// <summary>
    /// Register the two continuous effects that implement bestow's attached
    /// state for <paramref name="card"/>: the Layer-4 "not a creature while
    /// attached" strip (CR 702.103e / 702.103f) and the Layer-7c
    /// "enchanted creature gets +power/+toughness" boost (CR 613).
    ///
    /// Call once when building the card with a live
    /// <see cref="ContinuousEffectsService"/>.
    /// </summary>
    /// <param name="card">The bestow Enchantment Creature.</param>
    /// <param name="effects">The continuous-effects service to register
    /// against.</param>
    /// <param name="power">The +P component of "Enchanted creature gets
    /// +P/+T".</param>
    /// <param name="toughness">The +T component.</param>
    /// <param name="grantedKeywords">Optional keywords the bestowed boost
    /// grants the enchanted creature (CR 613 Layer 6). Most Theros bestow
    /// cards grant none.</param>
    public static void RegisterBestowEffects(
        Creature card,
        ContinuousEffectsService effects,
        int power,
        int toughness,
        IReadOnlyList<string>? grantedKeywords = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(effects);

        // CR 702.103e / 702.103f — while attached as an Aura, the bestow card
        // is NOT a creature; it reverts the instant it stops being attached.
        // Predicate reads AttachedTo live, so the transition is automatic.
        effects.Register(new Layer4TypeStripEffect(
            source: card,
            predicate: () => card.AttachedTo != null));

        // CR 613 — "Enchanted creature gets +P/+T" while bestowed. The effect
        // reads card.AttachedTo dynamically and self-gates on attach.
        effects.Register(new AttachedBoostEffect(
            source: card,
            power: power,
            toughness: toughness,
            grantedKeywords: grantedKeywords));
    }

    /// <summary>
    /// CR 702.103b — build the Aura-cast <see cref="SpellDefinition"/> for a
    /// bestow card cast for its bestow cost: "an Aura spell with enchant
    /// creature." Single creature target; on resolution the bestow card
    /// enters the battlefield attached to the chosen creature (CR 303.4f).
    /// </summary>
    /// <param name="card">The bestow card being cast for its bestow cost.</param>
    /// <param name="battlefield">Current battlefield permanents; filtered to
    /// creatures to produce the legal target list (CR 702.5b / 303.4c).</param>
    public static SpellDefinition BuildBestowSpellDefinition(
        Permanent card,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAura(
            card,
            targetDescription: "target creature",
            battlefield: battlefield,
            predicate: p => p.HasType(CardType.Creature));
    }

    /// <summary>
    /// CR 702.103b — the alternative bestow cost as a <see cref="ManaCost"/>.
    /// Parses the printed "{...}" cost string. Surfaced for cast-flow / bot
    /// consumers that price the bestow cast mode.
    /// </summary>
    public static ManaCost ParseBestowCost(string bestowCost)
    {
        ArgumentNullException.ThrowIfNull(bestowCost);
        return ManaCost.Parse(bestowCost);
    }
}
