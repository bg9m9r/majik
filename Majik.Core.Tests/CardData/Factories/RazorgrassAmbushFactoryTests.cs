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
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="RazorgrassAmbushFactory"/> and
/// <see cref="RazorgrassFieldFactory"/> — the front + back faces of the
/// Modern Horizons 3 modal double-faced card Razorgrass Ambush //
/// Razorgrass Field.
///
/// Front face (Razorgrass Ambush, {1}{W}):
///   Instant. "Razorgrass Ambush deals 3 damage to target attacking or
///   blocking creature."
///
/// Back face (Razorgrass Field):
///   Land. "As this land enters, you may pay 3 life. If you don't, it
///   enters tapped." "{T}: Add {W}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner, MV).
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front face — candidate gatherer: only attacking/blocking creatures
///   offered; a bystander creature is NOT a legal target (CR 509.1,
///   CR 509.3).
/// - Front face — resolve deals 3 damage to a legal creature target.
/// - Front face — non-Creature target at resolution → no-op (CR 608.2b).
/// - Front face — target off the battlefield at resolution → no-op
///   (CR 608.2b).
/// - Back face — {T}: Add {W} mana ability attached.
/// - Back face — pay 3 life → enters untapped.
/// - Back face — decline → enters tapped.
/// - Back face — can't pay (life &lt; 3) → enters tapped (CR 119.4).
/// - Back face — exactly 3 life → payment legal, enters untapped.
/// - Back face — no agent → enters tapped.
/// </summary>
[Trait("Color", "W")]
public class RazorgrassAmbushFactoryTests : IDisposable
{
    public RazorgrassAmbushFactoryTests()
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
    public void RazorgrassAmbush_Identity_OneWInstant_ManaValueTwo()
    {
        var alice = new Player("Alice", 20);
        var card = RazorgrassAmbushFactory.Create(alice);

        card.Name.Should().Be("Razorgrass Ambush");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(2,
            "Razorgrass Ambush costs {1}{W} — generic 1 + coloured 1 = MV 2 (CR 202.3)");
    }

