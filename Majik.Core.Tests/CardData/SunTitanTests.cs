using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SunTitanFactory"/>.
///
/// Covers:
/// - Identity (name, type Creature, P/T 6/6, Giant subtype, mana cost,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Vigilance keyword marker (CR 702.20) consumed by CombatAbilities.
/// - ETB triggered ability reanimates target permanent card with mana
///   value ≤ 3 from controller's graveyard (CR 603.1).
/// - ETB triggered ability no-ops when no permanent card with mv ≤ 3
///   is in graveyard (Hill Giant mv 4 → not eligible).
/// - Attack triggered ability fires on CreatureAttacksEvent and runs the
///   same reanimate effect (CR 508.1f).
/// </summary>
public class SunTitanTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SunTitan_Identity()
    {
        var c = SunTitanFactory.Create(_alice);

        c.Name.Should().Be("Sun Titan");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(6);
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue("Sun Titan is a Giant (CR 205.3m)");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCost.Should().Be("{4}{W}{W}");
    }

    [Fact]
    public void SunTitan_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sun Titan", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Sun Titan");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue();
        c.ManaCost.Should().Be("{4}{W}{W}");
    }

    // -----------------------------------------------------------------------
    // Vigilance keyword (CR 702.20)
    // -----------------------------------------------------------------------

    [Fact]
    public void SunTitan_HasVigilanceKeyword()
    {
        var c = SunTitanFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Vigilance",
            "CR 702.20 — Vigilance is a printed evergreen on Sun Titan");

        CombatAbilities.HasVigilance(c).Should().BeTrue(
            "CombatAbilities.HasVigilance consumes the KeywordAbility marker");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — reanimate target permanent card with mv ≤ 3
    // -----------------------------------------------------------------------

    [Fact]
    public void SunTitan_EtbTrigger_ReanimatesPermanentWithManaValueAtMostThree()
    {
        var alice = new Player("Alice", 20);

        // A Bear (mv 2 creature) is eligible — creature is a permanent type.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var titan = SunTitanFactory.Create(alice);
        var etb = titan.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "Grizzly Bears is a creature card with mv 2 — eligible under the ≤ 3 cap");
        alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
        alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        bear.Controller.Should().BeSameAs(alice,
            "the reanimated permanent enters under the activator's control (CR 110.2)");
    }

    [Fact]
    public void SunTitan_EtbTrigger_NoEligibleTarget_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        // mv 4 creature is NOT eligible (exceeds ≤ 3 cap).
        var giant = new Creature("Hill Giant", "3R", 3, 3);
        giant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        var titan = SunTitanFactory.Create(alice);
        var etb = titan.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow(
            "no permanent card with mv ≤ 3 in graveyard → no-op (CR 117.x — no legal target)");
        giant.Zone.Should().Be(ZoneType.Graveyard,
            "Hill Giant has mv 4 — outside the ≤ 3 cap, must remain in graveyard");
        alice.Zones.Battlefield.GetCards().Should().NotContain(giant);
    }

    // -----------------------------------------------------------------------
    // Attack trigger — same reanimate effect (CR 508.1f)
    // -----------------------------------------------------------------------

    [Fact]
    public void SunTitan_AttackTrigger_FiresOnCreatureAttacksEvent_AndReanimatesPermanent()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // A mv-3 creature in graveyard is eligible.
        var elf = new Creature("Wood Elves", "2G", 1, 1);
        elf.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(elf);
        elf.SetZone(ZoneType.Graveyard);

        var titan = SunTitanFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        // Locate the attack trigger by its CreatureAttacksEvent condition.
        var attackTrigger = titan.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

        // CR 508.1f — fires when this creature is declared as an attacker.
        var attackEvent = new CreatureAttacksEvent(titan, bob);
        attackTrigger.IsTriggered(attackEvent).Should().BeTrue(
            "the attack trigger matches CreatureAttacksEvent where the source is the attacker");

        // A different attacker should NOT trigger this ability.
        var otherAttacker = new Creature("Llanowar Elves", "G", 1, 1);
        otherAttacker.SetOwner(alice);
        otherAttacker.SetController(alice);
        otherAttacker.SetZone(ZoneType.Battlefield);
        var otherEvent = new CreatureAttacksEvent(otherAttacker, bob);
        attackTrigger.IsTriggered(otherEvent).Should().BeFalse(
            "the per-attacker trigger only fires for Sun Titan itself");

        // Resolve the attack-trigger effect — same reanimate body as the ETB.
        foreach (var effect in attackTrigger.Effects) effect.Execute();

        elf.Zone.Should().Be(ZoneType.Battlefield,
            "Wood Elves has mv 3 — eligible under the ≤ 3 cap, reanimated by attack trigger");
        alice.Zones.Graveyard.GetCards().Should().NotContain(elf);
        alice.Zones.Battlefield.GetCards().Should().Contain(elf);
        elf.Controller.Should().BeSameAs(alice,
            "the reanimated permanent enters under the activator's control (CR 110.2)");
    }
}
