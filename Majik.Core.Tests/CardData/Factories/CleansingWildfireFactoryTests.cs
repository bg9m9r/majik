using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="CleansingWildfireFactory"/> — Zendikar Rising
/// ({1}{R}) Sorcery.
///
/// Oracle text (verified against Scryfall):
///   "Destroy target land. Its controller may search their library for a
///    basic land card, put it onto the battlefield tapped, then shuffle.
///    Draw a card."
///
/// Same destroy-target-land + optional basic-land compensation search shape
/// as <see cref="SunderingEruptionFactory"/>, minus the combat restriction,
/// plus an unconditional "Draw a card" for the caster (CR 608.2e — left-to-
/// right clause ordering).
///
/// Covers:
/// - Identity ({1}{R} mono-red Sorcery, mana value 2, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatches the printed name.
/// - Candidate gatherer: only land permanents (own + opponent) offered.
/// - Resolve destroys the target land (CR 701.7b).
/// - Non-land / off-battlefield target at resolution → no destroy (CR 608.2b).
/// - Compensation search: destroyed land's controller MAY tutor a basic land
///   onto the battlefield tapped + shuffle; declines / no-agent → no search.
/// - "Draw a card" always fires for the caster (even on illegal target).
/// </summary>
public class CleansingWildfireFactoryTests : IDisposable
{
    public CleansingWildfireFactoryTests() => AgentRegistry.Clear();

    public void Dispose() => AgentRegistry.Clear();

    private static Land BasicLand(string name, CardSubtype sub, Player p)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { sub })
        {
            Owner = p,
            Controller = p,
        };
        return land;
    }

    [Fact]
    public void CleansingWildfire_Identity_OneRSorcery_ManaValueTwo()
    {
        var alice = new Player("Alice", 20);
        var card = CleansingWildfireFactory.Create(alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Cleansing Wildfire");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(2,
            "{1}{R} — generic 1 + coloured 1 = MV 2 (CR 202.3)");
    }

    [Fact]
    public void CleansingWildfire_IsRed()
    {
        var alice = new Player("Alice", 20);
        var card = CleansingWildfireFactory.Create(alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColorEnum.Red, "Cleansing Wildfire has an {R} pip");
        colors.Should().NotContain(ManaColorEnum.Blue);
        colors.Should().NotContain(ManaColorEnum.Green);
        colors.Should().NotContain(ManaColorEnum.White);
        colors.Should().NotContain(ManaColorEnum.Black);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CleansingWildfire()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Cleansing Wildfire", alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Cleansing Wildfire");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void CleansingWildfire_CandidateGatherer_OnlyLandPermanents()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bobIsland = BasicLand("Island", CardSubtype.Island, bob);
        bobIsland.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobIsland);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bobBear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobBear);

        var aliceMountain = BasicLand("Mountain", CardSubtype.Mountain, alice);
        aliceMountain.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(aliceMountain);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var def = CleansingWildfireFactory.BuildDefinition(alice, o => o);
        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: Majik.Core.StateMachine.PhaseStateType.PreCombatMain,
            stack: stack);

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(bobIsland);
        candidates.Should().Contain(aliceMountain, "no 'opponent' restriction — own lands are legal");
        candidates.Should().NotContain(bobBear, "creatures are not lands");
    }

    [Fact]
    public void CleansingWildfire_Resolve_DestroysTargetLand_AndCasterDraws()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var island = BasicLand("Island", CardSubtype.Island, bob);
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        // Alice's library has a card to draw.
        var topCard = new Sorcery("Roast", "{1}{R}") { Owner = alice, Controller = alice };
        alice.Zones.Library.AddCard(topCard);

        var def = CleansingWildfireFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        island.Zone.Should().Be(ZoneType.Graveyard, "CR 701.7b — destroyed land → owner's graveyard");
        bob.Zones.Graveyard.GetCards().Should().Contain(island);

        topCard.Zone.Should().Be(ZoneType.Hand, "Cleansing Wildfire draws a card for its caster");
        alice.Zones.Hand.GetCards().Should().Contain(topCard);
    }

    [Fact]
    public void CleansingWildfire_Resolve_NonLandTarget_NoDestroy_ButStillDraws()
    {
        // CR 608.2b — illegal target → no destroy. The "Draw a card" clause
        // still resolves (it is not gated on the target being legal).
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bear);

        var topCard = new Sorcery("Roast", "{1}{R}") { Owner = alice, Controller = alice };
        alice.Zones.Library.AddCard(topCard);

        var def = CleansingWildfireFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { bear } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield, "non-land target is illegal (CR 608.2b)");
        topCard.Zone.Should().Be(ZoneType.Hand, "the draw clause still resolves");
    }

    [Fact]
    public void CleansingWildfire_Resolve_CompensationSearch_AgentAccepts_BasicGoesToBattlefieldTapped()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(bob, agent);

        var island = BasicLand("Island", CardSubtype.Island, bob);
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var mountain = BasicLand("Mountain", CardSubtype.Mountain, bob);
        bob.Zones.Library.AddCard(mountain);

        var aliceCard = new Sorcery("Roast", "{1}{R}") { Owner = alice, Controller = alice };
        alice.Zones.Library.AddCard(aliceCard);

        var def = CleansingWildfireFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        island.Zone.Should().Be(ZoneType.Graveyard);
        mountain.Zone.Should().Be(ZoneType.Battlefield, "compensation search puts the basic onto the battlefield");
        bob.Zones.Battlefield.GetCards().Should().Contain(mountain);
        mountain.IsTapped.Should().BeTrue("the basic land enters tapped per oracle text");
        aliceCard.Zone.Should().Be(ZoneType.Hand, "caster still draws");
    }

    [Fact]
    public void CleansingWildfire_Resolve_CompensationSearch_AgentDeclines_NoSearch()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(bob, agent);

        var island = BasicLand("Island", CardSubtype.Island, bob);
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var mountain = BasicLand("Mountain", CardSubtype.Mountain, bob);
        bob.Zones.Library.AddCard(mountain);

        var def = CleansingWildfireFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        island.Zone.Should().Be(ZoneType.Graveyard);
        mountain.Zone.Should().NotBe(ZoneType.Battlefield, "declined search → no land put onto the battlefield");
        bob.Zones.Library.GetCards().Should().Contain(mountain);
    }

    [Fact]
    public void CleansingWildfire_Resolve_CompensationSearch_NoAgent_NoSearch()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var island = BasicLand("Island", CardSubtype.Island, bob);
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var mountain = BasicLand("Mountain", CardSubtype.Mountain, bob);
        bob.Zones.Library.AddCard(mountain);

        var def = CleansingWildfireFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        island.Zone.Should().Be(ZoneType.Graveyard);
        mountain.Zone.Should().NotBe(ZoneType.Battlefield, "no agent → default decline → no search");
    }
}
