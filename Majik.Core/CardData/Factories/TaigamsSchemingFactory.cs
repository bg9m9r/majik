using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Taigam's Scheming (Khans of Tarkir, {1}{U}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Surveil 5. (Look at the top five cards of your library, then put any
///    number of them into your graveyard and the rest on top of your library
///    in any order.)"
///
/// ## Declarative spell schema (cantrip-factory-harvest pay-down)
/// The resolve body is the single declarative verb
/// <c>[surveil_self(5)]</c> handed to
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the shared
/// <see cref="SurveilSelfEffectDef"/> verb (CR 701.42). Functionally identical
/// to Contingency Plan (also {1}{U}, "Surveil 5"). The modern Oracle wording
/// errata'd the original "look at top five, put any number in graveyard, rest
/// on top in any order" text into the Surveil 5 keyword, so it composes from
/// the existing verb with no bespoke milling code. Agent surveil decision
/// flows through <see cref="Majik.Core.Players.Agents.AgentRegistry"/>; with no
/// agent the pre-agent default sends all peeked cards to the graveyard.
/// </summary>
[CardName("Taigam's Scheming")]
public static class TaigamsSchemingFactory
{
    public const string CardName = "Taigam's Scheming";
    public const string PrintedManaCost = "{1}{U}";
    private const int SurveilAmount = 5;

    /// <summary>The single declarative resolve verb: surveil 5.</summary>
    internal static EffectDefinition[] EffectDefs() => new EffectDefinition[]
    {
        new SurveilSelfEffectDef { Amount = SurveilAmount },
    };

    /// <summary>CardDef DSL — card shape only.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>Declarative SpellDefinition (surveil 5).</summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(CardName, EffectDefs());

    /// <summary>
    /// Build Taigam's Scheming's resolve effect — surveil 5. Returns a SINGLE
    /// composite <see cref="IEffect"/> so the legacy <c>.Single()</c> caller
    /// contract holds.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster) =>
        CantripEffectComposer.Compose(CardName, caster, EffectDefs());
}
