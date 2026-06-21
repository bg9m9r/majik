using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scavenger Grounds (Hour of Devastation, Land — Desert).
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {2}, {T}, Sacrifice a Desert: Exile all graveyards."
///
/// The base shape (name, Land, Desert subtype, {T}: Add {C} mana ability) is
/// materialised from the embedded JSON definition (<c>scavenger-grounds.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — the same JSON-backed posture as
/// <see cref="RamunapRuinsFactory"/> / <see cref="SentinelTotemFactory"/>.
///
/// ## Implemented (v1)
/// - <b>Land — Desert</b> + <b>{T}: Add {C}</b> (from JSON; CR 605.1 — a mana
///   ability, no stack). {C} has no dedicated colourless bucket today, so it
///   is stored as +1 generic (same modelling as every <c>produces: C</c> land).
/// - <b>{2}, {T}, Sacrifice a Desert: Exile all graveyards</b> — an
///   <see cref="ActivatedAbility"/> (CR 605 — not a mana ability, goes on the
///   stack). Cost = {2} (<see cref="ManaCostCost"/>) + {T}
///   (<see cref="AdditionalCost.Tap"/>) + the real <b>"Sacrifice a Desert"</b>
///   filtered cost (<see cref="SacrificeFilteredCost"/> via
///   <see cref="Primitives.Costs.SacrificeASubtype"/>, CR 701.16). Unlike the older
///   Ramunap Ruins shape (which sacrificed itself via a no-op stub), the
///   sacrifice cost here is a genuine battlefield → graveyard move over ANY
///   Desert the controller controls — Scavenger Grounds itself qualifies
///   (CR 701.16) and is the deterministic v1 pick when it is the only Desert.
///   On resolution the effect exiles all cards from every reachable
///   graveyard (CR 406.2 — the cards go to their owners' exile zones).
///
/// ## All-graveyards sweep scoping
/// The sweep exiles every player's graveyard read off the live resolution
/// context (<c>ctx.Game.AllPlayers</c>) at resolution; when no live game is
/// wired (shape-only / legacy sync path) only the controller's graveyard is
/// swept (#2551 land cleanup — no captured resolver, so correct on the routed
/// prod build).
///
/// ## Deferred (v1 gaps)
/// - <b>"Sacrifice a Desert" agent prompt</b>: the filtered sacrifice cost
///   deterministically picks the first eligible Desert when the agent has not
///   pre-set a target — the same prompting MVP every sibling sacrifice-picker
///   cost waits on (<see cref="SacrificeFilteredCost"/>).
/// </summary>
[CardName("Scavenger Grounds")]
public static class ScavengerGroundsFactory
{
    public const string CardName = "Scavenger Grounds";
    public const string Slug = "scavenger-grounds";

    /// <summary>
    /// Construct Scavenger Grounds. The sacrifice ability's "exile all
    /// graveyards" sweep reads every player off the LIVE resolution context
    /// (<c>ctx.Game.AllPlayers</c>) at resolution; when no live game is wired
    /// (shape-only / legacy sync path) only the controller's graveyard is swept.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition: nonbasic Land with the
        // Desert subtype + the {T}: Add {C} mana ability (CR 605.1).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {2}, {T}, Sacrifice a Desert: Exile all graveyards.
        // CR 602 — activated ability (non-mana). Mana cost {2} + {T} + the
        // real "Sacrifice a Desert" filtered cost (CR 701.16). On resolution,
        // exile all cards from every reachable graveyard.
        // ----------------------------------------------------------------
        var sweepEffect = new Effect(
            $"{CardName}: exile all graveyards",
            ctx =>
            {
                // "Exile all graveyards" — read every player from the LIVE game
                // at resolution (ctx.Game.AllPlayers). No captured resolver, so
                // correct on the routed prod build (#2551 land cleanup). When no
                // live game is wired only the controller's graveyard is swept.
                var players = ctx.Game?.AllPlayers
                    ?? (IReadOnlyList<Player>)new[] { land.Controller ?? owner };

                foreach (var p in players)
                {
                    if (p == null) continue;
                    var graveyardCards = p.Zones.Graveyard.GetCards().ToList();
                    foreach (var card in graveyardCards)
                    {
                        p.Zones.Graveyard.RemoveCard(card);
                        p.Zones.Exile.AddCard(card);
                        card.SetZone(ZoneType.Exile);
                    }
                }

                return ValueTask.CompletedTask;
            });

        var sweepAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Tap(land),
                Primitives.Costs.SacrificeASubtype(CardSubtype.Desert),
            },
            effects: new IEffect[] { sweepEffect });

        land.AddAbility(sweepAbility);

        return land;
    }
}
