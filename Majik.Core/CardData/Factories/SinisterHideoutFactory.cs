using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sinister Hideout (Murders at Karlov Manor Commander /
/// reprints).
///
/// Land. Oracle text (Scryfall-confirmed 2026-06-24):
///   "This land enters tapped.
///    {T}: Add {U} or {B}.
///    {4}, {T}: Surveil 1. (Look at the top card of your library. You may put it
///    into your graveyard.)"
///
/// Scryfall type line: Land (no basic supertype, no subtypes). Sinister Hideout
/// is the U/B member of the "creature land surveil" / pay-to-surveil land family
/// — same shape as <see cref="CastleVantressFactory"/> (JSON identity + mana
/// ability + a {cost}, {T} activated card-advantage ability) except the activated
/// ability is Surveil 1 rather than Scry 2, the produced colours are two
/// (<c>{U}</c> / <c>{B}</c>), and the ETB-tapped clause is UNCONDITIONAL.
///
/// ## Card identity + abilities come from JSON
///
/// Name / type, the two <b>{T}: Add {U}</b> / <b>{T}: Add {B}</b> mana
/// abilities, and the <b>{4}, {T}: Surveil 1</b> activated ability are loaded
/// from the embedded JSON definition (<c>sinister-hideout.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The Surveil 1 effect uses the standard
/// <c>surveil_self</c> path (CR 701.42): when an
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> is registered the
/// controller decides which peeked cards go to the graveyard; otherwise the
/// pre-agent default sends the peeked card to the graveyard. Same posture as
/// <see cref="SinisterStarfishFactory"/> (JSON-declared <c>{cost}: Surveil N</c>).
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain nonbasic Land, no supertype, no subtype
///   (from JSON).
/// - <b>{T}: Add {U} or {B}</b> — two vanilla <see cref="Abilities.ManaAbility"/>s,
///   one per produced colour (CR 605.1a), from JSON.
/// - <b>{4}, {T}: Surveil 1</b> — an <see cref="Abilities.ActivatedAbility"/>
///   whose cost stack is a ManaCostCost({4}) + a tap-self additional cost,
///   resolving the standard <c>surveil_self</c> effect (CR 701.42), from JSON.
/// - <b>This land enters tapped (CR 614.1c)</b> — registered as an
///   unconditional <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> (the JSON schema models no ETB-tapped clause,
///   so it is wired in code — same split as <see cref="CastleVantressFactory"/>,
///   except unconditional). The single-arg dispatcher path omits the
///   replacement (shape-only posture); production gets the replacement from
///   <see cref="EntersTappedBinder"/> via <see cref="ScryfallCardFactory"/>.
/// </summary>
[CardName("Sinister Hideout")]
public static class SinisterHideoutFactory
{
    public const string CardName = "Sinister Hideout";
    public const string Slug = "sinister-hideout";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Sinister Hideout without a <see cref="ReplacementBus"/> wired.
    /// The unconditional ETB-tapped replacement is omitted (shape-only posture);
    /// the two mana abilities and the Surveil ability (from JSON) are still
    /// attached. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Sinister Hideout.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "this land enters tapped" replacement is registered (CR 614.1c).
    /// May be null.</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the two {T}: Add {U}/{B} mana abilities and the
        // {4},{T}: Surveil 1 activated ability all come from JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // "This land enters tapped." — unconditional (CR 614.1c). Wired in
        // code because the card-definition schema models no ETB-tapped clause.
        replacements?.Register(new EntersTappedReplacement(land));

        return land;
    }
}