    [Fact]
    public void RazorgrassAmbush_IsWhite()
    {
        var alice = new Player("Alice", 20);
        var card = RazorgrassAmbushFactory.Create(alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.White,
            "Razorgrass Ambush has {W} pip — it is mono-white");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.Red);
    }
    // =========================================================================
    // MDFC face tracker — front face
    // =========================================================================

    [Fact]
    public void RazorgrassAmbush_CarriesMdfcState_FrontFace()
    {
        var alice = new Player("Alice", 20);
        var card = RazorgrassAmbushFactory.Create(alice);

        card.MdfcState.Should().NotBeNull(
            "Razorgrass Ambush is the front face of an MDFC and must carry a face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Razorgrass Ambush");
        card.MdfcState!.BackFaceName.Should().Be("Razorgrass Field");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Razorgrass Ambush");
    }

    // =========================================================================
    // Front face — candidate gatherer: only attacking/blocking creatures
    // =========================================================================

    [Fact]
    public void RazorgrassAmbush_CandidateGatherer_OnlyAttackingAndBlockingCreatures()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Attacker — legal target.
        var attacker = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        attacker.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(attacker);

        // Blocker — legal target.
        var blocker = new Creature("Llanowar Elves", "{G}", 1, 1) { Owner = bob, Controller = bob };
        blocker.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(blocker);

        // Bystander — NOT in combat, must NOT be a legal target.
        var bystander = new Creature("Savannah Lions", "{W}", 2, 1) { Owner = alice, Controller = alice };
        bystander.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(bystander);

        // Inject the combat lookup returning only attacker + blocker.
        IReadOnlyList<Creature> CombatLookup() => new[] { attacker, blocker };

        var def = RazorgrassAmbushFactory.BuildDefinition(alice, o => o, CombatLookup);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: bob,
            turnNumber: 1,
            currentPhase: Majik.Core.StateMachine.PhaseStateType.DeclareBlockers,
            stack: stack);

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(attacker, "attacking creatures are legal targets");
        candidates.Should().Contain(blocker, "blocking creatures are legal targets");
        candidates.Should().NotContain(bystander,
            "creatures not in combat are not legal targets for Razorgrass Ambush");
    }

    [Fact]
    public void RazorgrassAmbush_CandidateGatherer_NullLookup_ReturnsEmpty()
    {
        // Shape-only / dispatcher path: no combatCreatureLookup supplied.
        var alice = new Player("Alice", 20);
        var def = RazorgrassAmbushFactory.BuildDefinition(alice, o => o, combatCreatureLookup: null);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: Majik.Core.StateMachine.PhaseStateType.DeclareBlockers,
            stack: stack);

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);
        candidates.Should().BeEmpty(
            "when no combat lookup is wired the gatherer reports no candidates");
    }

    // =========================================================================
    // Front face — resolve: deal 3 damage to the target creature
    // =========================================================================

    [Fact]
    public void RazorgrassAmbush_Resolve_Deals3DamageToTargetCreature()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // A 2/2 attacker — should survive 2 damage but die to 3.
        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        target.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(target);

        var def = RazorgrassAmbushFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        target.Damage.Should().Be(3,
            "Razorgrass Ambush deals exactly 3 damage to the target (CR 120.2)");
    }

    [Fact]
    public void RazorgrassAmbush_Resolve_NonCreatureTarget_NoOp()
    {
        // CR 608.2b — if the resolved object is not a Creature the effect
        // does nothing. (A land is never a legal candidate, but we guard
        // defensively at resolution.)
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var island = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island })
        {
            Owner = bob, Controller = bob,
        };
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var def = RazorgrassAmbushFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);

        // Should not throw; land has no DamageMarked.
        var act = () => { foreach (var e in def.EffectFactory(chosen)) e.Execute(); };
        act.Should().NotThrow(
            "non-Creature target at resolution is a no-op (CR 608.2b)");
    }

    [Fact]
    public void RazorgrassAmbush_Resolve_TargetLeftBattlefield_NoOp()
    {
        // CR 608.2b — target creature already left the battlefield.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        // Deliberately NOT placed on battlefield — already in graveyard.
        target.SetZone(ZoneType.Graveyard);
        bob.Zones.Graveyard.AddCard(target);

        var def = RazorgrassAmbushFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { target } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        target.Damage.Should().Be(0,
            "target was not on the battlefield at resolution — no damage dealt (CR 608.2b)");
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void RazorgrassField_Identity()
    {
        var alice = new Player("Alice", 20);
        var land = RazorgrassFieldFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Razorgrass Field");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Razorgrass Field is a non-Basic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }
    [Fact]
    public void RazorgrassField_CarriesMdfcState_PreFlippedToBackFace()
    {
        var alice = new Player("Alice", 20);
        var land = RazorgrassFieldFactory.Create(alice);

        land.MdfcState.Should().NotBeNull(
            "Razorgrass Field is the back face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Razorgrass Ambush");
        land.MdfcState!.BackFaceName.Should().Be("Razorgrass Field");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Razorgrass Field");
    }

    // =========================================================================
    // Back face — {T}: Add {W}
    // =========================================================================

    [Fact]
    public void RazorgrassField_HasSingleManaAbility_AddingWhite()
    {
        var alice = new Player("Alice", 20);
        var land = RazorgrassFieldFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {W} ability");

        var expected = ManaCost.Parse("W");
        manaAbilities[0].ManaGenerated.White.Should().Be(expected.White);
        manaAbilities[0].ManaGenerated.White.Should().BeGreaterThan(0, "produces white mana");
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0,
            "Razorgrass Field does not produce blue mana");
        manaAbilities[0].ManaGenerated.Red.Should().Be(0,
            "Razorgrass Field does not produce red mana");
    }

    [Fact]
    public void RazorgrassField_HasNoActivatedOrTriggeredAbilities_BeyondMana()
    {
        var alice = new Player("Alice", 20);
        var land = RazorgrassFieldFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Razorgrass Field has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement, not a triggered ability (CR 614.1c)");
    }

    // =========================================================================
    // Back face — ETB pay-3-life replacement
    // =========================================================================

    [Fact]
    public void RazorgrassField_EntersUntapped_WhenAgentPaysThreeLife()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = RazorgrassFieldFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Razorgrass Field enters untapped when the controller pays 3 life");
        alice.LifeTotal.Should().Be(17, "paying 3 life drops Alice from 20 → 17");
    }

    [Fact]
    public void RazorgrassField_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(alice, agent);

        var land = RazorgrassFieldFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Razorgrass Field enters tapped when the controller declines to pay 3 life");
        alice.LifeTotal.Should().Be(20, "declining keeps Alice's life unchanged");
    }

    [Fact]
    public void RazorgrassField_EntersTapped_WhenControllerCannotPayThreeLife()
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

        var land = RazorgrassFieldFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            $"Razorgrass Field enters tapped when controller can't pay 3 life (life={alice.LifeTotal})");
        alice.LifeTotal.Should().Be(2, "life unchanged — no payment took place");
    }

    [Fact]
    public void RazorgrassField_EntersUntapped_AtExactlyThreeLife()
    {
        // CR 119.4 carve-out — payments may bring you to 0. At exactly 3
        // life paying is legal: drop to 0; SBAs handle loss-of-game.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.LoseLife(17); // life = 3
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = RazorgrassFieldFactory.Create(alice, replacements: bus);

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
    public void RazorgrassField_EntersTapped_WhenNoAgentRegistered()
    {
        // No AgentRegistry.Set — the predicate's no-agent branch should
        // default to declining.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        var land = RazorgrassFieldFactory.Create(alice, replacements: bus);

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
