using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stormcatch Mentor (Secrets of Strixhaven Commander,
/// {U}{R}).
///
/// Creature — Otter Wizard 1/1. Oracle text:
///   "Haste
///    Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    Instant and sorcery spells you cast cost {1} less to cast."
///
/// ## Implemented (v1)
/// - 1/1 Otter Wizard, mana cost {U}{R}, owner/controller wired.
/// - <b>Haste (CR 702.10)</b> — keyword marker via <see cref="KeywordAbility"/>,
///   same wiring shape as Slickshot Show-Off's Haste.
/// - <b>Prowess (CR 702.108)</b> — "Whenever you cast a noncreature spell,
///   this creature gets +1/+1 until end of turn." Wired via
///   <see cref="ProwessFactory.Build"/> when a
///   <see cref="ContinuousEffectsService"/> is supplied; mirrors
///   <see cref="SoulScarMageFactory"/> / <see cref="MonasteryMentorFactory"/>.
///   The keyword marker is surfaced as the trigger itself — no separate
///   marker is added. Layer 7c pump flows through card.ActiveEffects.
/// - <b>Spell-cost reduction rider (CR 117.7 / CR 601.2f)</b> — "Instant and
///   sorcery spells you cast cost {1} less to cast." Wired via
///   <see cref="SpellCostReductionAbility"/> with the same predicate +
///   flat-1 reduction as <see cref="GoblinElectromancerFactory"/>. Scoped to
///   the controller's battlefield by <see cref="CostReduction.GetEffectiveCost"/>
///   ("spells YOU cast"); coloured pips are untouched (CR 117.7c) and the
///   generic bucket floors at zero. Multiple copies stack additively.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Haste + the
///   cost-reduction rider (both static, no live services needed) are wired;
///   Prowess is NOT wired (no effects service). Suitable for dispatcher /
///   structural tests. Mirrors <see cref="SoulScarMageFactory.Create(Player)"/>.
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?)"/>
///   — fully wired. Prowess trigger registered when <paramref name="effects"/>
///   is supplied.
/// </summary>
[CardName("Stormcatch Mentor")]
public static class StormcatchMentorFactory
{
    public const string CardName = "Stormcatch Mentor";
    public const string PrintedManaCost = "{U}{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Stormcatch Mentor with no live trigger wiring. Haste and the
    /// instant/sorcery cost-reduction rider are static and always attached;
    /// Prowess is not wired (no effects service supplied). Suitable for
    /// dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, triggers: null);

    /// <summary>
    /// Construct Stormcatch Mentor with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">ContinuousEffectsService for the Prowess pump
    /// effect (CR 613.1f, Layer 7c). May be null — Prowess trigger is not
    /// wired when null.</param>
    /// <param name="triggers">TriggerManager for the Prowess trigger. May be
    /// null — the trigger is still attached to the card shape.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Otter, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste. Keyword marker consumed by CombatValidator /
        // CombatAbilities (same wire-up shape as Slickshot Show-Off).
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // Prowess (CR 702.108) — "Whenever you cast a noncreature spell,
        // this creature gets +1/+1 until end of turn." Wired via
        // ProwessFactory.Build when a ContinuousEffectsService is supplied;
        // same shape as Soul-Scar Mage / Monastery Mentor. Layer 7c pump
        // flows through card.ActiveEffects so Power / Toughness reads
        // recompute via the layers pipeline (CR 613 Layer 7c).
        // ----------------------------------------------------------------
        if (effects != null)
        {
            card.ActiveEffects = effects;
            var prowessTrigger = ProwessFactory.Build(card, effects);
            card.AddAbility(prowessTrigger);
            triggers?.RegisterTriggeredAbility(prowessTrigger);
        }

        // CR 117.7 — "Instant and sorcery spells you cast cost {1} less to
        // cast." Predicate gates on the spell's card type; reduction is a
        // flat 1 generic. CostReduction.GetEffectiveCost scans only the
        // caster's battlefield for this ability shape, so the "you cast"
        // scope is enforced by the cost-calc helper. Coloured pips untouched
        // (CR 117.7c); generic floors at zero.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery),
            reduction: (_, _) => 1,
            description: "Instant and sorcery spells you cast cost {1} less to cast."));

        return card;
    }
}
