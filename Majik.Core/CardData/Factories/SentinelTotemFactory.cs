using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sentinel Totem (Hour of Devastation / reprints).
///
/// Artifact {1}. Oracle text (Scryfall-confirmed):
///   "When this artifact enters, scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {T}, Exile this artifact: Exile all graveyards."
///
/// <para>
/// ## Hybrid card identity
/// Name / Artifact type / {1} cost and the "When this artifact enters, scry 1"
/// ETB <see cref="TriggeredAbility"/> are declared in the embedded JSON
/// definition (<c>sentinel-totem.json</c>) and materialised via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The ETB effect uses the standard
/// <c>scry_self</c> path (CR 701.20): with a registered
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> the controller decides
/// the bottom/top partition; otherwise the pre-agent default puts the peeked
/// card on the bottom. Same JSON-identity posture as
/// <see cref="TempleOfDeceitFactory"/>.
/// </para>
///
/// <para>
/// ## Activated graveyard-hate ability (hand-built)
/// <b>{T}, Exile this artifact: Exile all graveyards.</b> There is no JSON
/// "exile all graveyards" effect verb, so this ability is constructed in C#
/// here — mirroring <see cref="RelicOfProgenitusFactory"/>'s sweep ability but
/// WITHOUT the "draw a card" tail and WITHOUT a target. Exile is not a mana
/// ability (CR 605 — it goes on the stack). The cost is {T} plus a self-exile
/// additional cost; the self-exile zone move (Battlefield → Exile) is performed
/// by the effect closure because the generic additional-cost pay path is a
/// no-op stub (same rationale as Relic of Progenitus / Mishra's Bauble).
/// </para>
///
/// <para>
/// ## All-graveyards sweep scoping
/// When <paramref name="allPlayersResolver"/> is supplied, the sweep exiles
/// every reachable player's graveyard in resolver order; without it only the
/// controller's graveyard is swept. The single-arg overload is suitable for
/// shape / dispatcher tests.
/// </para>
/// </summary>
[CardName("Sentinel Totem")]
public static class SentinelTotemFactory
{
    public const string CardName = "Sentinel Totem";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sentinel-totem");

    /// <summary>
    /// Construct Sentinel Totem. The activated sweep's "exile all graveyards"
    /// is scoped to the controller only (no allPlayersResolver). Identity and
    /// the ETB scry-1 trigger come from the embedded JSON definition.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, allPlayersResolver: null);

    /// <summary>
    /// Construct Sentinel Totem with optional cross-player graveyard access.
    /// When <paramref name="allPlayersResolver"/> is supplied, the activated
    /// ability's "exile all graveyards" sweeps every player's graveyard in
    /// resolver order. Without it, only the controller's graveyard is swept.
    /// </summary>
    public static Artifact Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + ETB scry-1 trigger come from JSON.
        var totem = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {T}, Exile this artifact: Exile all graveyards.
        //
        // CR 605 — not a mana ability (exile effect, goes on the stack).
        // Cost: {T} + self-exile. The self-exile zone move is performed by
        // the effect closure because the generic AdditionalCost pay path is
        // a no-op stub (mirrors Relic of Progenitus). No target, no draw.
        // ----------------------------------------------------------------
        var sweepEffect = new Effect(
            "Sentinel Totem: exile all graveyards",
            () =>
            {
                // Self-exile: move the Totem from Battlefield → Exile.
                // Idempotent if already exiled by the time this closure runs.
                if (totem.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(totem);
                    owner.Zones.Exile.AddCard(totem);
                    totem.SetZone(ZoneType.Exile);
                }

                // Exile all cards from all reachable graveyards.
                var players = allPlayersResolver?.Invoke()
                    ?? (IReadOnlyList<Player>)new[] { owner };

                foreach (var p in players)
                {
                    var graveyardCards = p.Zones.Graveyard.GetCards().ToList();
                    foreach (var card in graveyardCards)
                    {
                        p.Zones.Graveyard.RemoveCard(card);
                        p.Zones.Exile.AddCard(card);
                        card.SetZone(ZoneType.Exile);
                    }
                }
            });

        var sweepAbility = new ActivatedAbility(
            source: totem,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(totem),
                AdditionalCost.Sacrifice(totem), // models the self-exile cost; zone move in effect closure
            },
            effects: new IEffect[] { sweepEffect });

        totem.AddAbility(sweepAbility);

        return totem;
    }
}
