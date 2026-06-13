using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Opt (Invasion / Ixalan / Modern Horizons 3, {U}).
///
/// Instant. Oracle text:
///   "Scry 1. (Look at the top card of your library. You may put that card on
///    the bottom.) Draw a card."
///
/// Scry 1 + draw 1, sequenced scry-before-draw (CR 701.20 then CR 121.1).
///
/// ## Declarative spell schema (cantrip-factory-harvest pay-down)
/// The resolve body is no longer a hand-rolled closure: it is the ORDERED
/// declarative verb array <c>[scry_self(1), draw_card(1)]</c> handed to
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the same
/// <see cref="ScrySelfEffectDef"/> / <see cref="DrawCardEffectDef"/> verbs
/// every Theros scry-land and the JSON draw effects already use. Agent scry
/// decision (via <see cref="Majik.Core.Players.Agents.AgentRegistry"/>) and the
/// empty-library draw-from-empty SBA flag (CR 120.3 / 704.5b, now routed
/// through <see cref="Majik.Core.Primitives.Fx.DrawCards"/>) come from the
/// shared verbs, not bespoke code.
/// </summary>
[CardName("Opt")]
public static class OptFactory
{
    public const string CardName = "Opt";
    public const string PrintedManaCost = "{U}";

    /// <summary>The ordered declarative resolve verbs: scry 1, then draw 1.</summary>
    internal static EffectDefinition[] EffectDefs() => new EffectDefinition[]
    {
        new ScrySelfEffectDef { Amount = 1 },
        new DrawCardEffectDef { Amount = 1 },
    };

    /// <summary>CardDef DSL — card shape only.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>Declarative SpellDefinition (scry 1, then draw 1).</summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(CardName, EffectDefs());

    /// <summary>
    /// Build Opt's resolve effect — scry 1, then draw a card. Returns a SINGLE
    /// composite <see cref="IEffect"/> (the ordered verb array, executed
    /// in-sequence) so existing callers' <c>.Single()</c> contract holds.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster) =>
        CantripEffectComposer.Compose(CardName, caster, EffectDefs());
}
