using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mutagenic Growth (New Phyrexia, {G/P}).
///
/// Instant. Oracle text:
///   "({G/P} can be paid with either {G} or 2 life.)
///    Target creature gets +2/+2 until end of turn."
///
/// ## Implemented (v1)
/// - Instant card with printed mana cost {G} (the mana-only pip of the
///   phyrexian symbol). CR 107.4f — phyrexian mana symbols may be paid
///   with the listed colour OR 2 life (CR 118.8).
/// - Phyrexian 2-life alternative modelled via
///   <see cref="PhyrexianAlternativeCost"/> (same shape as
///   <see cref="GutShotFactory"/> / <see cref="SurgicalExtractionFactory"/>):
///   main cost = {G}; alt cost = {0} mana + 2 life. The <c>{G/P}</c>
///   shape is preserved as a structural marker via
///   <see cref="KeywordAbility"/>("Phyrexian") for any future
///   per-pip-choice dispatcher.
/// - <see cref="BuildDefinition"/> wires the resolve effect:
///   1..1 "target creature" TargetRequest. On resolve: register a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+2, +2) on the target
///   creature's <see cref="Creature.ActiveEffects"/> (CR 514.2 —
///   "until end of turn"). CR 608.2b: target not on battlefield → no-op.
///
/// ## Deferred (v1 gaps)
/// - Bot-side probe / heuristics for choosing between mana and life
///   payment — caller passes the alternative cost explicitly.
/// - Per-pip selectivity (not applicable here — single pip).
/// </summary>
[CardName("Mutagenic Growth")]
public static class MutagenicGrowthFactory
{
    public const string CardName = "Mutagenic Growth";

    /// <summary>
    /// Printed mana cost (the {G} pip of the phyrexian {G/P} symbol).
    /// The 2-life alternative is exposed via <see cref="PhyrexianAlternativeCost"/>.
    /// </summary>
    public const string PrintedManaCost = "{G}";

    /// <summary>Construct Mutagenic Growth as an Instant with owner/controller wired.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // Structural Phyrexian mana marker (CR 107.4f) — the {G/P} pip shape.
        // SpellCastFlow's alt-cost selection reads PhyrexianAlternativeCost;
        // the KeywordAbility is a visibility/search marker only.
        card.AddAbility(new KeywordAbility("Phyrexian", card, owner));

        return card;
    }

    /// <summary>
    /// Returns a <see cref="PhyrexianManaAlternativeCost"/> for the {G/P}
    /// pip: AlternativeManaCost = Zero (no non-phyrexian portion after
    /// stripping the single phyrexian pip), LifeCost = 2.
    ///
    /// Callers that want the 2-life cast supply this as
    /// <c>alternativeCost</c> to SpellCastFlow.CastAsync (mirrors
    /// <see cref="GutShotFactory.PhyrexianAlternativeCost"/>).
    /// </summary>
    public static PhyrexianManaAlternativeCost PhyrexianAlternativeCost()
        => PhyrexianManaAlternativeCost.ForPrintedCost(ManaCost.Parse("{G/P}"));

    /// <summary>
    /// Build the "target creature gets +2/+2 until end of turn" SpellDefinition.
    ///
    /// On resolve: validates target is still a Creature on the Battlefield
    /// (CR 608.2b — illegal target → no-op). When valid, registers a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(+2, +2) on the target's
    /// <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires at EOT).
    /// When ActiveEffects is null (shape-only tests), the registration is
    /// a no-op. Mirrors <see cref="DismemberFactory.BuildDefinition"/>
    /// modulo the sign of the pump.
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        "Mutagenic Growth — target creature gets +2/+2 until end of turn",
                        () => Resolve(raw)),
                };
            });

    private static void Resolve(object raw)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        // Register a +2/+2 EOT-scoped Layer 7c effect on the target creature.
        // Same pattern as DismemberFactory / GuideOfSoulsFactory. When
        // ActiveEffects is null (shape tests without a live
        // ContinuousEffectsService), the effect registration is a no-op.
        if (target.ActiveEffects == null) return;
        target.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(target, 2, 2));
    }
}
