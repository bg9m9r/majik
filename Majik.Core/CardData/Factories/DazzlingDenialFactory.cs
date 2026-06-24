using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dazzling Denial (Bloomburrow, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Counter target spell unless its controller pays {2}. If you control a
///    Bird, counter that spell unless its controller pays {4} instead."
///
/// ## Card shape
/// This factory builds only the Instant identity ({1}{U}, blue, mana value 2)
/// from the embedded JSON def (<c>dazzling-denial.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. Having the <c>[CardName]</c> factory
/// flips <c>IsImplemented</c> on automatically (the registry-derived flag in
/// <see cref="EmbeddedCardRepository"/>).
///
/// ## Behaviour (prod cast path)
/// Cards do not carry their spell definitions — the resolution body is bound at
/// CAST TIME by the oracle-text binder
/// (<see cref="ScryfallCardFactory.LookupSpellDefinition"/> →
/// <see cref="OracleSpellBinder"/>). The dedicated
/// <see cref="DazzlingDenialTemplate"/> owns the soft counter plus the
/// Bird-conditional {2}-vs-{4} tax. A bespoke template is required because the
/// generic
/// <see cref="SpellTemplates.Templates.Counter.CounterUnlessPayTemplate"/> would
/// otherwise match the first sentence and silently drop the "pay {4} instead"
/// rider.
/// </summary>
[CardName("Dazzling Denial")]
public static class DazzlingDenialFactory
{
    public const string CardName = "Dazzling Denial";
    public const string Slug = "dazzling-denial";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>
    /// Build Dazzling Denial as an Instant owned by <paramref name="owner"/>
    /// from the embedded JSON def. Card shape only — the counter + Bird-gated
    /// tax body is bound at cast time by <see cref="DazzlingDenialTemplate"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the runnable <see cref="SpellDefinition"/> for Dazzling Denial.
    /// Delegates to <see cref="DazzlingDenialTemplate.Build"/> so the prod binder
    /// path and tests share one source of truth.
    /// </summary>
    /// <param name="caster">The player who cast the spell — whose board is
    /// scanned for a Bird at resolution (CR 118.4).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack) =>
        DazzlingDenialTemplate.Build(caster, targetResolver, stack);
}
