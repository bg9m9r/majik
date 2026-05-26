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
/// Unit tests for <see cref="SpellseekerFactory"/>.
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB tutor: pulls a mana-value-1 instant from library → hand.
/// - ETB tutor: pulls a mana-value-2 sorcery from library → hand.
/// - ETB tutor: instants/sorceries above mv 2 are not eligible → no-op.
/// - ETB tutor: non-instant/sorcery cards filtered out → no-op.
/// </summary>
public class SpellseekerTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Spellseeker_Identity()
    {
        var c = SpellseekerFactory.Create(_alice);

        c.Name.Should().Be("Spellseeker");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue("Spellseeker is a Human");
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Spellseeker is a Wizard");
        c.ManaCost.Should().Be("{2}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Spellseeker_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Spellseeker", _alice);

        c.Should().BeOfType<Creature>("Spellseeker is a Creature");
        c.Name.Should().Be("Spellseeker");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB tutor — happy paths
    // -----------------------------------------------------------------------

    [Fact]
    public void Spellseeker_EtbTrigger_PullsManaValueOneInstant_FromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        var lightningBolt = new Instant("Lightning Bolt", "{R}");
        lightningBolt.SetOwner(alice);
        alice.Zones.Library.AddCard(lightningBolt);
        lightningBolt.SetZone(ZoneType.Library);

        var mage = SpellseekerFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        lightningBolt.Zone.Should().Be(ZoneType.Hand,
            "ETB tutor pulled the mv-1 instant to hand (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().Contain(lightningBolt);
        alice.Zones.Library.GetCards().Should().NotContain(lightningBolt);
    }

    [Fact]
    public void Spellseeker_EtbTrigger_PullsManaValueTwoSorcery_FromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        var sorcery = new Sorcery("Two-Cost Sorcery", "{1}{U}");
        sorcery.SetOwner(alice);
        alice.Zones.Library.AddCard(sorcery);
        sorcery.SetZone(ZoneType.Library);

        var mage = SpellseekerFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        sorcery.Zone.Should().Be(ZoneType.Hand,
            "ETB tutor pulled the mv-2 sorcery to hand (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().Contain(sorcery);
    }

    // -----------------------------------------------------------------------
    // ETB tutor — no eligible target
    // -----------------------------------------------------------------------

    [Fact]
    public void Spellseeker_EtbTrigger_OnlyHighMvSpells_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        // mv-3 instant and mv-4 sorcery — both above the mv ≤ 2 gate.
        var bigInstant = new Instant("Three-Cost Instant", "{1}{U}{U}");
        bigInstant.SetOwner(alice);
        alice.Zones.Library.AddCard(bigInstant);
        bigInstant.SetZone(ZoneType.Library);

        var bigSorcery = new Sorcery("Four-Cost Sorcery", "{2}{U}{U}");
        bigSorcery.SetOwner(alice);
        alice.Zones.Library.AddCard(bigSorcery);
        bigSorcery.SetZone(ZoneType.Library);

        var mage = SpellseekerFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("no eligible spell = CR 701.19a decline / no-op");
        bigInstant.Zone.Should().Be(ZoneType.Library);
        bigSorcery.Zone.Should().Be(ZoneType.Library);
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Spellseeker_EtbTrigger_NoInstantOrSorcery_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        // Creature card with mv ≤ 2 — predicate must reject (not instant/sorcery).
        var creature = new Creature("Mana One Creature", "{U}", power: 1, toughness: 1);
        creature.SetOwner(alice);
        alice.Zones.Library.AddCard(creature);
        creature.SetZone(ZoneType.Library);

        var mage = SpellseekerFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("no instant/sorcery candidate = CR 701.19a no-op");
        creature.Zone.Should().Be(ZoneType.Library,
            "creature card is filtered out by the instant-or-sorcery predicate");
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
