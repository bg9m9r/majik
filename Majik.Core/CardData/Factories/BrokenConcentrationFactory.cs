using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Spells;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Broken Concentration (Torment, {1}{U}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Counter target spell.
///    Madness {3}{U}"
///
/// ## Implementation
///
/// The vanilla hard counter — same <see cref="NegateFactory"/> shape but with
/// no type filter and no "unless pays" rider, identical body to
/// <see cref="CancelFactory"/>. On resolution the target spell is countered via
/// <see cref="Majik.Core.Primitives.Fx.Counter"/> (which aliases
/// <see cref="OracleSpellBinder.RemoveFromStack"/> + graveyard zone-move,
/// honouring uncounterable spells — CR 701.5b).
///
/// Card shape comes from the embedded JSON (<c>broken-concentration.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's
/// <see cref="Majik.Core.Game.GameContext"/> (not expressible in the data-only
/// JSON schema).
///
/// ## Madness {3}{U}
///
/// Madness (CR 702.35) is handled intrinsically by the engine: the
/// "Broken Concentration" → "{3}{U}" entry in
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> is consulted by the central
/// discard funnel (<see cref="Majik.Core.Primitives.Fx.DiscardCard"/>), which
/// routes a discarded madness card to exile and offers it for its madness cost.
/// No per-card wiring is required here.
/// </summary>
[CardName("Broken Concentration")]
public static class BrokenConcentrationFactory
{
    public const string CardName = "Broken Concentration";
    public const string Slug = "broken-concentration";
    public const string PrintedManaCost = "{1}{U}{U}";

    /// <summary>
    /// The fluent CardDef — "Counter target spell." compiles to a 1..1
    /// "target spell" <see cref="TargetRequest"/> + a
    /// <see cref="Majik.Core.Primitives.Fx.Counter"/> resolve step. Used by
    /// <see cref="BuildSpellDefinition"/>; the card shape itself loads from the
    /// embedded JSON (see <see cref="Create"/>).
    /// </summary>
    public static CardDef Define() => CardDef
        .Instant(CardName, PrintedManaCost)
        .Resolve(c => c.Counter(TargetKind.Spell));

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "counter target spell" SpellDefinition. Any spell on the
    /// stack is a legal target (no type filter) — CR 701.5. Delegates to the
    /// fluent <c>.Resolve(...)</c> body via
    /// <see cref="CardDefRuntime.BuildSpellDefinition"/>.
    /// </summary>
    /// <param name="targetResolver">Target resolver from the caller's
    /// <see cref="Majik.Core.Game.GameContext"/> (chosen → live stack object).</param>
    /// <param name="stack">Live stack — required to remove the countered
    /// spell. Null in pure-shape tests; the effect becomes a no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack) =>
        CardDefRuntime.BuildSpellDefinition(Define(), targetResolver, stack: stack);
}
