using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reach Through Mists (Champions of Kamigawa, {U}).
///
/// Instant — Arcane. Oracle text (verified against Scryfall):
///   "Draw a card."
///
/// ## Declarative spell schema (cantrip-factory-harvest pay-down)
/// The resolve body is the single declarative verb <c>[draw_card(1)]</c>
/// handed to <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> —
/// the shared <see cref="DrawCardEffectDef"/> verb. The draw routes through
/// the verb's <see cref="Majik.Core.Primitives.Fx.DrawCards"/> path
/// (ReplacementBus + empty-library SBA flag, CR 614 / 120.3 / 704.5b).
///
/// ## Arcane subtype
/// The printed "Arcane" subtype IS stamped via the CardDef DSL (CR 205.3k) so
/// the Arcane line is present for the splice gate (CR 702.46 — a Splice onto
/// Arcane rider may only attach to a spell with the Arcane subtype). Same
/// posture as <see cref="KodamasReachFactory"/>.
/// </summary>
[CardName("Reach Through Mists")]
public static class ReachThroughMistsFactory
{
    public const string CardName = "Reach Through Mists";
    public const string PrintedManaCost = "{U}";

    /// <summary>The single declarative resolve verb: draw 1.</summary>
    internal static EffectDefinition[] EffectDefs() => new EffectDefinition[]
    {
        new DrawCardEffectDef { Amount = 1 },
    };

    /// <summary>CardDef DSL — card shape only (Instant — Arcane).</summary>
    public static CardDef Define() =>
        CardDef.Instant(CardName, PrintedManaCost).WithSubtype(CardSubtype.Arcane);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>Declarative SpellDefinition (draw 1).</summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(CardName, EffectDefs());

    /// <summary>
    /// Build Reach Through Mists's resolve effect — draw a card. Returns a
    /// SINGLE composite <see cref="IEffect"/> so the legacy <c>.Single()</c>
    /// caller contract holds.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster) =>
        CantripEffectComposer.Compose(CardName, caster, EffectDefs());
}
