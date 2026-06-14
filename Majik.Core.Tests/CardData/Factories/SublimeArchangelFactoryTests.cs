using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SublimeArchangelFactory"/> (Avacyn Restored,
/// Creature — Angel {2}{W}{W} 4/3).
///
/// Oracle text (verified against Scryfall):
///   "Flying
///    Exalted (Whenever a creature you control attacks alone, that creature
///    gets +1/+1 until end of turn.)
///    Other creatures you control have exalted."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (mana cost, Angel subtype, 4/3).
/// - Flying keyword (from JSON).
/// - Exalted keyword marker on the Archangel itself (CR 702.91).
/// - The Archangel's own exalted trigger pumps a solo attacker +1/+1 EOT.
/// - "Other creatures you control have exalted" — a controlled OTHER creature
///   gains an exalted triggered ability registered with the live manager
///   (CR 613.1f / 702.91b).
/// - The Archangel itself is excluded from the grant ("Other"); a SECOND
///   exalted instance + the Archangel's own each fire separately, so a solo
///   attacker gets +2/+2.
/// </summary>
[Trait("Color", "W")]
public class SublimeArchangelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;

    public SublimeArchangelFactoryTests()
    {
        _effects = new ContinuousEffectsService(_bus);
        _zones = new ZoneService(_bus);
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
    }

    private Creature MakeBear(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.ActiveEffects = _effects;
        return c;
    }

    private void PutOnBattlefield(ICard card, Player owner)
    {
        owner.Zones.Library.AddCard(card);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);
    }

    private static TriggeredAbility ExaltedTrigger(ICard c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void SublimeArchangel_Identity()
    {
        var c = SublimeArchangelFactory.Create(_alice);

        c.Name.Should().Be("Sublime Archangel");
        c.ManaCost.Should().Be("{2}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SublimeArchangel_HasFlying()
    {
        var c = SublimeArchangelFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Sublime Archangel has Flying (CR 702.9).");
    }

    [Fact]
    public void SublimeArchangel_HasExaltedKeywordMarker()
    {
        var c = SublimeArchangelFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Exalted").Should().BeTrue(
                "Sublime Archangel has Exalted (CR 702.91).");
    }

    // ── Own exalted — solo attacker pumped ───────────────────────────────

    [Fact]
    public void SublimeArchangel_OwnExalted_SoloAttacker_GetsPumped()
    {
        // CR 702.91b — the attacker attacks alone; gets +1/+1 EOT from the
        // Archangel's own exalted.
        var attacker = MakeBear(_alice);
        var attackers = new List<Creature> { attacker };

        var archangel = SublimeArchangelFactory.Create(
            _alice, _effects, _triggers, () => attackers);
        archangel.SetZone(ZoneType.Battlefield);

        var trigger = ExaltedTrigger(archangel);
        trigger.IsTriggered(new CreatureAttacksEvent(attacker, _bob)).Should().BeTrue();

        foreach (var e in trigger.Effects) e.Execute();

        attacker.GetPower().Should().Be(3, "Exalted gives the solo attacker +1/+1.");
        attacker.GetToughness().Should().Be(3);
    }

    [Fact]
    public void SublimeArchangel_OwnExalted_TwoAttackers_NoPump()
    {
        // CR 702.91b — "attacks alone" requires no other controlled attackers.
        var a1 = MakeBear(_alice, "Bear Alpha");
        var a2 = MakeBear(_alice, "Bear Beta");
        var attackers = new List<Creature> { a1, a2 };

        var archangel = SublimeArchangelFactory.Create(
            _alice, _effects, _triggers, () => attackers);
        archangel.SetZone(ZoneType.Battlefield);

        var trigger = ExaltedTrigger(archangel);
        foreach (var e in trigger.Effects) e.Execute();

        a1.GetPower().Should().Be(2, "two attackers — not alone, no pump.");
        a1.GetToughness().Should().Be(2);
    }

    // ── "Other creatures you control have exalted" ───────────────────────

    [Fact]
    public void SublimeArchangel_GrantsExaltedToOtherControlledCreature()
    {
        // CR 613.1f / 702.91b — another creature Alice controls gains exalted,
        // registered with the live TriggerManager so it actually fires.
        var attackers = new List<Creature>();
        var archangel = SublimeArchangelFactory.Create(
            _alice, _effects, _triggers, () => attackers);
        PutOnBattlefield(archangel, _alice);

        var bear = MakeBear(_alice);
        PutOnBattlefield(bear, _alice);

        // Reconcile the Layer-6 group grant.
        _effects.Compute(bear);

        var granted = bear.Abilities.OfType<ITriggeredAbility>()
            .Where(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>)
            .ToList();
        granted.Should().HaveCount(1,
            "the Archangel grants exalted to another creature Alice controls.");
        _triggers.IsRegistered(granted[0]).Should().BeTrue(
            "the granted exalted trigger must be registered so it fires.");
    }

    [Fact]
    public void SublimeArchangel_DoesNotGrantExaltedToOpponentCreature()
    {
        var attackers = new List<Creature>();
        var archangel = SublimeArchangelFactory.Create(
            _alice, _effects, _triggers, () => attackers);
        PutOnBattlefield(archangel, _alice);

        var bobBear = MakeBear(_bob);
        PutOnBattlefield(bobBear, _bob);

        _effects.Compute(bobBear);

        bobBear.Abilities.OfType<ITriggeredAbility>().Should().BeEmpty(
            "exalted is granted only to creatures YOU control.");
    }

    [Fact]
    public void SublimeArchangel_GrantedExalted_StacksWithOwn_SoloAttackerGetsPlusTwo()
    {
        // CR 702.91b — "If a creature has multiple instances of exalted, each
        // triggers separately." The Archangel's own exalted + the exalted it
        // grants to the lone attacker each fire when that attacker attacks
        // alone, so it gets +2/+2.
        var archangel = SublimeArchangelFactory.Create(
            _alice, _effects, _triggers, () => Attackers());
        PutOnBattlefield(archangel, _alice);

        var bear = MakeBear(_alice);
        PutOnBattlefield(bear, _alice);
        _effects.Compute(bear);

        // bear attacks alone.
        _attacking.Add(bear);

        // The bear's OWN granted exalted instance fires.
        var bearExalted = ExaltedTrigger(bear);
        foreach (var e in bearExalted.Effects) e.Execute();
        // The Archangel's own exalted instance also fires (each separately).
        var archExalted = ExaltedTrigger(archangel);
        foreach (var e in archExalted.Effects) e.Execute();

        bear.GetPower().Should().Be(4,
            "two exalted instances each give +1/+1 to the lone attacker.");
        bear.GetToughness().Should().Be(4);
    }

    private readonly List<Creature> _attacking = new();
    private IReadOnlyList<Creature> Attackers() => _attacking;
}
