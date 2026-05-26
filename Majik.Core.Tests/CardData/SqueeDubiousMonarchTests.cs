using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SqueeDubiousMonarchFactory"/> (Dominaria
/// United, {2}{R}).
///
/// Covers:
/// - Identity (name, type Creature, supertype Legendary, subtype Goblin
///   + Warrior — v1 ships Goblin in lieu of the missing "Noble" subtype),
///   P/T 2/2, mana cost, owner / controller.
/// - NamedCardFactory dispatch.
/// - Menace + Haste keyword markers (CR 702.110 / 702.10).
/// - Attack trigger (CR 508.1f): fires on self-attacks event; creates a
///   1/1 red Goblin token on the controller's battlefield (v1: untapped
///   + not-attacking — see factory xmldoc).
/// - Attack trigger active only on the battlefield.
/// - Graveyard-activated unearth-style ability shape: mana cost
///   {3}{R}, exile-three-other + return-self resolution body.
/// - Activation no-ops if Squee isn't in graveyard.
/// - Activation no-ops if graveyard lacks 3 OTHER cards.
/// - Activation resolves: exiles 3 other graveyard cards + returns Squee
///   from graveyard to battlefield.
/// </summary>
public class SqueeDubiousMonarchTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Squee_Identity()
    {
        var c = SqueeDubiousMonarchFactory.Create(_alice);

        c.Name.Should().Be("Squee, Dubious Monarch");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue(
            "Squee is a Goblin — printed Goblin Noble; 'Noble' subtype not yet in the enum");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Squee_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Squee, Dubious Monarch", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Squee, Dubious Monarch");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Keywords (Menace, Haste)
    // -----------------------------------------------------------------------

    [Fact]
    public void Squee_HasMenaceAndHasteKeywords()
    {
        var c = SqueeDubiousMonarchFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Menace", "CR 702.110");
        keywords.Should().Contain("Haste", "CR 702.10");

        CombatAbilities.HasHaste(c).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Attack trigger (CR 508.1f)
    // -----------------------------------------------------------------------

    [Fact]
    public void Squee_AttackTrigger_FiresOnSelfAttack_NotOnOtherCreatureAttack()
    {
        var c = SqueeDubiousMonarchFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

        // Self-attack event matches.
        var selfEvent = new CreatureAttacksEvent(c, _alice);
        trigger.Condition.Matches(selfEvent, trigger).Should().BeTrue();

        // Different creature attacks — does NOT match.
        var otherGoblin = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        var otherEvent = new CreatureAttacksEvent(otherGoblin, _alice);
        trigger.Condition.Matches(otherEvent, trigger).Should().BeFalse();
    }

    [Fact]
    public void Squee_AttackTrigger_CreatesGoblinTokenOnControllerBattlefield()
    {
        var c = SqueeDubiousMonarchFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var trigger = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

        var beforeCount = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Count(x => x.Name == "Goblin");

        foreach (var e in trigger.Effects) e.Execute();

        var afterCount = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Count(x => x.Name == "Goblin");

        afterCount.Should().Be(beforeCount + 1,
            "Squee's attack trigger creates a 1/1 red Goblin token under his controller's control");

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().First(x => x.Name == "Goblin" && x.IsToken);
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
    }

    [Fact]
    public void Squee_AttackTrigger_OnlyActiveOnBattlefield()
    {
        var c = SqueeDubiousMonarchFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Activated ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Squee_ActivatedAbility_HasManaCost()
    {
        var c = SqueeDubiousMonarchFactory.Create(_alice);
        var ability = c.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "Squee's reanimation ability costs {3}{R} mana");
    }

    // -----------------------------------------------------------------------
    // Activated ability — resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Squee_ActivatedAbility_ExilesThreeOthers_AndReturnsSelf()
    {
        var alice = new Player("Alice", 20);
        var c = SqueeDubiousMonarchFactory.Create(alice);

        // Squee is in graveyard.
        alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);

        // Three other cards in graveyard (so the exile cost can be paid).
        var bolt = new Instant("Lightning Bolt", "R");
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        var swamp = new Land("Swamp");
        foreach (var card in new ICard[] { bolt, bear, swamp })
        {
            card.SetOwner(alice);
            alice.Zones.Graveyard.AddCard(card);
            card.SetZone(ZoneType.Graveyard);
        }

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        c.Zone.Should().Be(ZoneType.Battlefield,
            "Squee returned from graveyard to battlefield");
        alice.Zones.Battlefield.GetCards().Should().Contain(c);

        bolt.Zone.Should().Be(ZoneType.Exile, "Lightning Bolt exiled as part of cost");
        bear.Zone.Should().Be(ZoneType.Exile, "Bear exiled as part of cost");
        swamp.Zone.Should().Be(ZoneType.Exile, "Swamp exiled as part of cost");
        alice.Zones.Exile.GetCards()
            .Should().Contain(new ICard[] { bolt, bear, swamp });
    }

    [Fact]
    public void Squee_ActivatedAbility_NotInGraveyard_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var c = SqueeDubiousMonarchFactory.Create(alice);

        // Squee is on the battlefield, NOT graveyard.
        alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        // Three sacrificable cards in the graveyard.
        for (var i = 0; i < 3; i++)
        {
            var filler = new Instant($"Filler {i}", "R");
            filler.SetOwner(alice);
            alice.Zones.Graveyard.AddCard(filler);
            filler.SetZone(ZoneType.Graveyard);
        }

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => { foreach (var e in ability.Effects) e.Execute(); };

        act.Should().NotThrow("shape guard — activation no-ops when Squee not in graveyard");
        c.Zone.Should().Be(ZoneType.Battlefield);
        alice.Zones.Exile.GetCards().Should().BeEmpty(
            "no exiles should happen when activation guard rejects");
    }

    [Fact]
    public void Squee_ActivatedAbility_NotEnoughOthers_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var c = SqueeDubiousMonarchFactory.Create(alice);

        alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);

        // Only two OTHER cards — not enough to pay "exile three OTHER".
        var bolt = new Instant("Lightning Bolt", "R");
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        foreach (var card in new ICard[] { bolt, bear })
        {
            card.SetOwner(alice);
            alice.Zones.Graveyard.AddCard(card);
            card.SetZone(ZoneType.Graveyard);
        }

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => { foreach (var e in ability.Effects) e.Execute(); };

        act.Should().NotThrow();
        c.Zone.Should().Be(ZoneType.Graveyard, "Squee stays in graveyard — cost wasn't payable");
        bolt.Zone.Should().Be(ZoneType.Graveyard, "no exiles when cost unpayable");
        bear.Zone.Should().Be(ZoneType.Graveyard);
    }
}
