using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Weave Fate (Magic Origins, {3}{U}).
///
/// Instant. Oracle text:
///   "Draw two cards."
///
/// ## Declarative spell schema (cantrip-factory-harvest pay-down)
/// A pure draw cantrip: the resolve body is the single-verb declarative array
/// <c>[draw_card(2)]</c> handed to
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the same
/// <see cref="DrawCardEffectDef"/> verb the JSON draw cards and Opt / Serum
/// Visions already use. The draw routes through the verb's
/// <see cref="Majik.Core.Primitives.Fx.DrawCards"/> path (per-draw ReplacementBus
/// + the empty-library SBA loss flag, CR 614 / 120.3 / 704.5b); no bespoke
/// resolve closure. Distinct from Divination (same effect, sorcery) only by the
/// instant speed + cost.
/// </summary>
[CardName("Weave Fate")]
public static class WeaveFateFactory
{
    public const string CardName = "Weave Fate";
    public const string PrintedManaCost = "{3}{U}";
    private const int DrawAmount = 2;

    /// <summary>The ordered declarative resolve verbs: draw 2.</summary>
    internal static EffectDefinition[] EffectDefs() => new EffectDefinition[]
    {
        new DrawCardEffectDef { Amount = DrawAmount },
    };

    /// <summary>CardDef DSL — card shape only.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>Declarative SpellDefinition (draw 2).</summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(CardName, EffectDefs());

    /// <summary>
    /// Build Weave Fate's resolve effect — draw two cards. Returns a SINGLE
    /// composite <see cref="IEffect"/> so the legacy <c>.Single()</c> caller
    /// contract holds.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster) =>
        CantripEffectComposer.Compose(CardName, caster, EffectDefs());
}
