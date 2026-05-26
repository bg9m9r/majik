using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Geist of Saint Traft (Innistrad, {1}{W}{U}).
///
/// Covers:
///   - Identity (name, types, supertype, subtypes, P/T, mana cost,
///     owner/controller, Legendary supertype).
///   - NamedCardFactory dispatch.
///   - Hexproof keyword marker.
///   - Attack trigger fires on Geist's own CreatureAttacksEvent, NOT on
///     another creature's attack (CR 508.1f per-attacker self-match).
///   - Attack trigger creates a 4/4 white Angel token with Flying under
///     Geist's controller.
/// </summary>
public class GeistOfSaintTraftTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GeistOfSaintTraft_Identity()
    {
        var g = GeistOfSaintTraftFactory.Create(_alice);

        g.Name.Should().Be("Geist of Saint Traft");
        g.ManaCost.Should().Be("{1}{W}{U}");
        g.HasType(CardType.Creature).Should().BeTrue();
        g.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "CR 205.4a — printed Legendary supertype");
        g.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        g.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        g.BasePower.Should().Be(2);
        g.BaseToughness.Should().Be(2);
        g.Owner.Should().BeSameAs(_alice);
        g.Controller.Should().BeSameAs(_alice);

        g.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Hexproof",
            "CR 702.11 — printed Hexproof keyword");
    }

    [Fact]
    public void GeistOfSaintTraft_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Geist of Saint Traft", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Geist of Saint Traft");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
    }

    [Fact]
    public void GeistOfSaintTraft_HasAttackTrigger_SelfOnly()
    {
        var g = GeistOfSaintTraftFactory.Create(_alice);
        g.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(g);

        trigger.IsTriggered(new CreatureAttacksEvent(g, _bob)).Should().BeTrue(
            "CR 508.1f per-attacker self-match");

        var other = new Creature("Some Goblin", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        other.SetOwner(_alice);
        other.SetController(_alice);
        trigger.IsTriggered(new CreatureAttacksEvent(other, _bob)).Should().BeFalse(
            "the per-attacker trigger only fires for Geist itself");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — create 4/4 white Angel token with Flying
    // -----------------------------------------------------------------------

    [Fact]
    public void AttackTrigger_CreatesFourFourWhiteAngelWithFlying()
    {
        var g = GeistOfSaintTraftFactory.Create(
            _alice, triggers: null, zoneService: null);
        _alice.Zones.Battlefield.AddCard(g);
        g.SetZone(ZoneType.Battlefield);

        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        var trigger = GetAttackTrigger(g);
        foreach (var e in trigger.Effects) e.Execute();

        var battlefieldAfter = _alice.Zones.Battlefield.GetCards().ToList();
        battlefieldAfter.Should().HaveCount(battlefieldBefore + 1,
            "the attack trigger creates exactly one Angel token");

        var token = battlefieldAfter.OfType<Creature>()
            .Single(c => c.IsToken && c.HasSubtype(CardSubtype.Angel));
        token.Name.Should().Be("Angel");
        token.BasePower.Should().Be(4);
        token.BaseToughness.Should().Be(4);
        token.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);
        token.Owner.Should().BeSameAs(_alice);

        // Flying is granted as a KeywordAbility marker on the token.
        token.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Flying");

        // CR 111.4 — white colour identity stamped explicitly.
        var colors = Majik.Core.Cards.CardColors.GetColors(token);
        colors.Should().Contain(ManaColor.White,
            "printed '4/4 white Angel' — CR 105 colour identity");
    }

    [Fact]
    public void AttackTrigger_TokenLandsOnControllersBattlefield_NotOpponents()
    {
        var g = GeistOfSaintTraftFactory.Create(
            _alice, triggers: null, zoneService: null);
        _alice.Zones.Battlefield.AddCard(g);
        g.SetZone(ZoneType.Battlefield);

        var bobBefore = _bob.Zones.Battlefield.GetCards().Count();

        var trigger = GetAttackTrigger(g);
        foreach (var e in trigger.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().HaveCount(bobBefore,
            "CR 109.5 — the trigger creates the token under the controller, not the opponent");
    }
}
