using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
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
/// Tests for <see cref="SunderingEruptionFactory"/> and
/// <see cref="VolcanicFissureFactory"/> — the front + back faces of the
/// Innistrad: Reawakening modal double-faced card Sundering Eruption //
/// Volcanic Fissure.
///
/// Front face (Sundering Eruption, {2}{R}):
///   Sorcery. "Destroy target land. Its controller may search their library
///   for a basic land card, put it onto the battlefield tapped, then shuffle.
///   Creatures without flying can't block this turn."
///
/// Back face (Volcanic Fissure):
///   Land. "As this land enters, you may pay 3 life. If you don't, it enters
///   tapped." "{T}: Add {R}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner).
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front face — candidate gatherer: only land permanents offered.
/// - Front face — resolve destroys target land (CR 701.7b).
/// - Front face — non-land target at resolution → no-op (CR 608.2b).
/// - Front face — compensation search: destroyed land's controller MAY
///   search library for basic land → battlefield tapped + shuffle.
/// - Front face — compensation search declined → no search, no shuffle.
/// - Front face — "creatures without flying can't block this turn":
///   non-flying creature can't block; flying creature CAN block.
/// - Front face — ground-can't-block restriction expires at end of turn.
/// - Back face — {T}: Add {R} mana ability attached.
/// - Back face — pay 3 life → enters untapped.
/// - Back face — decline → enters tapped.
/// - Back face — can't pay (life &lt; 3) → enters tapped (CR 119.4).
/// - Back face — exactly 3 life → payment legal, enters untapped.
/// - Back face — no agent → enters tapped.
/// </summary>
public class SunderingEruptionFactoryTests : IDisposable
{
    public SunderingEruptionFactoryTests()
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
    public void SunderingEruption_Identity_TwoRSorcery_ManaValueThree()
    {
        var alice = new Player("Alice", 20);
        var card = SunderingEruptionFactory.Create(alice);

        card.Name.Should().Be("Sundering Eruption");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(3,
            "Sundering Eruption costs {2}{R} — generic 2 + coloured 1 = MV 3 (CR 202.3)");
    }

    [Fact]
    public void SunderingEruption_IsRed()
    {
        var alice = new Player("Alice", 20);
        var card = SunderingEruptionFactory.Create(alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColorEnum.Red,
            "Sundering Eruption has {R} pip — it is mono-red");
        colors.Should().NotContain(ManaColorEnum.Blue);
        colors.Should().NotContain(ManaColorEnum.Black);
        colors.Should().NotContain(ManaColorEnum.Green);
        colors.Should().NotContain(ManaColorEnum.White);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SunderingEruption()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Sundering Eruption", alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Sundering Eruption");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // =========================================================================
    // MDFC face tracker — front face
    // =========================================================================

    [Fact]
    public void SunderingEruption_CarriesMdfcState_FrontFace()
    {
        var alice = new Player("Alice", 20);
        var card = SunderingEruptionFactory.Create(alice);

        card.MdfcState.Should().NotBeNull(
            "Sundering Eruption is the front face of an MDFC and must carry a face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Sundering Eruption");
        card.MdfcState!.BackFaceName.Should().Be("Volcanic Fissure");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Sundering Eruption");
    }

    // =========================================================================
    // Front face — candidate gatherer
    // =========================================================================

