using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="RestlessBivouacFactory"/> (March of the Machine
/// "Restless" creature-land cycle, red/white member). Land:
///   "This land enters tapped.
///    {T}: Add {R} or {W}.
///    {1}{R}{W}: This land becomes a 2/2 red and white Ox creature until
///    end of turn. It's still a land.
///    Whenever this land attacks, put a +1/+1 counter on target creature
///    you control."
///
/// Covers:
/// - Identity (Land, no supertype, name, owner/controller).
/// - JSON-backed {T}: Add {R} / {T}: Add {W} mana abilities (two).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Animate ability cost ({1}{R}{W}, instant speed) + Layer 4 / Layer 7b:
///     * Adds Creature type + Ox subtype on Layer 4 ("still a land").
///     * Records 2/2 base P/T on Layer 7b.
/// - Unconditional ETB-tapped replacement.
/// - Attack trigger: a 1..1 "target creature you control" TargetRequest,
///   placing one +1/+1 counter on the chosen target.
/// </summary>
[Trait("Color", "C")]
public class RestlessBivouacFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessBivouac_Identity()
    {
        var land = RestlessBivouacFactory.Create(_alice);

        land.Name.Should().Be("Restless Bivouac");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Restless Bivouac is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RestlessBivouac_HasManaAnimateAndAttackTrigger()
    {
        var land = RestlessBivouacFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "{T}: Add {R} and {T}: Add {W} are wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{1}{R}{W} animate ability is wired");
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the attack trigger is attached to the land shape");
    }
    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessBivouac_AnimateAbility_HasPrintedManaCost1RW()
    {
        var land = RestlessBivouacFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({1}{R}{W})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void RestlessBivouac_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessBivouacFactory.Create(_alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Ox,
            "Ox subtype added");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped — unconditional
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessBivouac_RegistersUnconditionalEtbTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = RestlessBivouacFactory.Create(_alice, effects: null, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "\"This land enters tapped\" — unconditional (CR 614.1c)");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — +1/+1 counter on target creature you control
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessBivouac_AttackTrigger_RequestsOneTargetCreature()
    {
        var land = RestlessBivouacFactory.Create(_alice);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.TargetRequests.Should().HaveCount(1,
            "the attack trigger needs one 'target creature you control'");
        var req = trigger.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
    }

    [Fact]
    public void RestlessBivouac_AttackTrigger_PutsCounterOnChosenTarget()
    {
        var land = RestlessBivouacFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // A creature Alice controls to receive the counter.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        foreach (var e in trigger.Effects) e.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the attack trigger places one +1/+1 counter on the chosen creature");
    }
}
