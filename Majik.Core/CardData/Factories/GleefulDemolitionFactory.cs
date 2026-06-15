using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gleeful Demolition (Phyrexia: All Will Be One, {R}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-14):
///   "Destroy target artifact. If you controlled that artifact, create three
///    1/1 red Phyrexian Goblin creature tokens."
///
/// ## Card shape
/// This factory builds only the Sorcery identity ({R}, red, mana value 1) from
/// the embedded JSON def (<c>gleeful-demolition.json</c>) via
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
/// <see cref="GleefulDemolitionTemplate"/> owns the destroy-target-artifact +
/// conditional own-artifact Phyrexian Goblin token rider. A bespoke template is
/// required because the generic
/// <see cref="SpellTemplates.Templates.Destroy.DestroyArtifactEnchantmentTemplate"/>
/// would otherwise match "destroy target artifact" and silently drop the token
/// rider.
/// </summary>
[CardName("Gleeful Demolition")]
public static class GleefulDemolitionFactory
{
    public const string CardName = "Gleeful Demolition";
    public const string Slug = "gleeful-demolition";
    public const string PrintedManaCost = "{R}";

    /// <summary>
    /// Build Gleeful Demolition as a Sorcery owned by <paramref name="owner"/>
    /// from the embedded JSON def. Card shape only — the destroy +
    /// conditional-token body is bound at cast time by
    /// <see cref="GleefulDemolitionTemplate"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the runnable <see cref="SpellDefinition"/> for Gleeful Demolition.
    /// Delegates to <see cref="GleefulDemolitionTemplate.Build"/> so the prod
    /// binder path and tests share one source of truth.
    /// </summary>
    /// <param name="caster">The player who cast the spell; controls any tokens.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand engine
    /// objects directly.</param>
    /// <param name="zoneService">Optional zone service — routes token ETBs
    /// through <see cref="ZoneService"/> so events publish.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> targetResolver,
        ZoneService? zoneService = null) =>
        GleefulDemolitionTemplate.Build(caster, targetResolver, zoneService);
}
