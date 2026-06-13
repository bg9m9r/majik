using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Curate (Theros Beyond Death, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Surveil 2. (Look at the top two cards of your library, then put any
///    number of them into your graveyard and the rest on top of your library
///    in any order.)
///    Draw a card."
///
/// ## Declarative spell schema (cantrip-factory-harvest pay-down)
/// The resolve body is the ORDERED declarative verb array
/// <c>[surveil_self(2), draw_card(1)]</c> handed to
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the shared
/// <see cref="SurveilSelfEffectDef"/> / <see cref="DrawCardEffectDef"/> verbs
/// (the same path Consider, Opt, Preordain, Serum Visions and every Theros
/// scry-land already use). ORDER MATTERS — Curate surveils BEFORE the draw
/// (CR 701.42 then CR 121.1, sequenced left-to-right), so the draw pulls the
/// post-surveil top. Agent surveil decision flows through
/// <see cref="Majik.Core.Players.Agents.AgentRegistry"/>; an empty-library draw
/// flags the draw-from-empty SBA (CR 120.3 / 704.5b) via the <c>draw_card</c>
/// verb's <see cref="Majik.Core.Primitives.Fx.DrawCards"/> route.
///
/// Curate is functionally Consider ({U} Instant, "Surveil 1. Draw a card.") at
/// the additional cost of {1} and one extra surveil depth.
/// </summary>
[CardName("Curate")]
public static class CurateFactory
{
    public const string CardName = "Curate";
    public const string PrintedManaCost = "{1}{U}";
    private const int SurveilAmount = 2;

    /// <summary>The ordered declarative resolve verbs: surveil 2, then draw 1.</summary>
    internal static EffectDefinition[] EffectDefs() => new EffectDefinition[]
    {
        new SurveilSelfEffectDef { Amount = SurveilAmount },
        new DrawCardEffectDef { Amount = 1 },
    };

    /// <summary>CardDef DSL — card shape only.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>Declarative SpellDefinition (surveil 2, then draw 1).</summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(CardName, EffectDefs());

    /// <summary>
    /// Build Curate's resolve effect — surveil 2, then draw a card. Returns a
    /// SINGLE composite <see cref="IEffect"/> so the legacy <c>.Single()</c>
    /// caller contract holds.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster) =>
        CantripEffectComposer.Compose(CardName, caster, EffectDefs());
}
