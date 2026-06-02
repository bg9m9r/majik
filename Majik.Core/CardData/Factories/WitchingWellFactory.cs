using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Witching Well (Throne of Eldraine).
///
/// Artifact {U}. Oracle text (Scryfall-confirmed):
///   "When this artifact enters, scry 2. (Look at the top two cards of your
///    library, then put any number of them on the bottom and the rest on top
///    in any order.)
///    {3}{U}, Sacrifice this artifact: Draw two cards."
///
/// Scryfall type line: Artifact (no subtype). Mana cost {U}.
///
/// ## Card identity + abilities come from JSON
///
/// Name / type / mana cost, the <b>"When this artifact enters, scry 2"</b>
/// ETB triggered ability, and the <b>{3}{U}, Sacrifice this artifact: Draw
/// two cards</b> activated ability are all loaded from the embedded JSON
/// definition (<c>witching-well.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. No code-side wiring is required —
/// every ability shape is already modelled by the JSON schema:
/// <list type="bullet">
///   <item><b>ETB scry 2</b> — standard <c>scry_self</c> effect on an
///     <c>etb_self</c> trigger (CR 701.20 — scry keyword action). When an
///     <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> is registered the
///     controller decides the bottom/top partition of the two peeked cards;
///     otherwise the pre-agent default sends both to the bottom. Same
///     <c>scry_self</c> path as the Theros scry-land cycle
///     (<see cref="TempleOfMaliceFactory"/>) and Castle Vantress
///     (<see cref="CastleVantressFactory"/>).</item>
///   <item><b>{3}{U}, Sacrifice: Draw two cards</b> — an
///     <see cref="Majik.Core.Abilities.ActivatedAbility"/> whose cost stack is
///     a <see cref="Majik.Core.Costs.ManaCostCost"/>({3}{U}) +
///     a <c>sacrifice_self</c> additional cost (CR 701.16), resolving a
///     <c>draw_card</c> effect of amount 2. Same sac-to-draw artifact shape
///     as Dreamstone Hedron's <c>{3},{T}, Sacrifice: Draw three</c>
///     (<c>dreamstone-hedron.json</c>) — Witching Well drops the tap and
///     draws two instead of three.</item>
/// </list>
/// </summary>
[CardName("Witching Well")]
public static class WitchingWellFactory
{
    public const string CardName = "Witching Well";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("witching-well");

    /// <summary>
    /// Construct Witching Well owned and controlled by
    /// <paramref name="owner"/>. Identity, the ETB scry-2 triggered ability,
    /// and the {3}{U}, Sacrifice: Draw two cards activated ability all come
    /// from JSON.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
