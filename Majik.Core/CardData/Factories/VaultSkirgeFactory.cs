using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vault Skirge (New Phyrexia, {2}{B/P}).
///
/// Artifact Creature — Imp 1/1. Oracle text:
///   "({B/P} can be paid with either {B} or 2 life.)
///    Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)
///    Flying
///    Lifelink"
///
/// ## Implementation (v1)
///
/// - 1/1 Artifact Creature — Imp at printed cost {2}{B/P}. The Artifact
///   type is layered on via <see cref="Card.AddCardType"/> so HasType
///   lookups + colour identity see both types (same shape as
///   <see cref="FrogmiteFactory"/> / <see cref="ArcboundRavagerFactory"/>).
/// - <b>Affinity for artifacts (CR 702.40 / CR 117.7)</b>: wired via
///   <see cref="CostReductionAbility.AffinityFor"/>(<see cref="CardType.Artifact"/>).
///   The cost-reducer scans the caster's battlefield at cast time
///   (<see cref="CostReduction.GetEffectiveCost"/>) and lowers Vault
///   Skirge's generic-mana requirement by 1 per controller-controlled
///   artifact; floor-at-zero (CR 117.7c). A
///   <see cref="KeywordAbility"/>("Affinity") marker is attached so
///   keyword-scan callers see the keyword without inspecting the
///   <see cref="CostReductionAbility"/> list — same shape as Frogmite.
/// - <b>Flying (CR 702.9)</b> + <b>Lifelink (CR 702.15)</b>:
///   <see cref="KeywordAbility"/> markers — combat helpers in
///   <see cref="Majik.Core.Combat.CombatAbilities"/> read them directly.
/// - <b>Phyrexian alt-cost (CR 107.4f / CR 118.8)</b>: exposed via
///   <see cref="PhyrexianAlternativeCost"/> — strips the single {B/P}
///   pip from the printed cost, leaving {2} mana to pay and charging
///   2 life. Callers (SpellCastFlow / tests) supply this as
///   <c>alternativeCost</c> on cast. Same shape as
///   <see cref="SurgicalExtractionFactory.PhyrexianAlternativeCost"/> /
///   <see cref="DismemberFactory.PhyrexianAlternativeCost"/>.
/// - A <see cref="KeywordAbility"/>("Phyrexian") marker is attached
///   for keyword-scan parity with Dismember / Surgical Extraction.
///
/// ## Deferred (v1 gaps)
/// - Per-pip selectivity (pay the single phyrexian pip as mana while
///   still using the phyrexian alt-cost path) — n/a here, Vault Skirge
///   has exactly one phyrexian pip so the two paths
///   ({2}{B} all mana vs. {2} + 2 life) are the only legal payments.
/// - Bot-side probe / heuristics for picking between the {B} and the
///   2-life payment — the engine relies on the caller to pass the
///   alternative cost explicitly.
/// </summary>
[CardName("Vault Skirge")]
public static class VaultSkirgeFactory
{
    public const string CardName = "Vault Skirge";

    /// <summary>
    /// Printed mana cost. The {B/P} symbol is parsed into a phyrexian pip
    /// on the ManaCost value object; for runtime payment in the v1 engine
    /// we treat the cost as {2}{B} (mana-pay) and the 2-life option via
    /// <see cref="PhyrexianAlternativeCost"/>. Mirrors the Surgical
    /// Extraction / Spellskite convention.
    /// </summary>
    public const string PrintedManaCost = "{2}{B}";
    public const int Power = 1;
    public const int Toughness = 1;

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Imp });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType lookups + colour identity see both
        // types (mirrors Frogmite / Arcbound Ravager).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.40 / CR 117.7 — Affinity for artifacts cost-reducer +
        // keyword marker. Frogmite-shape wiring.
        card.AddAbility(CostReductionAbility.AffinityFor(CardType.Artifact));
        card.AddAbility(new KeywordAbility("Affinity", card, owner));

        // CR 702.9 / CR 702.15 — Flying + Lifelink markers. Combat-side
        // reads via CombatAbilities; the marker keeps the keyword scan
        // surface uniform.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // CR 107.4f / CR 118.8 — Phyrexian marker for keyword-scan
        // parity with Dismember / Surgical Extraction. The runtime
        // alt-cost itself is built by callers via
        // PhyrexianAlternativeCost().
        card.AddAbility(new KeywordAbility("Phyrexian", card, owner));

        return card;
    }

    /// <summary>
    /// Build the phyrexian alternative cost (pay {2} mana + 2 life
    /// instead of {2}{B}) for a just-created Vault Skirge instance.
    /// Caller passes this to <c>SpellCastFlow.CastAsync(...,
    /// alternativeCost: ...)</c>. The remaining mana cost is {2}; the
    /// life cost is 2 (one phyrexian pip × 2 life).
    /// </summary>
    public static PhyrexianManaAlternativeCost PhyrexianAlternativeCost()
        => PhyrexianManaAlternativeCost.ForPrintedCost(
            Majik.Core.ValueObjects.ManaCost.Parse("{2}{B/P}"));
}
