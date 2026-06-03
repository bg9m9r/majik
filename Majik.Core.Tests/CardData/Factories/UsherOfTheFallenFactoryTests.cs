using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Usher of the Fallen (Kaldheim, {W}, 2/1 Spirit Warrior) — the
/// Boast-keyword (CR 702.135) cluster card this deferral unblocks. Verifies the
/// printed shape, the Boast activated ability + its "attacked this turn / only
/// once each turn" gate, the white Human Warrior token, and Birgi's boast-twice
/// cap override.
/// </summary>
public class UsherOfTheFallenFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature Usher(IEventBus? bus = null)
    {
        var u = UsherOfTheFallenFactory.Create(_alice, bus, zones: null);
        u.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(u);
        return u;
    }

    private Core.Combat.Combat DeclareAttack(Creature attacker)
    {
        var combat = new Core.Combat.Combat(_alice, _bob);
        combat.AddAttacker(new Attacker(attacker, targetPlayer: _bob));
        return combat;
    }

    private ActivatedAbility BoastOf(Creature u) =>
        u.Abilities.OfType<ActivatedAbility>().Single();

    [Fact]
    public void Usher_HasPrintedShape_SpiritWarrior_2_1_W()
    {
        var u = UsherOfTheFallenFactory.Create(_alice);

        u.Name.Should().Be("Usher of the Fallen");
        u.Power.Should().Be(2);
        u.Toughness.Should().Be(1);
        u.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        u.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        u.ManaCost.Should().Be("{W}");
    }

    [Fact]
    public void Usher_CarriesBoastKeyword_AndActivatedAbilityWithCost()
    {
        var u = UsherOfTheFallenFactory.Create(_alice);

        u.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Boast");
        BoastOf(u).Costs.Should().ContainSingle().Which.Should().BeOfType<ManaCostCost>();
    }

    [Fact]
    public void Boast_GateClosedUntilAttack_ThenOpens()
    {
        var bus = new EventBus();
        var u = Usher(bus);
        var boast = BoastOf(u);

        boast.CanActivateNow().Should().BeFalse("hasn't attacked yet");

        bus.Publish(new AttackersDeclaredEvent(DeclareAttack(u)));
        boast.CanActivateNow().Should().BeTrue();
    }

    [Fact]
    public void Boast_OnlyOnceEachTurn()
    {
        var bus = new EventBus();
        var u = Usher(bus);
        var boast = BoastOf(u);

        bus.Publish(new AttackersDeclaredEvent(DeclareAttack(u)));
        boast.CanActivateNow().Should().BeTrue();

        bus.Publish(new AbilityActivatedEvent(boast));
        boast.CanActivateNow().Should().BeFalse("only once each turn");
    }

    [Fact]
    public void Boast_CreatesWhiteHumanWarriorToken()
    {
        var token = UsherOfTheFallenFactory.CreateHumanWarriorToken(_alice);

        token.Name.Should().Be("Human Warrior");
        token.Power.Should().Be(1);
        token.Toughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Human).Should().BeTrue();
        token.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        Majik.Core.Cards.CardColors.GetColors(token).Should().Contain(ManaColor.White);
    }

    [Fact]
    public void Boast_EffectMintsTokenOntoBattlefield()
    {
        var bus = new EventBus();
        var u = Usher(bus);
        var boast = BoastOf(u);

        var before = _alice.Zones.Battlefield.GetCards().Count();
        foreach (var eff in boast.Effects) eff.Execute();
        var after = _alice.Zones.Battlefield.GetCards().Count();

        after.Should().Be(before + 1);
        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Should().Contain(c => c.Name == "Human Warrior");
    }

    [Fact]
    public void Birgi_RaisesUsherBoastCapToTwo()
    {
        // CR 702.135c — Birgi: "Creatures you control can boast twice during
        // each of your turns rather than once."
        var bus = new EventBus();
        var u = Usher(bus);
        var boast = BoastOf(u);

        var birgi = BirgiGodOfStorytellingFactory.Create(_alice);
        birgi.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(birgi);

        bus.Publish(new AttackersDeclaredEvent(DeclareAttack(u)));
        boast.CanActivateNow().Should().BeTrue();
        bus.Publish(new AbilityActivatedEvent(boast));   // first boast
        boast.CanActivateNow().Should().BeTrue("Birgi lets it boast twice");
        bus.Publish(new AbilityActivatedEvent(boast));   // second boast
        boast.CanActivateNow().Should().BeFalse("cap of 2 reached");
    }
}
