using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// CR 614.1c / 614.10 — Temple of the Dragon Queen's "enters tapped unless you
/// revealed a Dragon card this way or you control a Dragon" clause, bound by the
/// production <see cref="ConditionalEntersTappedBinder"/>. This is the binder-
/// chain path that was previously deferred: the reveal-a-Dragon half now reaches
/// the controller's <see cref="IPlayerAgent"/> through
/// <see cref="RevealCardFromHandReplacement"/>, so the combined conditional-
/// tapped is live in prod (the land no longer always enters untapped).
/// </summary>
public class TempleOfTheDragonQueenBinderTests : IDisposable
{
    private const string TempleOracle =
        "As this land enters, you may reveal a Dragon card from your hand. This " +
        "land enters tapped unless you revealed a Dragon card this way or you " +
        "control a Dragon. As this land enters, choose a color. {T}: Add one " +
        "mana of the chosen color.";

    public TempleOfTheDragonQueenBinderTests() => AgentRegistry.Clear();
    public void Dispose() => AgentRegistry.Clear();

    private static CardEntity Entity() => new()
    {
        Name = "Temple of the Dragon Queen",
        OracleText = TempleOracle,
        TypeLine = "Land",
    };

    private static Land MakeTemple(Player owner)
    {
        var land = new Land("Temple of the Dragon Queen");
        land.SetOwner(owner);
        land.SetController(owner);
        owner.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);
        return land;
    }

    private static void SeedDragonInHand(Player owner)
    {
        var dragon = new Creature("Shivan Dragon", "{4}{R}{R}", 5, 5,
            subtypes: new[] { CardSubtype.Dragon });
        dragon.SetOwner(owner);
        owner.Zones.Hand.AddCard(dragon);
        dragon.SetZone(ZoneType.Hand);
    }

    private static void SeedDragonOnBattlefield(Player owner)
    {
        var dragon = new Creature("Shivan Dragon", "{4}{R}{R}", 5, 5,
            subtypes: new[] { CardSubtype.Dragon });
        dragon.SetOwner(owner);
        owner.Zones.Battlefield.AddCard(dragon);
        dragon.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void Binder_ClaimsTheTempleClause()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = MakeTemple(alice);

        ConditionalEntersTappedBinder.Bind(land, Entity(), bus)
            .Should().BeTrue("the reveal-or-control variant is claimed");
    }

    [Fact]
    public async Task EntersTapped_WhenNoDragonRevealed_AndNoDragonControlled()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = MakeTemple(alice);
        SeedDragonInHand(alice); // a Dragon is available to reveal …
        ConditionalEntersTappedBinder.Bind(land, Entity(), bus).Should().BeTrue();

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false); // … but the controller declines to reveal it
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        var after = await bus.ApplyAsync(
            new ZoneMoveIntent(land, ZoneType.Hand, ZoneType.Battlefield, Controller: alice), ctx);

        after!.EntersTapped.Should().BeTrue(
            "no Dragon revealed and no Dragon controlled → enters tapped");
    }

    [Fact]
    public async Task EntersUntapped_WhenDragonRevealedFromHand()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = MakeTemple(alice);
        SeedDragonInHand(alice);
        ConditionalEntersTappedBinder.Bind(land, Entity(), bus).Should().BeTrue();

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true); // reveal the Dragon "this way"
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        var after = await bus.ApplyAsync(
            new ZoneMoveIntent(land, ZoneType.Hand, ZoneType.Battlefield, Controller: alice), ctx);

        after!.EntersTapped.Should().BeFalse(
            "revealing a Dragon card this way lets it enter untapped");
    }

    [Fact]
    public async Task EntersUntapped_WhenControllerControlsADragon_EvenWithoutRevealing()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = MakeTemple(alice);
        SeedDragonOnBattlefield(alice);
        ConditionalEntersTappedBinder.Bind(land, Entity(), bus).Should().BeTrue();

        var agent = new ScriptedAgent();
        // No Dragon in hand to reveal; control-half should carry it.
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        var after = await bus.ApplyAsync(
            new ZoneMoveIntent(land, ZoneType.Hand, ZoneType.Battlefield, Controller: alice), ctx);

        after!.EntersTapped.Should().BeFalse(
            "controlling a Dragon satisfies the second half of the clause");
    }

    [Fact]
    public async Task EntersTapped_WhenOnlyOpponentControlsADragon()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var land = MakeTemple(alice);
        SeedDragonOnBattlefield(bob); // opponent's Dragon doesn't count
        ConditionalEntersTappedBinder.Bind(land, Entity(), bus).Should().BeTrue();

        var agent = new ScriptedAgent();
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        var after = await bus.ApplyAsync(
            new ZoneMoveIntent(land, ZoneType.Hand, ZoneType.Battlefield, Controller: alice), ctx);

        after!.EntersTapped.Should().BeTrue(
            "only the controller's Dragons count ('you control a Dragon')");
    }

    [Fact]
    public async Task EndToEnd_ZoneServiceMove_EntersTapped_WhenNothingSatisfiesClause()
    {
        // Full live path: ZoneService runs the bus on the ETB move and applies
        // EntersTapped to the permanent.
        var eventBus = new Majik.Core.Events.EventBus();
        var bus = new ReplacementBus();
        var zones = new Majik.Core.Services.ZoneService(eventBus, bus);
        var alice = new Player("Alice", 20);
        var land = MakeTemple(alice);
        ConditionalEntersTappedBinder.Bind(land, Entity(), bus).Should().BeTrue();

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeTrue(
            "no reveal, no Dragon controlled → Temple enters tapped");
    }
}
