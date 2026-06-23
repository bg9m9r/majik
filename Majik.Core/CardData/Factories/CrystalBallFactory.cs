using System;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Crystal Ball (Magic 2015 / various, {3}).
///
/// Artifact. Oracle text (Scryfall-confirmed 2026-06-23):
///   "{1}, {T}: Scry 2. (Look at the top two cards of your library, then put
///    any number of them on the bottom and the rest on top in any order.)"
///
/// Scryfall type line: Artifact (no subtype). Mana cost {3}.
///
/// ## Card identity + ability come from JSON
///
/// Name / type / {3} mana cost and the single activated ability are loaded from
/// the embedded JSON definition (<c>crystal-ball.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>:
///
/// - <b>{1}, {T}: Scry 2</b> — an <see cref="Majik.Core.Abilities.ActivatedAbility"/>
///   (JSON <c>{ "kind": "activated", costs:[{1} mana, tap_self], effects:[scry_self 2] }</c>).
///   This is the same activated-scry shape as Seer's Lantern's {2}, {T}: Scry 1,
///   differing only in the cheaper {1} cost and the deeper Scry 2 window. The
///   scry resolves through the standard <c>scry_self</c> path (CR 701.20 — scry
///   keyword action): with a registered
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> the controller picks
///   the bottom/top partition; the pre-agent default puts the peeked cards on
///   the bottom. The ability DOES use the stack (CR 602.2 — it is not a mana
///   ability). Unlike Witching Well / the scry-land cycle (where scry is an ETB
///   <i>triggered</i> ability), Crystal Ball's scry is a repeatable activated
///   ability paid for with {1} + {T}.
/// </summary>
[CardName("Crystal Ball")]
public static class CrystalBallFactory
{
    public const string CardName = "Crystal Ball";
    public const string PrintedManaCost = "{3}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("crystal-ball");

    /// <summary>
    /// Construct Crystal Ball owned and controlled by <paramref name="owner"/>.
    /// Identity and the {1}, {T}: Scry 2 activated ability come from JSON.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
