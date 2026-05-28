using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for <see cref="FellTheProfaneFactory"/> and
/// <see cref="FellMireFactory"/> — the front + back faces of the modal
/// double-faced card Fell the Profane // Fell Mire.
///
/// Front face (Fell the Profane, {2}{B}{B}):
///   Instant. "Destroy target creature or planeswalker."
///
/// Back face (Fell Mire):
///   Land. "As this land enters, you may pay 3 life. If you don't, it
///   enters tapped." "{T}: Add {B}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, mana value, owner).
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face-tracker attachment (front starts on front; back pre-flipped).
/// - Front face — candidate gatherer: creatures + planeswalkers included;
///   non-creature/non-planeswalker permanents NOT offered.
/// - Front face — resolve destroys target creature (CR 701.7).
/// - Front face — resolve destroys target planeswalker (CR 701.7).
/// - Front face — non-creature/non-planeswalker target at resolution → no-op (CR 608.2b).
/// - Front face — target leaves battlefield before resolution → no-op (CR 608.2b).
/// - Back face — {T}: Add {B} mana ability attached.
/// - Back face — pay 3 life → enters untapped.
/// - Back face — decline → enters tapped.
/// - Back face — can't pay (life &lt; 3) → enters tapped (CR 119.4).
/// - Back face — exactly 3 life → payment legal, enters untapped.
/// - Back face — no agent → enters tapped.
/// </summary>
public class FellTheProfaneFactoryTests : IDisposable
{
    public FellTheProfaneFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void FellTheProfane_Identity_TwoBBBlackInstant_ManaValueFour()
    {
        var alice = new Player("Alice", 20);
        var card = FellTheProfaneFactory.Create(alice);

        card.Name.Should().Be("Fell the Profane");
        card.ManaCost.Should().Be("{2}{B}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(4,
            "Fell the Profane costs {2}{B}{B} — generic 2 + coloured 2 = MV 4 (CR 202.3)");
    }

    [Fact]
    public void FellTheProfane_IsBlack()
    {
        var alice = new Player("Alice", 20);
        var card = FellTheProfaneFactory.Create(alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColorEnum.Black,
            "Fell the Profane has {B}{B} pips — it is mono-black");
        colors.Should().NotContain(ManaColorEnum.Blue);
        colors.Should().NotContain(ManaColorEnum.Red);
        colors.Should().NotContain(ManaColorEnum.Green);
        colors.Should().NotContain(ManaColorEnum.White);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FellTheProfane()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Fell the Profane", alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Fell the Profane");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // =========================================================================
    // MDFC face tracker — front face
    // =========================================================================

    [Fact]
    public void FellTheProfane_CarriesMdfcState_FrontFace()
    {
        var alice = new Player("Alice", 20);
        var card = FellTheProfaneFactory.Create(alice);

        card.MdfcState.Should().NotBeNull(
            "Fell the Profane is the front face of an MDFC and must carry a face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Fell the Profane");
        card.MdfcState!.BackFaceName.Should().Be("Fell Mire");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Fell the Profane");
    }

    // =========================================================================
    // Front face — candidate gatherer
    // =========================================================================

