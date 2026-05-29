using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Floodfarm Verge (Duskmourn: House of Horror).
///
/// WU Verge cycle — counterpart to Gloomlake Verge (UB), Wastewood Verge
/// (GB), Sunsplit Verge (RW), and Gleamfield Verge (GW).
///
/// Land. Oracle text (verified against Scryfall):
///   "{T}: Add {W}.
///    {T}: Add {U}. Activate only if you control a Plains or an Island."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/floodfarm-verge.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card.
///
/// ## Implemented (JSON pipeline)
/// - {T}: Add {W} mana ability — wired.
/// - {T}: Add {U} mana ability — wired.
///
/// CR 605.1 — both abilities are mana abilities; they do not use the stack.
///
/// ## Deferred (matches the sibling Wastewood Verge factory)
/// - <b>"Activate only if you control a Plains or an Island"</b> (CR 605.4 —
///   an activation restriction on the {U} ability, checked before paying
///   {T}). The current <c>ManaAbilityDefinition</c> JSON schema carries only
///   a <c>produces</c> field, so the restriction is not expressible through
///   the data pipeline. As with Wastewood Verge and the Kaladesh fastlands,
///   the conditional is deferred to the binder layer; this named-card factory
///   wires the two mana outputs without the activation predicate.
/// </summary>
[CardName("Floodfarm Verge")]
public static class FloodfarmVergeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("floodfarm-verge");

    /// <summary>
    /// Construct Floodfarm Verge owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
