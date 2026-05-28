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
/// Tests for <see cref="SinkIntoStuporFactory"/> and
/// <see cref="SoporificSpringsFactory"/> — the front + back faces of the
/// Bloomburrow modal double-faced card Sink into Stupor // Soporific
/// Springs.
///
/// Front face (Sink into Stupor, {1}{U}{U}):
///   Instant. "Return target spell or nonland permanent an opponent
///   controls to its owner's hand."
///
/// Back face (Soporific Springs):
///   Land. "As this land enters, you may pay 3 life. If you don't, it
///   enters tapped." "{T}: Add {U}."
///
/// Covers:
/// - Identity for both faces.
/// - <see cref="NamedCardFactory"/> dispatches both printed names to their
///   respective faces.
/// - MDFC face-tracker attachment (front-face card carries front-name +
///   back-name; back-face card carries the same pair pre-flipped).
/// - Front face — candidate gatherer: opponent spells on stack +
///   opponent-controlled nonland permanents; own-side + lands excluded.
/// - Front face — resolve: spell target → owner's hand off the stack;
///   permanent target → owner's hand; illegal-target re-check (CR 608.2b).
/// - Back face — pay 3 life → enters untapped; decline / can't pay /
///   no agent → enters tapped; mana ability adds {U}.
/// </summary>
public class SinkIntoStuporFactoryTests : IDisposable
{
    public SinkIntoStuporFactoryTests()
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
    public void SinkIntoStupor_Identity()
    {
        var alice = new Player("Alice", 20);
        var card = SinkIntoStuporFactory.Create(alice);

        card.Name.Should().Be("Sink into Stupor");
        card.ManaCost.Should().Be("{1}{U}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void SinkIntoStupor_IsBlue()
    {
        var alice = new Player("Alice", 20);
        var card = SinkIntoStuporFactory.Create(alice);

        // Colour derived from {U}{U} pips on the printed mana cost.
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColorEnum.Blue);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SinkIntoStupor()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Sink into Stupor", alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Sink into Stupor");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // =========================================================================
    // MDFC face tracker — front face carries Soporific Springs name
    // =========================================================================

    [Fact]
    public void SinkIntoStupor_CarriesMdfcState_FrontNameAndBackName()
    {
        var alice = new Player("Alice", 20);
        var card = SinkIntoStuporFactory.Create(alice);

        card.MdfcState.Should().NotBeNull(
            "Sink into Stupor is the front face of an MDFC and must carry a face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Sink into Stupor");
        card.MdfcState!.BackFaceName.Should().Be("Soporific Springs");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Sink into Stupor");
    }

    // =========================================================================
    // Front face — resolve: spell target → owner's hand off the stack
    // =========================================================================

    [Fact]
    public void SinkIntoStupor_Resolve_OpponentSpellOnStack_GoesToOwnersHand_NotGraveyard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        // Bob's spell on the stack — Sink's target.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = bob, Controller = bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, bob);
        stack.Push(bobSpell);

        var def = SinkIntoStuporFactory.BuildDefinition(
            caster: alice,
            targetResolver: o => o,
            stack: stack);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        stack.Count.Should().Be(0, "Sink into Stupor removed the spell from the stack");
        bobBolt.Zone.Should().Be(ZoneType.Hand,
            "the countered card goes to its owner's hand (CR 701.16-style redirect)");
        bob.Zones.Hand.GetCards().Should().Contain(bobBolt);
        bob.Zones.Graveyard.GetCards().Should().NotContain(bobBolt,
            "Sink explicitly routes the card to hand, NOT graveyard");
    }

    [Fact]
    public void SinkIntoStupor_Resolve_TargetSpellNoLongerOnStack_NoOp()
    {
        // CR 608.2b — target spell already left the stack. Effect does nothing.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = bob, Controller = bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, bob);
        // Deliberately NOT pushed.

        var def = SinkIntoStuporFactory.BuildDefinition(alice, o => o, stack);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { bobSpell } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bobBolt.Zone.Should().NotBe(ZoneType.Hand,
            "no-op when target spell is no longer on the stack (CR 608.2b)");
        bob.Zones.Hand.GetCards().Should().NotContain(bobBolt);
    }

    // =========================================================================
    // Front face — resolve: nonland permanent target → owner's hand
    // =========================================================================

