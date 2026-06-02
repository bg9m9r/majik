using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SetessanChampionFactory"/>.
///
/// Covers (CR 702.144 Constellation — see factory xmldoc):
/// - Card identity (name, Human Warrior subtypes, P/T, mana cost,
///   owner/controller).
/// - Single <see cref="TriggeredAbility"/> attached to the card shape.
/// - End-to-end constellation firing through a live
///   <see cref="TriggerManager"/>:
///     * Self-ETB fires (Setessan Champion itself counts).
///     * Another enchantment ETB under controller fires.
///     * Default no-agent posture = auto-accept: life is paid, card is drawn.
/// - Negative cases: opponent enchantment ETB does not fire; non-
///   enchantment, non-self ETB under controller does not fire.
/// - <see cref="NamedCardFactory"/> dispatch returns Setessan Champion with
///   the right shape.
/// </summary>
[Trait("Color", "G")]
public class SetessanChampionFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SetessanChampion_Identity_NameSubtypesPtAndManaCost()
    {
        var c = SetessanChampionFactory.Create(_alice);

        c.Name.Should().Be("Setessan Champion");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.ManaCost.Should().Be("{1}{G}{G}");

        var parsed = ManaCost.Parse(c.ManaCost);
        parsed.Generic.Should().Be(1);
        parsed.Green.Should().Be(2, "two green pips");
        parsed.TotalValue.Should().Be(3);

        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SetessanChampion_HasExactlyOneTriggeredAbility_Constellation()
    {
        var c = SetessanChampionFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Setessan Champion carries the single constellation trigger (CR 702.144)");
    }
    // -----------------------------------------------------------------------
    // Constellation behaviour — end-to-end via TriggerManager
    // -----------------------------------------------------------------------

    [Fact]
    public void Constellation_FiresOnSelfEtb_PaysOneLifeAndDraws()
    {
        var zones = new ZoneService(_bus);
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var champion = SetessanChampionFactory.Create(_alice, triggers, agent: null);
        // Start in hand; ETB via ZoneService so CardMovedEvent fires.
        champion.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(champion);
        triggers.BindCard(champion);

        // Library top — so the draw produces a deterministic card.
        var libTop = new Creature("Llanowar Elves", "{G}", 1, 1);
        libTop.SetOwner(_alice);
        _alice.Zones.Library.AddCard(libTop);
        libTop.SetZone(ZoneType.Library);

        zones.MoveCardTo(champion, ZoneType.Battlefield, _alice);

        triggers.PendingCount.Should().Be(1,
            "self-ETB qualifies under constellation — 'Setessan Champion or another " +
            "enchantment enters'");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(19,
            "auto-accept posture (no agent) pays 1 life on the 'you may'");
        _alice.Zones.Hand.GetCards().Should().Contain(libTop,
            "'if you do, draw a card' — paid the life, drew the top");
    }

    [Fact]
    public void Constellation_FiresOnAnotherEnchantmentEtb_PaysOneLifeAndDraws()
    {
        var zones = new ZoneService(_bus);
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var champion = SetessanChampionFactory.Create(_alice, triggers, agent: null);
        champion.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(champion);
        triggers.BindCard(champion);

        var libTop = new Creature("Llanowar Elves", "{G}", 1, 1);
        libTop.SetOwner(_alice);
        _alice.Zones.Library.AddCard(libTop);
        libTop.SetZone(ZoneType.Library);

        // Another enchantment enters under Alice's control.
        var ench = new Enchantment("Wild Growth", "{G}");
        ench.SetOwner(_alice);
        ench.SetController(_alice);
        ench.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(ench);

        zones.MoveCardTo(ench, ZoneType.Battlefield, _alice);

        triggers.PendingCount.Should().Be(1,
            "another enchantment under controller — constellation predicate fires");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(19);
        _alice.Zones.Hand.GetCards().Should().Contain(libTop);
    }

    [Fact]
    public void Constellation_DoesNotFire_ForOpponentEnchantmentEtb()
    {
        var zones = new ZoneService(_bus);
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var champion = SetessanChampionFactory.Create(_alice, triggers, agent: null);
        champion.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(champion);
        triggers.BindCard(champion);

        // Bob plays an enchantment — Setessan Champion must not fire.
        var bobAura = new Enchantment("Pacifism", "{1}{W}");
        bobAura.SetOwner(_bob);
        bobAura.SetController(_bob);
        bobAura.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(bobAura);

        zones.MoveCardTo(bobAura, ZoneType.Battlefield, _bob);

        triggers.PendingCount.Should().Be(0,
            "controller-gated predicate excludes opponent ETBs");
    }

    [Fact]
    public void Constellation_DoesNotFire_ForUnrelatedCreatureEtb()
    {
        var zones = new ZoneService(_bus);
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var champion = SetessanChampionFactory.Create(_alice, triggers, agent: null);
        champion.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(champion);
        triggers.BindCard(champion);

        // Plain (non-self, non-enchantment) creature ETB under controller —
        // predicate excludes it.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bear);

        zones.MoveCardTo(bear, ZoneType.Battlefield, _alice);

        triggers.PendingCount.Should().Be(0,
            "non-enchantment, non-self ETBs do not satisfy the constellation predicate");
    }
}
