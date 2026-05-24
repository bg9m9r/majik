using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WallOfRootsFactory"/>.
///
/// Covers:
/// - Card identity (0/5 Creature — Plant Wall, mana cost {1}{G}).
/// - Defender keyword marker (CR 702.3) — surfaced via
///   <see cref="CombatAbilities.HasDefender"/>.
/// - Mana ability shape: produces {G}, no {T} cost (the source stays
///   untapped after activation; the activation pays the -0/-1 counter
///   side-effect alone).
/// - Activation places one -0/-1 counter on Wall of Roots and the
///   layered toughness (via <see cref="ContinuousEffectsService"/>'s
///   layer 7c handler — CR 122.1g) reads one lower.
/// - Once-per-turn gate (CR 602.5e): activating again the same turn is
///   blocked; the <see cref="TurnStartedEvent"/> reset handler re-enables
///   the ability on the next turn (CR 500.1).
/// - SBA kill: 5 -0/-1 counters → toughness 0 → <see cref="Creature.IsDead"/>
///   (CR 704.5f).
/// - NamedCardFactory dispatcher resolves "Wall of Roots" to the
///   expected Plant Wall shape with the Defender keyword + the cost-
///   counter mana ability attached.
/// </summary>
public class WallOfRootsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfRoots_IsCreature()
    {
        var card = WallOfRootsFactory.Create(_alice);

        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void WallOfRoots_NameIsCorrect()
    {
        var card = WallOfRootsFactory.Create(_alice);

        card.Name.Should().Be("Wall of Roots");
    }

    [Fact]
    public void WallOfRoots_HasCorrectPrintedManaCost()
    {
        var card = WallOfRootsFactory.Create(_alice);

        card.ManaCost.Should().Be("{1}{G}");
    }

    [Fact]
    public void WallOfRoots_HasCorrectPrintedPowerAndToughness()
    {
        var card = WallOfRootsFactory.Create(_alice);

        card.BasePower.Should().Be(0);
        card.BaseToughness.Should().Be(5);
    }

    [Fact]
    public void WallOfRoots_HasPlantAndWallSubtypes()
    {
        var card = WallOfRootsFactory.Create(_alice);

        card.Subtypes.Should().Contain(CardSubtype.Plant);
        card.Subtypes.Should().Contain(CardSubtype.Wall);
    }

    [Fact]
    public void WallOfRoots_OwnerAndControllerAreSet()
    {
        var card = WallOfRootsFactory.Create(_alice);

        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WallOfRoots_IsNotLegendary()
    {
        var card = WallOfRootsFactory.Create(_alice);

        card.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Defender keyword
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfRoots_HasDefenderKeyword()
    {
        var card = WallOfRootsFactory.Create(_alice);

        // CR 702.3 — Defender wired as a KeywordAbility marker.
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender");
        CombatAbilities.HasDefender(card).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Mana ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfRoots_HasExactlyOneManaAbility()
    {
        var card = WallOfRootsFactory.Create(_alice);

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the printed mana ability is the only ManaAbility on the card");
    }

    [Fact]
    public void WallOfRoots_ManaAbility_ProducesGreen()
    {
        var card = WallOfRootsFactory.Create(_alice);
        var mana = card.Abilities.OfType<ManaAbility>().Single();

        mana.ManaGenerated.Green.Should().Be(1,
            "the activated ability adds one {G}");
        mana.ManaGenerated.Generic.Should().Be(0);
        mana.ManaGenerated.White.Should().Be(0);
        mana.ManaGenerated.Blue.Should().Be(0);
        mana.ManaGenerated.Black.Should().Be(0);
        mana.ManaGenerated.Red.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Activation — places -0/-1 counter + does NOT tap
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfRoots_ActivatingManaAbility_PlacesMinusZeroMinusOneCounter()
    {
        var card = WallOfRootsFactory.Create(_alice);
        var mana = card.Abilities.OfType<ManaAbility>().Single();

        card.Counters.Count(CounterType.MinusZeroMinusOne).Should().Be(0,
            "no counters before the first activation");

        mana.Activate();

        card.Counters.Count(CounterType.MinusZeroMinusOne).Should().Be(1,
            "each activation places one -0/-1 counter on Wall of Roots");
    }

    [Fact]
    public void WallOfRoots_ActivatingManaAbility_DoesNotTapTheCard()
    {
        // The printed cost is the place-counter-on-self side-effect alone —
        // there is no {T} component, so the permanent must stay untapped.
        var card = WallOfRootsFactory.Create(_alice);
        var mana = card.Abilities.OfType<ManaAbility>().Single();

        card.IsTapped.Should().BeFalse();
        mana.Activate();
        card.IsTapped.Should().BeFalse(
            "Wall of Roots' cost-counter mana ability does NOT include {T}");
    }

    [Fact]
    public void WallOfRoots_ActivatingManaAbility_ReducesToughnessByOne()
    {
        // CR 122.1g + ContinuousEffectsService layer 7c — a -0/-1 counter
        // shifts only toughness. The Wall needs ActiveEffects wired to
        // observe the layered toughness (same posture as the +1/+1 /
        // -1/-1 counter tests in CounterPTTests).
        var svc = new ContinuousEffectsService();
        var card = WallOfRootsFactory.Create(_alice);
        card.ActiveEffects = svc;

        card.Toughness.Should().Be(5);

        var mana = card.Abilities.OfType<ManaAbility>().Single();
        mana.Activate();

        card.Toughness.Should().Be(4,
            "one -0/-1 counter reduces toughness by 1 via layer 7c");
        card.Power.Should().Be(0,
            "a -0/-1 counter does NOT shift power (CR 122.1g — toughness only)");
    }

    // -----------------------------------------------------------------------
    // Once-per-turn gate (CR 602.5e)
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfRoots_ManaAbility_IsAvailableBeforeFirstActivation()
    {
        var card = WallOfRootsFactory.Create(_alice);
        var mana = card.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue(
            "the once-per-turn gate is open before any activation this turn");
    }

    [Fact]
    public void WallOfRoots_SecondActivationSameTurn_IsBlocked()
    {
        var card = WallOfRootsFactory.Create(_alice);
        var mana = card.Abilities.OfType<ManaAbility>().Single();

        mana.Activate();

        mana.CanActivate().Should().BeFalse(
            "CR 602.5e — \"Activate only once each turn\" gates the ability " +
            "after the first use this turn");
    }

    [Fact]
    public void WallOfRoots_OnNewTurn_ActivationGateResets()
    {
        // CR 500.1 — turn start. The bus-aware overload subscribes a
        // TurnStartedEvent handler that resets the once-per-turn closure.
        var bus = new EventBus();
        var card = WallOfRootsFactory.Create(_alice, bus);
        var mana = card.Abilities.OfType<ManaAbility>().Single();

        mana.Activate();
        mana.CanActivate().Should().BeFalse("first use is locked");

        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        mana.CanActivate().Should().BeTrue(
            "the TurnStartedEvent reset handler re-opens the gate at the " +
            "start of the next turn");
    }

    [Fact]
    public void WallOfRoots_OnNewTurn_PreviouslyPlacedCountersPersist()
    {
        // Wall of Roots' printed wording does NOT cycle -0/-1 counters
        // off in the cleanup step — they accumulate across turns.
        var bus = new EventBus();
        var card = WallOfRootsFactory.Create(_alice, bus);
        var mana = card.Abilities.OfType<ManaAbility>().Single();

        mana.Activate();
        card.Counters.Count(CounterType.MinusZeroMinusOne).Should().Be(1);

        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        card.Counters.Count(CounterType.MinusZeroMinusOne).Should().Be(1,
            "-0/-1 counters persist across turns (no cleanup-step removal)");

        mana.Activate();
        card.Counters.Count(CounterType.MinusZeroMinusOne).Should().Be(2,
            "the second turn's activation stacks a second -0/-1 counter");
    }

    // -----------------------------------------------------------------------
    // SBA — 5 -0/-1 counters → toughness 0 → IsDead (CR 704.5f)
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfRoots_FiveMinusZeroMinusOneCounters_KillTheWall()
    {
        // CR 704.5f — a creature with toughness 0 (or less) is put into its
        // owner's graveyard by the SBA pass. Direct IsDead check here —
        // SBA scheduler tests live in their own suite; this confirms the
        // layered-toughness → IsDead transition is correct.
        var svc = new ContinuousEffectsService();
        var card = WallOfRootsFactory.Create(_alice);
        card.ActiveEffects = svc;

        card.Counters.Add(CounterType.MinusZeroMinusOne, 5);

        card.Toughness.Should().Be(0,
            "5 -0/-1 counters reduce printed toughness 5 → 0");
        card.IsDead().Should().BeTrue(
            "CR 704.5f — toughness 0 with 0 damage marked still satisfies " +
            "the Damage >= Toughness IsDead check");
    }

    [Fact]
    public void WallOfRoots_FourMinusZeroMinusOneCounters_StillAlive()
    {
        // Boundary check — 4 counters leaves toughness 1, not lethal.
        var svc = new ContinuousEffectsService();
        var card = WallOfRootsFactory.Create(_alice);
        card.ActiveEffects = svc;

        card.Counters.Add(CounterType.MinusZeroMinusOne, 4);

        card.Toughness.Should().Be(1);
        card.IsDead().Should().BeFalse(
            "4 -0/-1 counters leaves toughness 1 — Wall of Roots survives");
    }

    // -----------------------------------------------------------------------
    // Dispatcher integration
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfRoots_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create("Wall of Roots", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Wall of Roots");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Subtypes.Should().Contain(CardSubtype.Plant);
        card.Subtypes.Should().Contain(CardSubtype.Wall);
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender",
                "dispatcher path attaches the Defender keyword");
        card.Abilities.OfType<ManaAbility>()
            .Should().HaveCount(1,
                "dispatcher path attaches the cost-counter mana ability");
    }
}
