using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fountainport (Bloomburrow).
///
/// Land. Oracle text (Scryfall-confirmed):
///   "{T}: Add {C}.
///    {2}, {T}, Sacrifice a token: Draw a card.
///    {3}, {T}, Pay 1 life: Create a 1/1 blue Fish creature token.
///    {4}, {T}: Create a Treasure token."
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtype, no supertype, no mana cost).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{2}, {T}, Sacrifice a token: Draw a card.</b> —
///   <see cref="ActivatedAbility"/> with cost stack
///   <c>[Mana({2}), Tap(self), SacrificeAToken]</c>. The token-sacrifice
///   activation cost routes through the shared
///   <see cref="Costs.SacrificeAToken"/> rail (CR 111.8 / 701.16 →
///   <see cref="SacrificeFilteredCost"/>). Resolution draws a single card
///   through <see cref="Fx.DrawCards"/> so any
///   <c>DrawCardIntent</c> replacements participate (CR 120.6).
/// - <b>{3}, {T}, Pay 1 life: Create a 1/1 blue Fish creature token.</b> —
///   <see cref="ActivatedAbility"/> with cost stack
///   <c>[Mana({3}), Tap(self), PayLifeCost(1)]</c> (CR 119.4 — paying life).
///   Resolution mints a 1/1 blue Fish via
///   <see cref="TokenFactory.CreateOnBattlefield"/> (CR 111 / 111.4 — blue
///   colour stamped explicitly).
/// - <b>{4}, {T}: Create a Treasure token.</b> —
///   <see cref="ActivatedAbility"/> with cost stack <c>[Mana({4}), Tap(self)]</c>.
///   Resolution mints a Treasure via <see cref="TokenFactory.CreateTreasure"/>
///   (CR 111.10 — colourless artifact token with its sacrifice-for-mana ability).
///
/// ## Token routing
/// When a <see cref="ZoneService"/> is supplied each minted token routes
/// through it so <see cref="CardMovedEvent"/> fires (downstream ETB listeners
/// — Soul Warden, token-doublers' observers, etc.). The single-arg dispatcher
/// path omits the service (shape-only); tokens still land on the battlefield
/// directly (CR 111.6).
///
/// ## Production / test parity
/// Built inline (the same posture as the structural twin
/// <see cref="SeaGateWreckageFactory"/>, another colourless utility land with a
/// vanilla mana ability + extra activated abilities) because the JSON-def rail
/// does not yet express token-creation effects. The dispatcher / test path
/// (<see cref="NamedCardFactory.Create"/>) routes here. Adding this factory
/// flips <c>IsImplemented</c> automatically via the
/// <see cref="ImplementedCardNames"/> registry.
/// </summary>
[CardName("Fountainport")]
public static class FountainportFactory
{
    public const string CardName = "Fountainport";

    /// <summary>
    /// Construct Fountainport with no live <see cref="ZoneService"/> wiring.
    /// All four abilities are attached for shape inspection; spawned tokens
    /// land on the battlefield directly. Suitable for dispatcher / shape tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, zoneService: null);

    /// <summary>
    /// Construct Fountainport.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Zone service used so spawned tokens publish
    /// <see cref="CardMovedEvent"/> on battlefield entry. May be null.</param>
    public static Land Create(Player owner, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Plain Land — no subtype, no supertype, no mana cost.
        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C} — vanilla mana ability (CR 605.1).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // ----------------------------------------------------------------
        // {2}, {T}, Sacrifice a token: Draw a card. (CR 602 / 119.x)
        //
        // "Sacrifice a token" routes through the shared Costs.SacrificeAToken
        // rail (CR 111.8 / 701.16). Resolution draws one card through
        // Fx.DrawCards so DrawCardIntent replacements participate.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card",
            () =>
            {
                var controller = land.Controller ?? owner;
                Fx.DrawCards(controller, 1);
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                Primitives.Costs.Mana("{2}"),
                Primitives.Costs.TapSelf(land),
                Primitives.Costs.SacrificeAToken(),
            },
            effects: new IEffect[] { drawEffect }));

        // ----------------------------------------------------------------
        // {3}, {T}, Pay 1 life: Create a 1/1 blue Fish creature token.
        // (CR 602 / CR 119.4 — paying life as a cost.)
        // ----------------------------------------------------------------
        var fishEffect = new Effect(
            $"{CardName}: create a 1/1 blue Fish creature token",
            () => CreateFishToken(land.Controller ?? owner, zoneService));

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                Primitives.Costs.Mana("{3}"),
                Primitives.Costs.TapSelf(land),
                new PayLifeCost(1),
            },
            effects: new IEffect[] { fishEffect }));

        // ----------------------------------------------------------------
        // {4}, {T}: Create a Treasure token. (CR 602 / CR 111.10.)
        // ----------------------------------------------------------------
        var treasureEffect = new Effect(
            $"{CardName}: create a Treasure token",
            () => TokenFactory.CreateTreasure(land.Controller ?? owner, zoneService));

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                Primitives.Costs.Mana("{4}"),
                Primitives.Costs.TapSelf(land),
            },
            effects: new IEffect[] { treasureEffect }));

        return land;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — mint a 1/1 blue Fish creature token under
    /// <paramref name="controller"/>'s control. Blue is stamped explicitly via
    /// <see cref="TokenFactory.TokenSpec.Colors"/>.
    /// </summary>
    private static Creature CreateFishToken(Player controller, ZoneService? zones)
    {
        var spec = new TokenFactory.TokenSpec(
            Name: "Fish",
            Power: 1,
            Toughness: 1,
            Subtypes: new[] { CardSubtype.Fish },
            Colors: new[] { ManaColor.Blue });

        return TokenFactory.CreateOnBattlefield(spec, controller, zones);
    }
}
