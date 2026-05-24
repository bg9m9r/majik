using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Primitives;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gut Shot (New Phyrexia, {R/P}).
///
/// Instant. Oracle text:
///   "({R/P} can be paid with either {R} or 2 life.)
///    Gut Shot deals 1 damage to any target."
///
/// ## Implemented (v1)
/// - Instant card with printed mana cost {R} (the mana-only pip of the
///   phyrexian symbol). CR 107.4f — phyrexian mana symbols may be paid
///   with the listed colour OR 2 life (CR 118.8).
/// - Phyrexian 2-life alternative modelled via
///   <see cref="PhyrexianAlternativeCost"/> (same shape as
///   <see cref="SurgicalExtractionFactory"/> / Spellskite): main cost
///   = {R}; alt cost = {0} mana + 2 life. The <c>{R/P}</c> shape is
///   preserved as a structural marker via
///   <see cref="KeywordAbility"/>("Phyrexian") for any future
///   per-pip-choice dispatcher.
/// - <see cref="BuildDefinition"/> wires the resolve effect:
///   1..1 "any target" TargetRequest (Player / Creature / Planeswalker).
///   On resolve: deal 1 damage via <see cref="OracleSpellBinder.DealDamage"/>.
///
/// ## Deferred (v1 gaps)
/// - Bot-side probe / heuristics for choosing between mana and life
///   payment — caller passes the alternative cost explicitly.
/// - Per-pip selectivity (pay one pip as mana, one as life) — not
///   applicable here (single pip), but
///   <see cref="PhyrexianManaAlternativeCost"/> only models "pay every
///   pip as life".
/// </summary>
[CardName("Gut Shot")]
public static class GutShotFactory
{
    public const string CardName = "Gut Shot";

    /// <summary>
    /// Printed mana cost (the {R} pip of the phyrexian {R/P} symbol).
    /// The 2-life alternative is exposed via <see cref="PhyrexianAlternativeCost"/>.
    /// </summary>
    public const string PrintedManaCost = "{R}";

    /// <summary>CardDef DSL — card shape only. CR 107.4f Phyrexian marker
    /// is wired via <see cref="CardDefBuilder.WithKeyword"/>; the damage
    /// body lives in <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef
        .Instant(CardName, PrintedManaCost)
        .WithKeyword("Phyrexian");

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Returns a <see cref="PhyrexianManaAlternativeCost"/> for the {R/P}
    /// pip: AlternativeManaCost = Zero (no non-phyrexian portion after
    /// stripping the single phyrexian pip), LifeCost = 2.
    ///
    /// Callers that want the 2-life cast supply this as
    /// <c>alternativeCost</c> to SpellCastFlow.CastAsync (mirrors
    /// <see cref="SurgicalExtractionFactory.PhyrexianAlternativeCost"/>).
    /// </summary>
    public static PhyrexianManaAlternativeCost PhyrexianAlternativeCost()
        => PhyrexianManaAlternativeCost.ForPrintedCost(ManaCost.Parse("{R/P}"));

    /// <summary>
    /// Build the "deal 1 damage to any target" SpellDefinition.
    /// Target is 1..1 "any target" (Player, Creature, or Planeswalker).
    /// On resolve: 1 damage via <see cref="OracleSpellBinder.DealDamage"/>.
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    Fx.Inline(
                        "Gut Shot — deals 1 damage to any target",
                        () => Fx.DealDamage(raw, 1)),
                };
            });
}
