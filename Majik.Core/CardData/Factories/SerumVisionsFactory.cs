using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Serum Visions (Fifth Dawn / Modern Horizons, {U}).
///
/// Sorcery. Oracle text:
///   "Draw a card. Scry 2."
///
/// ## Declarative spell schema (cantrip-factory-harvest pay-down)
/// The resolve body is the ORDERED declarative verb array
/// <c>[draw_card(1), scry_self(2)]</c> handed to
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>. ORDER MATTERS —
/// Serum Visions resolves the draw BEFORE the scry (CR 121.1 then CR 701.20,
/// sequenced left-to-right), so the scry inspects the post-draw top. The draw
/// routes through the <c>draw_card</c> verb's
/// <see cref="Majik.Core.Primitives.Fx.DrawCards"/> path (ReplacementBus +
/// empty-library SBA flag, CR 614 / 120.3 / 704.5b); the scry decision flows
/// through <see cref="Majik.Core.Players.Agents.AgentRegistry"/>.
/// </summary>
[CardName("Serum Visions")]
public static class SerumVisionsFactory
{
    public const string CardName = "Serum Visions";
    public const string PrintedManaCost = "{U}";
    private const int ScryAmount = 2;

    /// <summary>The ordered declarative resolve verbs: draw 1, then scry 2.</summary>
    internal static EffectDefinition[] EffectDefs() => new EffectDefinition[]
    {
        new DrawCardEffectDef { Amount = 1 },
        new ScrySelfEffectDef { Amount = ScryAmount },
    };

    /// <summary>CardDef DSL — card shape only.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>Declarative SpellDefinition (draw 1, then scry 2).</summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(CardName, EffectDefs());

    /// <summary>
    /// Build Serum Visions' resolve effect — draw a card, then scry 2. Returns
    /// a SINGLE composite <see cref="IEffect"/> so the legacy <c>.Single()</c>
    /// caller contract holds.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster) =>
        CantripEffectComposer.Compose(CardName, caster, EffectDefs());
}