    [Fact]
    public void SunderingEruption_CandidateGatherer_OnlyLandPermanents()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Bob's land — should be a candidate.
        var bobIsland = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob, Controller = bob,
        };
        bobIsland.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobIsland);

        // Bob's creature — should NOT be a candidate.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bobBear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobBear);

        // Alice's land — should also be a candidate (no "opponent" restriction).
        var aliceMountain = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain })
        {
            Owner = alice, Controller = alice,
        };
        aliceMountain.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(aliceMountain);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var def = SunderingEruptionFactory.BuildDefinition(alice, o => o);
        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: Majik.Core.StateMachine.PhaseStateType.PreCombatMain,
            stack: stack);

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(bobIsland, "land permanents are legal targets");
        candidates.Should().Contain(aliceMountain, "own lands are legal targets — no 'opponent' restriction");
        candidates.Should().NotContain(bobBear, "creatures are not lands — not a legal target");
    }

    // =========================================================================
    // Front face — resolve destroys target land
    // =========================================================================

    [Fact]
    public void SunderingEruption_Resolve_DestroysTargetLand()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var island = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob, Controller = bob,
        };
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var def = SunderingEruptionFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { island } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        island.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.7b — destroyed land moves to its owner's graveyard");
        bob.Zones.Battlefield.GetCards().Should().NotContain(island);
        bob.Zones.Graveyard.GetCards().Should().Contain(island);
    }

    [Fact]
    public void SunderingEruption_Resolve_NonLandTarget_NoOp()
    {
        // CR 608.2b — if the resolved object is not a land on the battlefield
        // at resolution time the effect does nothing.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bear);

        var def = SunderingEruptionFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { bear } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "non-land target is illegal at resolution — effect is a no-op (CR 608.2b)");
        bob.Zones.Battlefield.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void SunderingEruption_Resolve_TargetLeftBattlefield_NoOp()
    {
        // CR 608.2b — target land already left the battlefield.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var island = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob, Controller = bob,
        };
        // NOT placed on battlefield — already gone.
        island.SetZone(ZoneType.Graveyard);
        bob.Zones.Graveyard.AddCard(island);

        var def = SunderingEruptionFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        island.Zone.Should().Be(ZoneType.Graveyard,
            "target was already in graveyard — effect is a no-op (CR 608.2b)");
    }

    // =========================================================================
    // Front face — compensation search (destroyed land controller may search)
    // =========================================================================

    [Fact]
    public void SunderingEruption_Resolve_CompensationSearch_AgentAccepts_BasicGoesToBattlefieldTapped()
    {
        // After destroying Bob's land, Bob is offered the search.
        // Bob's agent accepts → basic land goes to battlefield tapped.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Bob's agent accepts the search offer; ChooseLibraryPickAsync
        // falls back to candidates[0] automatically (ScriptedAgent default).
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true); // "Yes, I'll search"
        AgentRegistry.Set(bob, agent);

        // Bob's land to be destroyed.
        var island = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob, Controller = bob,
        };
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        // Bob's library has a Mountain.
        var mountain = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain })
        {
            Owner = bob, Controller = bob,
        };
        bob.Zones.Library.AddCard(mountain);

        var def = SunderingEruptionFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // The destroyed land should be in the graveyard.
        island.Zone.Should().Be(ZoneType.Graveyard, "target land was destroyed");

        // The basic land from the library should be on the battlefield (tapped).
        mountain.Zone.Should().Be(ZoneType.Battlefield,
            "compensation search puts the basic land onto the battlefield");
        bob.Zones.Battlefield.GetCards().Should().Contain(mountain);
        if (mountain is Permanent mPerm)
            mPerm.IsTapped.Should().BeTrue("the basic land enters tapped per oracle text");
    }

    [Fact]
    public void SunderingEruption_Resolve_CompensationSearch_AgentDeclines_NoSearch()
    {
        // Bob declines the search → no library change.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false); // "No, I'll skip the search"
        AgentRegistry.Set(bob, agent);

        var island = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob, Controller = bob,
        };
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var mountain = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain })
        {
            Owner = bob, Controller = bob,
        };
        bob.Zones.Library.AddCard(mountain);

        var def = SunderingEruptionFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        island.Zone.Should().Be(ZoneType.Graveyard, "target land was destroyed");
        mountain.Zone.Should().NotBe(ZoneType.Battlefield,
            "Bob declined the search — no land was put onto the battlefield");
        bob.Zones.Library.GetCards().Should().Contain(mountain,
            "library unchanged when agent declines");
    }

    [Fact]
    public void SunderingEruption_Resolve_CompensationSearch_NoAgentForController_NoSearch()
    {
        // No agent registered for the destroyed land's controller → no search.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Note: AgentRegistry.Clear() was called in constructor; Bob has no agent.

        var island = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob, Controller = bob,
        };
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var mountain = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain })
        {
            Owner = bob, Controller = bob,
        };
        bob.Zones.Library.AddCard(mountain);

        var def = SunderingEruptionFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        island.Zone.Should().Be(ZoneType.Graveyard, "target land was destroyed");
        mountain.Zone.Should().NotBe(ZoneType.Battlefield,
            "no agent registered → default decline → no search");
    }

    // =========================================================================
    // Front face — "creatures without flying can't block this turn"
    // =========================================================================

    [Fact]
    public void SunderingEruption_Resolve_GroundCreatureCannotBlock()
    {
        // After Sundering Eruption resolves, a non-flying creature cannot
        // be declared as a blocker (CombatRestriction.CannotBlock applies).
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var island = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob, Controller = bob,
        };
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var effects = new ContinuousEffectsService();

        // Non-flying blocker (no KeywordAbility for Flying).
        var groundCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = bob, Controller = bob,
        };
        groundCreature.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(groundCreature);

        var def = SunderingEruptionFactory.BuildDefinition(alice, o => o, effects);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Verify the ground creature has CannotBlock restriction.
        effects.HasRestriction(groundCreature, CombatRestriction.CannotBlock)
            .Should().BeTrue(
                "creatures without flying can't block this turn after Sundering Eruption resolves");
    }

    [Fact]
    public void SunderingEruption_Resolve_FlyingCreatureCanBlock()
    {
        // A flying creature is NOT covered by the "creatures without flying
        // can't block" restriction — flying creatures CAN block normally.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var island = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob, Controller = bob,
        };
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var effects = new ContinuousEffectsService();

        // Flying blocker (has Flying keyword).
        var flier = new Creature("Wind Drake", "{2}{U}", 2, 2)
        {
            Owner = bob, Controller = bob,
        };
        flier.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(flier);
        flier.AddAbility(new KeywordAbility("Flying", flier, bob));

        var def = SunderingEruptionFactory.BuildDefinition(alice, o => o, effects);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Flying creatures are excluded from the can't-block predicate.
        effects.HasRestriction(flier, CombatRestriction.CannotBlock)
            .Should().BeFalse(
                "flying creatures are excluded from the 'without flying' predicate and CAN block");
    }

    [Fact]
    public void SunderingEruption_Resolve_GroundCantBlockRestriction_ExpiresAtEndOfTurn()
    {
        // The CombatRestrictionEffect is EOT-scoped (ExpiresAtEndOfTurn = true).
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var island = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob, Controller = bob,
        };
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var effects = new ContinuousEffectsService();

        var def = SunderingEruptionFactory.BuildDefinition(alice, o => o, effects);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Simulate end-of-turn expiry (drops effects where ExpiresAtEndOfTurn = true).
        effects.ExpireEndOfTurn();

        var groundCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        effects.HasRestriction(groundCreature, CombatRestriction.CannotBlock)
            .Should().BeFalse(
                "EOT-scoped restriction expires at end of turn (CR 514.2)");
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void VolcanicFissure_Identity()
    {
        var alice = new Player("Alice", 20);
        var land = VolcanicFissureFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Volcanic Fissure");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Volcanic Fissure is a non-Basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VolcanicFissure()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Volcanic Fissure", alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Volcanic Fissure");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void VolcanicFissure_CarriesMdfcState_PreFlippedToBackFace()
    {
        var alice = new Player("Alice", 20);
        var land = VolcanicFissureFactory.Create(alice);

        land.MdfcState.Should().NotBeNull(
            "Volcanic Fissure is the back face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Sundering Eruption");
        land.MdfcState!.BackFaceName.Should().Be("Volcanic Fissure");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Volcanic Fissure");
    }

    // =========================================================================
    // Back face — {T}: Add {R}
    // =========================================================================

    [Fact]
    public void VolcanicFissure_HasSingleManaAbility_AddingRed()
    {
        var alice = new Player("Alice", 20);
        var land = VolcanicFissureFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {R} ability");

        var expected = ManaCost.Parse("R");
        manaAbilities[0].ManaGenerated.Red.Should().Be(expected.Red);
        manaAbilities[0].ManaGenerated.Red.Should().BeGreaterThan(0, "produces red mana");
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0,
            "Volcanic Fissure does not produce blue mana");
        manaAbilities[0].ManaGenerated.Black.Should().Be(0,
            "Volcanic Fissure does not produce black mana");
    }

    [Fact]
    public void VolcanicFissure_HasNoActivatedOrTriggeredAbilities_BeyondMana()
    {
        var alice = new Player("Alice", 20);
        var land = VolcanicFissureFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Volcanic Fissure has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement, not a triggered ability (CR 614.1c)");
    }

    // =========================================================================
    // Back face — ETB pay-3-life replacement
    // =========================================================================

    [Fact]
    public void VolcanicFissure_EntersUntapped_WhenAgentPaysThreeLife()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = VolcanicFissureFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Volcanic Fissure enters untapped when the controller pays 3 life");
        alice.LifeTotal.Should().Be(17, "paying 3 life drops Alice from 20 → 17");
    }

    [Fact]
    public void VolcanicFissure_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(alice, agent);

        var land = VolcanicFissureFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Volcanic Fissure enters tapped when the controller declines to pay 3 life");
        alice.LifeTotal.Should().Be(20, "declining keeps Alice's life unchanged");
    }

    [Fact]
    public void VolcanicFissure_EntersTapped_WhenControllerCannotPayThreeLife()
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

        var land = VolcanicFissureFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            $"Volcanic Fissure enters tapped when controller can't pay 3 life (life={alice.LifeTotal})");
        alice.LifeTotal.Should().Be(2, "life unchanged — no payment took place");
    }

    [Fact]
    public void VolcanicFissure_EntersUntapped_AtExactlyThreeLife()
    {
        // CR 119.4 carve-out — payments may bring you to 0. At exactly 3
        // life paying is legal: drop to 0; SBAs handle loss-of-game.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.LoseLife(17); // life = 3
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = VolcanicFissureFactory.Create(alice, replacements: bus);

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
    public void VolcanicFissure_EntersTapped_WhenNoAgentRegistered()
    {
        // No AgentRegistry.Set — the predicate's no-agent branch should
        // default to declining.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        var land = VolcanicFissureFactory.Create(alice, replacements: bus);

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
