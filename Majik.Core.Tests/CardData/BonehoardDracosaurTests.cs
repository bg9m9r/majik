using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="BonehoardDracosaurFactory"/> (The Lost Caverns of
/// Ixalan, {3}{R}{R}). Oracle (Scryfall-verified):
///   "Flying, first strike
///    At the beginning of your upkeep, exile the top two cards of your
///    library. You may play them this turn. If you exiled a land card this
///    way, create a 3/1 red Dinosaur creature token. If you exiled a nonland
///    card this way, create a Treasure token."
///
/// Covers the card's UNIQUE behaviour (identity + the upkeep trigger):
/// - Identity: 5/5 Creature — Dinosaur Dragon, {3}{R}{R}, Flying + First strike.
/// - Upkeep trigger exiles the top two and stamps a "play this turn"
///   (EndOfTurn) grant on each (CR 701.20 / 118.9 / 514.2).
/// - Land + nonland split → BOTH a 3/1 red Dinosaur token AND a Treasure.
/// - Two nonlands → exactly one Treasure, no Dino (a … token, not for each).
/// - Two lands → exactly one 3/1 red Dinosaur, no Treasure.
/// (Dispatch + well-formedness are covered by CardFactoryContractTests.)
/// </summary>
[Trait("Color", "R")]
public class BonehoardDracosaurTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public BonehoardDracosaurTests()
    {
        _zones = new ZoneService(_bus);
    }

    private Card AddNonlandToLibrary(string name, string cost = "{R}")
    {
        var c = new Card(name, cost);
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private Land AddLandToLibrary(string name = "Mountain")
    {
        var land = new Land(name);
        land.SetOwner(_alice);
        _alice.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);
        return land;
    }

    /// <summary>Build Bonehoard fully wired, on Alice's battlefield, and fire
    /// her upkeep so the trigger resolves off the stack.</summary>
    private Creature BuildOnBattlefieldAndFireUpkeep()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new Majik.Core.Abilities.TriggerManager(stack, _bus);

        var draco = BonehoardDracosaurFactory.Create(
            _alice, eventBus: _bus, triggers: triggers, zones: _zones, replacements: null);
        _alice.Zones.Battlefield.AddCard(draco);
        draco.SetZone(ZoneType.Battlefield);

        _bus.Publish(new StepStartedEvent(StepStateType.Upkeep, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();
        return draco;
    }

    private int TreasureCount() =>
        _alice.Zones.Battlefield.GetCards().Count(c => c.HasSubtype(CardSubtype.Treasure));

    // The token is named "Dinosaur"; filter by name so the Dracosaur itself
    // (also a Dinosaur subtype — it is a Dinosaur Dragon) is not counted.
    private List<Creature> DinoTokens() =>
        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Dinosaur").ToList();

    // -------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------

    [Fact]
    public void Bonehoard_Identity_DinosaurDragon_5_5_At3RR_FlyingFirstStrike()
    {
        var draco = BonehoardDracosaurFactory.Create(_alice);

        draco.Name.Should().Be("Bonehoard Dracosaur");
        draco.ManaCost.Should().Be("{3}{R}{R}");
        draco.HasType(CardType.Creature).Should().BeTrue();
        draco.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        draco.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        draco.BasePower.Should().Be(5);
        draco.BaseToughness.Should().Be(5);
        CombatAbilities.HasFlying(draco).Should().BeTrue();
        CombatAbilities.HasFirstStrike(draco).Should().BeTrue();
    }

    // -------------------------------------------------------------------
    // Upkeep trigger — exile + play-this-turn grant
    // -------------------------------------------------------------------

    [Fact]
    public void Upkeep_ExilesTopTwo_AndGrantsPlayThisTurn()
    {
        var top1 = AddNonlandToLibrary("Top1", "{R}");
        var top2 = AddNonlandToLibrary("Top2", "{1}{R}");
        var top3 = AddNonlandToLibrary("Top3", "{2}{R}");

        BuildOnBattlefieldAndFireUpkeep();

        _alice.Zones.Exile.GetCards().Should().Contain(new[] { top1, top2 });
        _alice.Zones.Exile.GetCards().Should().NotContain(top3);
        _alice.Zones.Library.GetCards().Should().Contain(top3);

        // CR 118.9 — each exiled card carries a play-this-turn grant for Alice.
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Upkeep_PlayGrant_ClearsAtControllersCleanup_ThisTurn()
    {
        var top1 = AddNonlandToLibrary("Top1", "{R}");
        AddNonlandToLibrary("Top2", "{R}");

        BuildOnBattlefieldAndFireUpkeep();
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);

        // CR 514.2 — "this turn" = the FIRST Cleanup the controller owns.
        _bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _alice));
        top1.RuntimeExileCastAllowedCaster.Should().BeNull(
            "'you may play them this turn' expires at the controller's cleanup");
    }

    // -------------------------------------------------------------------
    // Payoff clauses — land → 3/1 red Dino, nonland → Treasure (independent)
    // -------------------------------------------------------------------

    [Fact]
    public void Upkeep_LandPlusNonland_CreatesBothDinoAndTreasure()
    {
        // Library top order: top1 (land), top2 (nonland) exiled.
        AddLandToLibrary("Mountain");
        AddNonlandToLibrary("Lightning Bolt", "{R}");

        BuildOnBattlefieldAndFireUpkeep();

        var dinos = DinoTokens();
        dinos.Should().HaveCount(1, "exactly one 3/1 red Dinosaur for the land clause");
        var dino = dinos[0];
        dino.BasePower.Should().Be(3);
        dino.BaseToughness.Should().Be(1);
        Majik.Core.Cards.CardColors.GetColors(dino).Should().Contain(Majik.Core.ValueObjects.ManaColor.Red);

        TreasureCount().Should().Be(1, "exactly one Treasure for the nonland clause");
    }

    [Fact]
    public void Upkeep_TwoNonlands_CreatesOneTreasure_NoDino()
    {
        AddNonlandToLibrary("Top1", "{R}");
        AddNonlandToLibrary("Top2", "{1}{R}");

        BuildOnBattlefieldAndFireUpkeep();

        TreasureCount().Should().Be(1, "'a Treasure token' is a single token, not one per nonland");
        DinoTokens().Should().BeEmpty("no land exiled → no Dinosaur");
    }

    [Fact]
    public void Upkeep_TwoLands_CreatesOneDino_NoTreasure()
    {
        AddLandToLibrary("Mountain");
        AddLandToLibrary("Forest");

        BuildOnBattlefieldAndFireUpkeep();

        DinoTokens().Should().HaveCount(1, "'a 3/1 red Dinosaur' is a single token, not one per land");
        TreasureCount().Should().Be(0, "no nonland exiled → no Treasure");
    }
}
