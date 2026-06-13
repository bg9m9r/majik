using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Preordain (Magic 2011 / Modern Horizons 3, {U}).
///
/// Sorcery. Oracle text:
///   "Scry 2, then draw a card."
///
/// ## Declarative spell schema (cantrip-factory-harvest pay-down)
/// The resolve body is the ORDERED declarative verb array
/// <c>[scry_self(2), draw_card(1)]</c> handed to
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the shared
/// <see cref="ScrySelfEffectDef"/> / <see cref="DrawCardEffectDef"/> verbs.
/// Scry before draw (CR 701.20 then CR 121.1). Agent scry decision flows
/// through <see cref="Majik.Core.Players.Agents.AgentRegistry"/>; an
/// empty-library draw flags the draw-from-empty SBA (CR 120.3 / 704.5b) via
/// the <c>draw_card</c> verb's <see cref="Majik.Core.Primitives.Fx.DrawCards"/>
/// route.
/// </summary>
[CardName("Preordain")]
public static class PreordainFactory
{
    public const string CardName = "Preordain";
    public const string PrintedManaCost = "{U}";
    private const int ScryAmount = 2;

    /// <summary>The ordered declarative resolve verbs: scry 2, then draw 1.</summary>
    internal static EffectDefinition[] EffectDefs() => new EffectDefinition[]
    {
        new ScrySelfEffectDef { Amount = ScryAmount },
        new DrawCardEffectDef { Amount = 1 },
    };

    /// <summary>CardDef DSL — card shape only.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>Declarative SpellDefinition (scry 2, then draw 1).</summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(CardName, EffectDefs());

    /// <summary>
    /// Build Preordain's resolve effect — scry 2, then draw a card. Returns a
    /// SINGLE composite <see cref="IEffect"/> so the legacy <c>.Single()</c>
    /// caller contract holds.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster) =>
        CantripEffectComposer.Compose(CardName, caster, EffectDefs());
}
