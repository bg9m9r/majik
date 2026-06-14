using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tidings (Tenth Edition and reprints, {3}{U}{U}).
///
/// Sorcery. Oracle text:
///   "Draw four cards."
///
/// ## Declarative spell schema (cantrip-factory-harvest pay-down)
/// A pure draw cantrip: the resolve body is the single-verb declarative array
/// <c>[draw_card(4)]</c> handed to
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the same
/// <see cref="DrawCardEffectDef"/> verb the JSON draw cards and Opt / Serum
/// Visions already use. The draw routes through the verb's
/// <see cref="Majik.Core.Primitives.Fx.DrawCards"/> path (per-draw ReplacementBus
/// + the empty-library SBA loss flag, CR 614 / 120.3 / 704.5b); no bespoke
/// resolve closure.
/// </summary>
[CardName("Tidings")]
public static class TidingsFactory
{
    public const string CardName = "Tidings";
    public const string PrintedManaCost = "{3}{U}{U}";
    private const int DrawAmount = 4;

    /// <summary>The ordered declarative resolve verbs: draw 4.</summary>
    internal static EffectDefinition[] EffectDefs() => new EffectDefinition[]
    {
        new DrawCardEffectDef { Amount = DrawAmount },
    };

    /// <summary>CardDef DSL — card shape only.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>Declarative SpellDefinition (draw 4).</summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(CardName, EffectDefs());

    /// <summary>
    /// Build Tidings' resolve effect — draw four cards. Returns a SINGLE
    /// composite <see cref="IEffect"/> so the legacy <c>.Single()</c> caller
    /// contract holds.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster) =>
        CantripEffectComposer.Compose(CardName, caster, EffectDefs());
}
