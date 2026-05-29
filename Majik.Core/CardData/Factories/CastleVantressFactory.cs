using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Castle Vantress (Throne of Eldraine / reprints).
///
/// Land. Oracle text (Scryfall-confirmed):
///   "This land enters tapped unless you control an Island.
///    {T}: Add {U}.
///    {2}{U}{U}, {T}: Scry 2."
///
/// Scryfall type line: Land (no basic supertype, no subtypes).
/// Castle Vantress is NOT itself an Island.
///
/// Same Eldraine Castle cycle as <see cref="CastleArdenvaleFactory"/> — the
/// only differences are the gating subtype (Island vs Plains), the produced
/// colour ({U} vs {W}), and the second activated ability (Scry 2 vs token
/// creation).
///
/// ## Card identity + abilities come from JSON
///
/// Name / type, the <b>{T}: Add {U}</b> mana ability, and the
/// <b>{2}{U}{U}, {T}: Scry 2</b> activated ability are loaded from the
/// embedded JSON definition (<c>castle-vantress.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The Scry 2 effect uses the standard
/// <c>scry_self</c> path (CR 701.20): when an
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> is registered the
/// controller decides the bottom/top partition; otherwise the pre-agent
/// default sends all peeked cards to the bottom. Same posture as
/// <see cref="MinamoSchoolAtWatersEdgeFactory"/> (JSON identity + abilities)
/// and <see cref="SeaGateRebornFactory"/> (JSON abilities + code-side ETB
/// replacement).
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain nonbasic Land, no supertype, no subtype
///   (from JSON).
/// - <b>{T}: Add {U}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1), from JSON.
/// - <b>{2}{U}{U}, {T}: Scry 2</b> — <see cref="ActivatedAbility"/> whose
///   cost stack is a ManaCostCost({2}{U}{U}) + a tap-self additional cost,
///   resolving the standard <c>scry_self</c> effect (CR 701.20), from JSON.
/// - <b>ETB tapped unless you control an Island (CR 614.1c)</b> — registered
///   as a <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> (the JSON schema models no conditional
///   ETB-tapped, so it is wired in code — same split as
///   <see cref="SeaGateRebornFactory"/>). The predicate checks whether the
///   controller controls at least one other permanent with the
///   <see cref="CardSubtype.Island"/> subtype (dual lands with the Island
///   subtype, snow-covered Islands, etc. all qualify). The card itself is
///   excluded via reference equality (Castle Vantress is not an Island).
///   Single-arg dispatcher path omits the replacement (shape-only posture).
/// </summary>
[CardName("Castle Vantress")]
public static class CastleVantressFactory
{
    public const string CardName = "Castle Vantress";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("castle-vantress");

    /// <summary>
    /// Construct Castle Vantress without a <see cref="ReplacementBus"/>
    /// wired. The ETB-tapped-unless-Island predicate is omitted (shape-only
    /// posture); the mana ability and Scry ability (from JSON) are still
    /// attached.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Castle Vantress.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the
    /// "enters tapped unless you control an Island" replacement is registered
    /// (CR 614.1c). May be null.</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {U} mana ability and the {2}{U}{U},{T}:
        // Scry 2 activated ability all come from JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // ETB tapped unless you control an Island (CR 614.1c).
        //
        // Predicate: entersUntappedIf returns true ⟺ the controller
        // controls at least one land (other than this card) with the
        // CardSubtype.Island subtype. Reference-equality exclusion of self
        // mirrors CastleArdenvaleFactory's single-type predicate shape.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    controller.Zones.Battlefield.GetCards()
                        .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Island))));
        }

        return land;
    }
}