    [Fact]
    public void FellTheProfane_CandidateGatherer_IncludesCreaturesAndPlaneswalkers()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bobBear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobBear);

        var bobPW = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", 3) { Owner = bob, Controller = bob };
        bobPW.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobPW);

        var bobIsland = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob, Controller = bob
        };
        bobIsland.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobIsland);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var def = FellTheProfaneFactory.BuildDefinition(o => o);
        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: Majik.Core.StateMachine.PhaseStateType.PreCombatMain,
            stack: stack);

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(bobBear, "creatures are legal targets");
        candidates.Should().Contain(bobPW, "planeswalkers are legal targets");
        candidates.Should().NotContain(bobIsland,
            "lands are not creatures or planeswalkers — not a legal target");
    }

    [Fact]
    public void FellTheProfane_CandidateGatherer_IncludesOwnCreaturesToo()
    {
        // Oracle text is "target creature or planeswalker" — no "opponent" restriction.
        var alice = new Player("Alice", 20);

        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = alice, Controller = alice };
        aliceBear.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(aliceBear);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var def = FellTheProfaneFactory.BuildDefinition(o => o);
        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: Majik.Core.StateMachine.PhaseStateType.PreCombatMain,
            stack: stack);

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(aliceBear,
            "own creatures are legal targets — no 'opponent controls' restriction");
    }

    // =========================================================================
    // Front face — resolve destroys target creature
    // =========================================================================

    [Fact]
    public void FellTheProfane_Resolve_DestroysTargetCreature()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bear);

        var def = FellTheProfaneFactory.BuildDefinition(o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { bear } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.7 — destroyed permanent moves to graveyard");
        bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
        bob.Zones.Graveyard.GetCards().Should().Contain(bear);
    }

    // =========================================================================
    // Front face — resolve destroys target planeswalker
    // =========================================================================

    [Fact]
    public void FellTheProfane_Resolve_DestroysTargetPlaneswalker()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var liliana = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", 3) { Owner = bob, Controller = bob };
        liliana.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(liliana);

        var def = FellTheProfaneFactory.BuildDefinition(o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { liliana } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        liliana.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.7 — planeswalkers are valid targets; destroyed → graveyard");
        bob.Zones.Battlefield.GetCards().Should().NotContain(liliana);
        bob.Zones.Graveyard.GetCards().Should().Contain(liliana);
    }

    // =========================================================================
    // Front face — illegal target at resolution → no-op (CR 608.2b)
    // =========================================================================

    [Fact]
    public void FellTheProfane_Resolve_TargetLeftBattlefield_NoOp()
    {
        // CR 608.2b — target no longer on the battlefield at resolution.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        // NOT placed on battlefield — already gone.
        bear.SetZone(ZoneType.Graveyard);
        bob.Zones.Graveyard.AddCard(bear);

        var def = FellTheProfaneFactory.BuildDefinition(o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { bear } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "target was already in graveyard — effect is a no-op (CR 608.2b)");
    }

    [Fact]
    public void FellTheProfane_Resolve_NonCreatureNonPlaneswalkerTarget_NoOp()
    {
        // CR 608.2b — if the resolved object is not a creature or planeswalker
        // at resolution time the effect does nothing.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var island = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob, Controller = bob
        };
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var def = FellTheProfaneFactory.BuildDefinition(o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        island.Zone.Should().Be(ZoneType.Battlefield,
            "land is not a creature or planeswalker — effect is a no-op (CR 608.2b)");
        bob.Zones.Battlefield.GetCards().Should().Contain(island);
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void FellMire_Identity()
    {
        var alice = new Player("Alice", 20);
        var land = FellMireFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Fell Mire");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Fell Mire is a non-Basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FellMire()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Fell Mire", alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Fell Mire");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void FellMire_CarriesMdfcState_PreFlippedToBackFace()
    {
        var alice = new Player("Alice", 20);
        var land = FellMireFactory.Create(alice);

        land.MdfcState.Should().NotBeNull(
            "Fell Mire is the back face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Fell the Profane");
        land.MdfcState!.BackFaceName.Should().Be("Fell Mire");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Fell Mire");
    }

    // =========================================================================
    // Back face — {T}: Add {B}
    // =========================================================================

    [Fact]
    public void FellMire_HasSingleManaAbility_AddingBlack()
    {
        var alice = new Player("Alice", 20);
        var land = FellMireFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {B} ability");

        var expected = ManaCost.Parse("B");
        manaAbilities[0].ManaGenerated.Black.Should().Be(expected.Black);
        manaAbilities[0].ManaGenerated.Black.Should().BeGreaterThan(0,
            "Fell Mire produces black mana");
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0,
            "Fell Mire does not produce blue mana");
    }

    [Fact]
    public void FellMire_HasNoActivatedOrTriggeredAbilities_BeyondMana()
    {
        var alice = new Player("Alice", 20);
        var land = FellMireFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Fell Mire has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement, not a triggered ability (CR 614.1c)");
    }

    // =========================================================================
    // Back face — ETB pay-3-life replacement
    // =========================================================================

    [Fact]
    public void FellMire_EntersUntapped_WhenAgentPaysThreeLife()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = FellMireFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Fell Mire enters untapped when the controller pays 3 life");
        alice.LifeTotal.Should().Be(17, "paying 3 life drops Alice from 20 → 17");
    }

    [Fact]
    public void FellMire_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(alice, agent);

        var land = FellMireFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Fell Mire enters tapped when the controller declines to pay 3 life");
        alice.LifeTotal.Should().Be(20, "declining keeps Alice's life unchanged");
    }

    [Fact]
    public void FellMire_EntersTapped_WhenControllerCannotPayThreeLife()
    {
        // CR 119.4 — you can't pay life you don't have. Below 3 life the
        // agent is never prompted; land enters tapped.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.LoseLife(18); // life = 2
        var agent = new ScriptedAgent();
        // No QueueYesNo — if the predicate (incorrectly) prompted, the
        // ScriptedAgent would throw and the test would fail.
        AgentRegistry.Set(alice, agent);

        var land = FellMireFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            $"Fell Mire enters tapped when controller can't pay 3 life (life={alice.LifeTotal})");
        alice.LifeTotal.Should().Be(2, "life unchanged — no payment took place");
    }

    [Fact]
    public void FellMire_EntersUntapped_AtExactlyThreeLife()
    {
        // CR 119.4 carve-out — payments may bring you to 0. At exactly 3
        // life paying is legal: drop to 0; SBAs handle loss-of-game.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.LoseLife(17); // life = 3
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = FellMireFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "at exactly 3 life the payment is legal — enters untapped");
        alice.LifeTotal.Should().Be(0,
            "paying 3 life from 3 drops to 0; SBAs run later");
    }

    [Fact]
    public void FellMire_EntersTapped_WhenNoAgentRegistered()
    {
        // No AgentRegistry.Set — the predicate's no-agent branch should
        // default to declining.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        var land = FellMireFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no agent registered → default decline → enters tapped");
        alice.LifeTotal.Should().Be(20);
    }
}