    [Fact]
    public void SinkIntoStupor_Resolve_OpponentNonlandPermanent_GoesToOwnersHand()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bear);

        var def = SinkIntoStuporFactory.BuildDefinition(alice, o => o, stack: null);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { bear } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Hand, "CR 701.20 — returned to owner's hand");
        bob.Zones.Hand.GetCards().Should().Contain(bear);
        bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
        bear.Controller.Should().BeSameAs(bob, "controller resets to owner on entering hand");
    }

    [Fact]
    public void SinkIntoStupor_Resolve_LandPermanent_NoOp()
    {
        // Printed text excludes lands. CR 608.2b — at resolution, a land
        // target is illegal → do nothing.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var island = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob,
            Controller = bob,
        };
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var def = SinkIntoStuporFactory.BuildDefinition(alice, o => o, stack: null);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        island.Zone.Should().Be(ZoneType.Battlefield, "land target is illegal — no bounce");
        bob.Zones.Battlefield.GetCards().Should().Contain(island);
    }

    [Fact]
    public void SinkIntoStupor_Resolve_OwnSideSpell_NoOp()
    {
        // Printed text: "an OPPONENT controls". A spell whose controller
        // is the caster is illegal at resolution.
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var aliceBolt = new Instant("Lightning Bolt", "{R}") { Owner = alice, Controller = alice };
        var aliceSpell = new Majik.Core.Spells.Spell(aliceBolt, alice);
        stack.Push(aliceSpell);

        var def = SinkIntoStuporFactory.BuildDefinition(alice, o => o, stack);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { aliceSpell } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        stack.Count.Should().Be(1, "caster's own spell is not a legal target — stack unchanged");
        aliceBolt.Zone.Should().NotBe(ZoneType.Hand);
    }

    // =========================================================================
    // Front face — candidate gatherer: only opponent spells + nonland permanents
    // =========================================================================

    [Fact]
    public void SinkIntoStupor_CandidateGatherer_IncludesOpponentSpellsAndNonlandPermanents()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        // Bob's stuff (should be candidates).
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = bob, Controller = bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, bob);
        stack.Push(bobSpell);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bobBear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobBear);

        var bobIsland = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob,
            Controller = bob,
        };
        bobIsland.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobIsland);

        // Alice's stuff (should NOT be candidates — opponent-only).
        var aliceBolt = new Instant("Lightning Bolt", "{R}") { Owner = alice, Controller = alice };
        var aliceSpell = new Majik.Core.Spells.Spell(aliceBolt, alice);
        stack.Push(aliceSpell);

        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = alice, Controller = alice };
        aliceBear.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(aliceBear);

        var def = SinkIntoStuporFactory.BuildDefinition(alice, o => o, stack);
        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: Majik.Core.StateMachine.PhaseStateType.PreCombatMain,
            stack: stack);

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(bobSpell, "opponent-controlled spell on the stack is a candidate");
        candidates.Should().Contain(bobBear, "opponent-controlled nonland permanent is a candidate");
        candidates.Should().NotContain(bobIsland, "lands are excluded (printed text: NONLAND permanent)");
        candidates.Should().NotContain(aliceSpell, "own-side spell is excluded (opponent-only)");
        candidates.Should().NotContain(aliceBear, "own-side permanent is excluded (opponent-only)");
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void SoporificSprings_Identity()
    {
        var alice = new Player("Alice", 20);
        var land = SoporificSpringsFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Soporific Springs");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Soporific Springs is a non-Basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SoporificSprings()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Soporific Springs", alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Soporific Springs");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SoporificSprings_CarriesMdfcState_PreFlippedToBackFace()
    {
        var alice = new Player("Alice", 20);
        var land = SoporificSpringsFactory.Create(alice);

        land.MdfcState.Should().NotBeNull(
            "Soporific Springs is the back face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Sink into Stupor");
        land.MdfcState!.BackFaceName.Should().Be("Soporific Springs");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Soporific Springs");
    }

    // =========================================================================
    // Back face — {T}: Add {U}
    // =========================================================================

    [Fact]
    public void SoporificSprings_HasSingleManaAbility_AddingBlue()
    {
        var alice = new Player("Alice", 20);
        var land = SoporificSpringsFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {U} ability");

        var expected = ManaCost.Parse("U");
        manaAbilities[0].ManaGenerated.Generic.Should().Be(expected.Generic);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(expected.Blue);
        manaAbilities[0].ManaGenerated.Blue.Should().BeGreaterThan(0, "produces blue mana");
    }

    [Fact]
    public void SoporificSprings_HasNoActivatedOrTriggeredAbilities_BeyondMana()
    {
        var alice = new Player("Alice", 20);
        var land = SoporificSpringsFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Soporific Springs has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement, not a triggered ability (CR 614.1c)");
    }

    // =========================================================================
    // Back face — ETB pay-3-life replacement
    // =========================================================================

    [Fact]
    public void SoporificSprings_EntersUntapped_WhenAgentPaysThreeLife()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = SoporificSpringsFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Soporific Springs enters untapped when the controller pays 3 life");
        alice.LifeTotal.Should().Be(17, "paying 3 life drops Alice from 20 → 17");
    }

    [Fact]
    public void SoporificSprings_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(alice, agent);

        var land = SoporificSpringsFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Soporific Springs enters tapped when the controller declines to pay 3 life");
        alice.LifeTotal.Should().Be(20, "declining keeps Alice's life unchanged");
    }

    [Fact]
    public void SoporificSprings_EntersTapped_WhenControllerCannotPayThreeLife()
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

        var land = SoporificSpringsFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            $"Soporific Springs enters tapped when controller can't pay 3 life (life={alice.LifeTotal})");
        alice.LifeTotal.Should().Be(2, "life unchanged — no payment took place");
    }

    [Fact]
    public void SoporificSprings_EntersUntapped_AtExactlyThreeLife()
    {
        // CR 119.4 carve-out — payments may bring you to 0. At exactly 3
        // life paying is legal: drop to 0; SBAs handle loss-of-game.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.LoseLife(17); // life = 3
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = SoporificSpringsFactory.Create(alice, replacements: bus);

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
    public void SoporificSprings_EntersTapped_WhenNoAgentRegistered()
    {
        // No AgentRegistry.Set — the predicate's no-agent branch should
        // default to declining (matches ShockLandCycleFactory's posture).
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        var land = SoporificSpringsFactory.Create(alice, replacements: bus);

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
