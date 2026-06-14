using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kuldotha Rebirth (Scars of Mirrodin, {R}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-14):
///   "As an additional cost to cast this spell, sacrifice an artifact.
///    Create three 1/1 red Goblin creature tokens."
///
/// ## Card shape
/// This factory builds only the Sorcery identity ({R}, red, mana value 1)
/// from the embedded JSON def (<c>kuldotha-rebirth.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. Having the <c>[CardName]</c> factory
/// flips <c>IsImplemented</c> on automatically (the registry-derived flag in
/// <see cref="EmbeddedCardRepository"/>).
///
/// ## Behaviour (prod cast path)
/// The spell's resolution + additional cost are bound at CAST TIME by the
/// oracle-text binder, NOT by this factory: cards do not carry their spell
/// definitions (see <see cref="SpellDefinitionResolverFactory"/> /
/// <see cref="ScryfallCardFactory.LookupSpellDefinition"/>). The dedicated
/// <see cref="SpellTemplates.Templates.Bespoke.KuldothaRebirthTemplate"/>
/// owns both halves:
/// - <b>Additional cost (CR 601.2f)</b>:
///   <see cref="Costs.SacrificeAnArtifactAdditionalCost"/> — the caster
///   sacrifices an artifact they control. The cast is illegal when the caster
///   controls no artifact (CR 601.2g). A bespoke template is required because
///   the generic <c>CreateTokensTemplate</c> would otherwise create the
///   Goblins for free — <see cref="SpellTemplates.OracleTextNormalizer"/>
///   strips the additional-cost sentence before normalized matching, so the
///   cost would be silently dropped on the generic path.
/// - <b>Resolution</b>: create three 1/1 red Goblin creature tokens under the
///   caster (CR 111 / CR 111.4).
/// </summary>
[CardName("Kuldotha Rebirth")]
public static class KuldothaRebirthFactory
{
    public const string CardName = "Kuldotha Rebirth";
    public const string Slug = "kuldotha-rebirth";
    public const string PrintedManaCost = "{R}";

    /// <summary>
    /// Build Kuldotha Rebirth as a Sorcery owned by <paramref name="owner"/>
    /// from the embedded JSON def. Card shape only — the additional-cost +
    /// token-creation body is bound at cast time by
    /// <see cref="SpellTemplates.Templates.Bespoke.KuldothaRebirthTemplate"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }
}
