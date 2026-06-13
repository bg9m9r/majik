using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Consider (Innistrad: Midnight Hunt, {U}).
///
/// Instant. Oracle text:
///   "Surveil 1. (Look at the top card of your library. You may put it into
///    your graveyard.) Draw a card."
///
/// Surveil 1 (CR 701.42) then draw a card (CR 121.1), sequenced
/// surveil-before-draw.
///
/// ## Declarative spell schema (cantrip-factory-harvest pay-down)
/// The resolve body is the ORDERED declarative verb array
/// <c>[surveil_self(1), draw_card(1)]</c> handed to
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the shared
/// <see cref="SurveilSelfEffectDef"/> / <see cref="DrawCardEffectDef"/> verbs.
/// Agent surveil decision flows through
/// <see cref="Majik.Core.Players.Agents.AgentRegistry"/>; an empty-library draw
/// flags the draw-from-empty SBA (CR 120.3 / 704.5b) via the <c>draw_card</c>
/// verb's <see cref="Majik.Core.Primitives.Fx.DrawCards"/> route.
/// </summary>
[CardName("Consider")]
public static class ConsiderFactory
{
    public const string CardName = "Consider";
    public const string PrintedManaCost = "{U}";

    /// <summary>The ordered declarative resolve verbs: surveil 1, then draw 1.</summary>
    internal static EffectDefinition[] EffectDefs() => new EffectDefinition[]
    {
        new SurveilSelfEffectDef { Amount = 1 },
        new DrawCardEffectDef { Amount = 1 },
    };

    /// <summary>CardDef DSL — card shape only.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>Declarative SpellDefinition (surveil 1, then draw 1).</summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(CardName, EffectDefs());

    /// <summary>
    /// Build Consider's resolve effect — surveil 1, then draw a card. Returns a
    /// SINGLE composite <see cref="IEffect"/> so the legacy <c>.Single()</c>
    /// caller contract holds.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster) =>
        CantripEffectComposer.Compose(CardName, caster, EffectDefs());
}
