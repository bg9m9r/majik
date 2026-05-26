using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Heliod's Pilgrim (Theros Beyond Death, {1}{W}, Creature —
/// Human Cleric 1/2).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost,
///     owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - ETB tutor pulls the first Aura card from the library to the hand
///     (CR 701.19a). Non-Aura cards are filtered.
///   - ETB tutor is a no-op when no Aura is present (declined / empty
///     candidate pool per CR 701.19a).
/// </summary>
public class HeliodsPilgrimTests
{
    private readonly Player _alice = new("Alice", 20);

    // ------------------------------------------------------------------
    // Identity
    // ------------------------------------------------------------------

    [Fact]
    public void HeliodsPilgrim_Identity()
    {
        var c = HeliodsPilgrimFactory.Create(_alice);

        c.Name.Should().Be("Heliod's Pilgrim");
        c.ManaCost.Should().Be("{1}{W}");
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(2);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue("Heliod's Pilgrim is a Human");
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue("Heliod's Pilgrim is a Cleric");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HeliodsPilgrim()
    {
        var card = NamedCardFactory.Create("Heliod's Pilgrim", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Heliod's Pilgrim");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
    }

    [Fact]
    public void HeliodsPilgrim_HasOneTriggeredAbility()
    {
        var c = HeliodsPilgrimFactory.Create(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the printed text has exactly one ETB triggered ability");
    }

    // ------------------------------------------------------------------
    // ETB tutor
    // ------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_PullsAuraFromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        // A non-Aura first; an Aura second. The tutor must skip the
        // non-Aura and find the Aura.
        var bait = new Card("Random Sorcery", "");
        bait.SetOwner(alice);
        alice.Zones.Library.AddCard(bait);
        bait.SetZone(ZoneType.Library);

        var aura = new Enchantment("Pacifism", "1W",
            subtypes: new[] { CardSubtype.Aura });
        aura.SetOwner(alice);
        alice.Zones.Library.AddCard(aura);
        aura.SetZone(ZoneType.Library);

        var pilgrim = HeliodsPilgrimFactory.Create(alice);
        var etb = pilgrim.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        aura.Zone.Should().Be(ZoneType.Hand,
            "the ETB tutor pulled the Aura to hand (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().Contain(aura);
        alice.Zones.Library.GetCards().Should().NotContain(aura);
        bait.Zone.Should().Be(ZoneType.Library,
            "non-Aura cards remain in the library");
    }

    [Fact]
    public void EtbTrigger_NoAuraInLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var unrelated = new Card("Random Card", "");
        unrelated.SetOwner(alice);
        alice.Zones.Library.AddCard(unrelated);
        unrelated.SetZone(ZoneType.Library);

        var pilgrim = HeliodsPilgrimFactory.Create(alice);
        var etb = pilgrim.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("no Aura in library = CR 701.19a decline / no-op");
        unrelated.Zone.Should().Be(ZoneType.Library);
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
