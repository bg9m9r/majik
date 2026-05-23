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
/// Unit tests for <see cref="TrinketMageFactory"/>.
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB tutor: pulls a mana-value-0 artifact (Mox Opal) from library → hand.
/// - ETB tutor: pulls a mana-value-1 artifact from library → hand.
/// - ETB tutor: no eligible artifact (only mv ≥ 2) → no-op.
/// - ETB tutor: no artifacts at all → no-op.
/// </summary>
public class TrinketMageTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TrinketMage_Identity()
    {
        var c = TrinketMageFactory.Create(_alice);

        c.Name.Should().Be("Trinket Mage");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue("Trinket Mage is a Human");
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Trinket Mage is a Wizard");
        c.ManaCost.Should().Be("{2}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TrinketMage_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Trinket Mage", _alice);

        c.Should().BeOfType<Creature>("Trinket Mage is a Creature");
        c.Name.Should().Be("Trinket Mage");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB tutor — happy paths
    // -----------------------------------------------------------------------

    [Fact]
    public void TrinketMage_EtbTrigger_PullsManaValueZeroArtifact_FromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        // Seed library: a non-artifact first (filter check), then Mox Opal
        // (artifact, mana value 0).
        var bait = new Card("Random Card", "");
        bait.SetOwner(alice);
        alice.Zones.Library.AddCard(bait);
        bait.SetZone(ZoneType.Library);

        var moxOpal = new Artifact("Mox Opal", "0");
        moxOpal.SetOwner(alice);
        alice.Zones.Library.AddCard(moxOpal);
        moxOpal.SetZone(ZoneType.Library);

        var mage = TrinketMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        moxOpal.Zone.Should().Be(ZoneType.Hand,
            "ETB tutor pulled the mana-value-0 artifact to hand (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().Contain(moxOpal);
        alice.Zones.Library.GetCards().Should().NotContain(moxOpal);
        bait.Zone.Should().Be(ZoneType.Library,
            "the non-artifact card stays in the library (predicate-filtered)");
    }

    [Fact]
    public void TrinketMage_EtbTrigger_PullsManaValueOneArtifact_FromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        // An mv-1 artifact is eligible (mana value 1 or less).
        var trinket = new Artifact("One-Cost Trinket", "1");
        trinket.SetOwner(alice);
        alice.Zones.Library.AddCard(trinket);
        trinket.SetZone(ZoneType.Library);

        var mage = TrinketMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        trinket.Zone.Should().Be(ZoneType.Hand,
            "ETB tutor pulled the mana-value-1 artifact to hand (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().Contain(trinket);
        alice.Zones.Library.GetCards().Should().NotContain(trinket);
    }

    // -----------------------------------------------------------------------
    // ETB tutor — no eligible target
    // -----------------------------------------------------------------------

    [Fact]
    public void TrinketMage_EtbTrigger_OnlyHighMvArtifacts_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        // mv-2 and mv-3 artifacts are NOT eligible — Trinket Mage filters mv ≤ 1.
        var sword = new Artifact("Sword of Test", "2",
            subtypes: new[] { CardSubtype.Equipment });
        sword.SetOwner(alice);
        alice.Zones.Library.AddCard(sword);
        sword.SetZone(ZoneType.Library);

        var bigArtifact = new Artifact("Big Artifact", "3");
        bigArtifact.SetOwner(alice);
        alice.Zones.Library.AddCard(bigArtifact);
        bigArtifact.SetZone(ZoneType.Library);

        var mage = TrinketMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("no eligible artifact = CR 701.19a decline / no-op");
        sword.Zone.Should().Be(ZoneType.Library, "mv-2 artifact stays in library");
        bigArtifact.Zone.Should().Be(ZoneType.Library, "mv-3 artifact stays in library");
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no eligible artifact = nothing was tutored to hand");
    }

    [Fact]
    public void TrinketMage_EtbTrigger_NoArtifactsInLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var unrelated = new Card("Random Card", "");
        unrelated.SetOwner(alice);
        alice.Zones.Library.AddCard(unrelated);
        unrelated.SetZone(ZoneType.Library);

        var mage = TrinketMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("no artifact in library = CR 701.19a decline / no-op");
        unrelated.Zone.Should().Be(ZoneType.Library);
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
