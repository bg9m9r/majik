using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WickedWolfFactory"/> (Throne of Eldraine,
/// {2}{G}{G}).
///
/// Creature — Wolf 3/3. Oracle text (Scryfall, verified 2026-06-14):
///   "When this creature enters, it fights up to one target creature you
///    don't control.
///    Sacrifice a Food: Put a +1/+1 counter on this creature. It gains
///    indestructible until end of turn. Tap it."
///
/// Covers ONLY the card's unique behaviour (plus a single identity assert):
///   - Identity (Wolf 3/3 at {2}{G}{G}).
///   - ETB fight trigger fights an opponent's creature (CR 701.12) — read
///     from the live resolution context.
///   - "up to one" → clean no-op when no opposing creature exists.
///   - Food-sacrifice ability: +1/+1 counter, indestructible until EOT, tap.
///
/// (NamedCardFactory dispatch + well-formedness are covered for every
/// implemented card by CardFactoryContractTests — not re-asserted here.)
/// </summary>
[Trait("Color", "G")]
public class WickedWolfFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static GameContext Ctx(Player self, params Player[] all) =>
        new(
            self: self,
            allPlayers: all,
            activePlayer: self,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack());

    private static ResolutionContext Rc(GameContext game, Player controller) =>
        ResolutionContext.For(controller, agent: null, game: game, chosenTargets: null);

    [Fact]
    public void WickedWolf_Identity()
    {
        var c = WickedWolfFactory.Create(_alice);

        c.Name.Should().Be("Wicked Wolf");
        c.ManaCost.Should().Be("{2}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wolf).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public async Task EtbTrigger_FightsAnOpponentsCreature()
    {
        var wolf = WickedWolfFactory.Create(_alice);
        SeatOnBattlefield(wolf, _alice);

        // Bob controls a 2/2 — Wicked Wolf (3/3) fights it: each deals power
        // to the other (CR 701.12a). The 2/2 takes 3 (lethal-marked), the
        // wolf takes 2.
        var foe = new Creature("Bear", "{1}{G}", 2, 2);
        SeatOnBattlefield(foe, _bob);

        var trigger = wolf.Abilities.OfType<TriggeredAbility>().Single();
        var rc = Rc(Ctx(_alice, _alice, _bob), _alice);
        foreach (var e in trigger.Effects) await e.ExecuteAsync(rc);

        // CR 701.12a — simultaneous: foe took the wolf's 3 power, wolf took
        // the foe's 2 power.
        foe.Damage.Should().Be(3, "Wicked Wolf's 3 power was dealt to the foe");
        wolf.Damage.Should().Be(2, "the foe's 2 power was dealt back to the wolf");
    }

    [Fact]
    public async Task EtbTrigger_UpToOne_NoOpponentCreature_IsCleanNoOp()
    {
        var wolf = WickedWolfFactory.Create(_alice);
        SeatOnBattlefield(wolf, _alice);
        // Bob controls nothing — "up to one" means the fight simply fizzles.

        var trigger = wolf.Abilities.OfType<TriggeredAbility>().Single();
        var rc = Rc(Ctx(_alice, _alice, _bob), _alice);

        var act = async () =>
        {
            foreach (var e in trigger.Effects) await e.ExecuteAsync(rc);
        };

        await act.Should().NotThrowAsync("'up to one' with no eligible foe is a no-op");
        wolf.Damage.Should().Be(0, "no fight occurred");
    }

    [Fact]
    public void FoodSacrifice_AddsCounter_GrantsIndestructibleEot_AndTaps()
    {
        var wolf = WickedWolfFactory.Create(_alice);
        wolf.ActiveEffects = new ContinuousEffectsService();
        SeatOnBattlefield(wolf, _alice);

        // A Food token to pay the sacrifice cost.
        var food = Majik.Core.Tokens.TokenFactory.CreateFood(_alice);

        var ability = wolf.Abilities.OfType<ActivatedAbility>().Single();
        var sacCost = ability.Costs
            .OfType<UnderworldCookbookFactory.SacrificeAFoodCost>().Single();
        sacCost.CanPay(_alice).Should().BeTrue("a Food is available to sacrifice");
        sacCost.Pay(_alice);

        foreach (var e in ability.Effects) e.Execute();

        // CR 122.1 — a +1/+1 counter.
        wolf.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        // CR 702.12 — indestructible until end of turn.
        wolf.ActiveEffects!.Compute(wolf).Keywords.Should().Contain("Indestructible");

        // CR 701.21a — "Tap it."
        wolf.IsTapped.Should().BeTrue();

        // Food consumed (CR 701.16 — sacrifice).
        _alice.Zones.Graveyard.GetCards().Should().Contain(food);

        // CR 514.2 — the indestructible grant expires at cleanup.
        wolf.ActiveEffects.ExpireEndOfTurn();
        wolf.ActiveEffects.Compute(wolf).Keywords.Should().NotContain("Indestructible");
    }

    private void SeatOnBattlefield(Creature card, Player controller)
    {
        card.SetController(controller);
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}
