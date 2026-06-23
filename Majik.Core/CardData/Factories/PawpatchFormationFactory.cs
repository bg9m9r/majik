using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pawpatch Formation (Bloomburrow, {1}{G}).
///
/// Instant. Oracle text (verified against Scryfall, scryfallId
/// b82c20ad-0f69-4822-ae76-770832cccdf7):
///   "Choose one —
///     • Destroy target creature with flying.
///     • Destroy target enchantment.
///     • Draw a card. Create a Food token. (It's an artifact with
///       "{2}, {T}, Sacrifice this token: You gain 3 life.")"
///
/// CR 700.2d — modal "Choose one —" spell. This factory is intentionally
/// thin: it materialises only the card's base shape (name, single Instant
/// card type, {1}{G}) from the embedded JSON definition
/// (<c>pawpatch-formation.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same data-only posture as
/// <see cref="AncientGrudgeFactory"/>.
///
/// Unlike the bespoke charm factories (Boros / Bant / Izzet), Pawpatch
/// Formation needs NO hand-written <c>BuildDefinition</c>: every one of its
/// three modes is already a pattern the live spell-template registry binds
/// from oracle text at cast time. The card's resolution therefore flows
/// through the SAME prod path as any other template-driven instant —
/// <c>TurnDriver</c> → <see cref="SpellDefinitionResolverFactory"/> →
/// <see cref="ScryfallCardFactory.LookupSpellDefinition"/> →
/// <see cref="OracleSpellBinder.Bind"/>, where
/// <see cref="SpellTemplates.ModalChooseOneTemplate"/> (Priority 250) splits
/// the bullet list into modes and rebinds each body against the registry:
///   • Mode 0 "Destroy target creature with flying." →
///     <see cref="SpellTemplates.Templates.Destroy.DestroyCreatureTemplate"/>
///     (the "with flying" modifier chain is absorbed by its modifier regex;
///     v1 destroys the chosen target — the flying restriction lives in the
///     target predicate, CR 608.2b).
///   • Mode 1 "Destroy target enchantment." →
///     <see cref="SpellTemplates.Templates.Destroy.DestroyArtifactEnchantmentTemplate"/>
///     (destroys via <c>MoveToGraveyard(…, Destroy)</c> so indestructible /
///     regeneration shields are honoured, CR 701.7 / 702.12).
///   • Mode 2 "Draw a card. Create a Food token." → a compound clause bound
///     by <see cref="SpellTemplates.ClauseCompositionTemplate"/> (Priority
///     200), composing
///     <see cref="SpellTemplates.Templates.Resource.DrawCardsTemplate"/>
///     (CR 121.1 — draw 1) and
///     <see cref="SpellTemplates.Templates.Tokens.CreateFoodTokensTemplate"/>
///     (CR 111 — create one Food artifact token via
///     <see cref="Majik.Core.Tokens.TokenFactory.CreateFood"/>, which wires
///     the printed "{2}, {T}, Sacrifice this token: You gain 3 life."
///     activated ability).
///
/// Because all behaviour is template-driven, adding this factory exists
/// purely to flip <c>IsImplemented</c> (derived from the <c>[CardName]</c>
/// registry) and to satisfy the per-card contract test. The companion
/// <see cref="PawpatchFormationFactoryTests"/> asserts the modal binding +
/// per-mode resolution directly through <see cref="OracleSpellBinder.Bind"/>
/// against the printed oracle text (the same entity the embedded card pool
/// supplies in prod).
/// </summary>
[CardName("Pawpatch Formation")]
public static class PawpatchFormationFactory
{
    public const string CardName = "Pawpatch Formation";
    public const string Slug = "pawpatch-formation";
    public const string PrintedManaCost = "{1}{G}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }
}
