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
/// Unit tests for <see cref="TributeMageFactory"/>.
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB tutor: artifact with mv exactly 2 → hand.
/// - ETB tutor: artifact with mv != 2 ignored (mv 1 / mv 3).
/// - ETB tutor: non-artifact mv-2 card ignored.
/// - ETB tutor: no eligible target → no-op.
/// </summary>
public class TributeMageTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TributeMage_Identity()
    {
        var c = TributeMageFactory.Create(_alice);

        c.Name.Should().Be("Tribute Mage");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TributeMage_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Tribute Mage", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Tribute Mage");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB tutor — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void TributeMage_EtbTrigger_PullsManaValueTwoArtifact_FromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        // mv-2 artifact — Mind Stone shape ({2}).
        var mindStone = new Artifact("Mind Stone", "{2}");
        mindStone.SetOwner(alice);
        alice.Zones.Library.AddCard(mindStone);
        mindStone.SetZone(ZoneType.Library);

        var mage = TributeMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        mindStone.Zone.Should().Be(ZoneType.Hand,
            "ETB tutor pulled the mv-2 artifact to hand (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().Contain(mindStone);
        alice.Zones.Library.GetCards().Should().NotContain(mindStone);
    }

    // -----------------------------------------------------------------------
    // ETB tutor — filter rejection
    // -----------------------------------------------------------------------

    [Fact]
    public void TributeMage_EtbTrigger_IgnoresWrongManaValue()
    {
        var alice = new Player("Alice", 20);

        // mv-1 and mv-3 artifacts are NOT eligible — Tribute Mage requires
        // mv exactly 2.
        var moxOpal = new Artifact("Mox Opal", "{0}");
        moxOpal.SetOwner(alice);
        alice.Zones.Library.AddCard(moxOpal);
        moxOpal.SetZone(ZoneType.Library);

        var mvOne = new Artifact("One-Cost Trinket", "{1}");
        mvOne.SetOwner(alice);
        alice.Zones.Library.AddCard(mvOne);
        mvOne.SetZone(ZoneType.Library);

        var mvThree = new Artifact("Heavy Gear", "{3}");
        mvThree.SetOwner(alice);
        alice.Zones.Library.AddCard(mvThree);
        mvThree.SetZone(ZoneType.Library);

        var mage = TributeMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        moxOpal.Zone.Should().Be(ZoneType.Library, "mv-0 artifact ignored");
        mvOne.Zone.Should().Be(ZoneType.Library, "mv-1 artifact ignored");
        mvThree.Zone.Should().Be(ZoneType.Library, "mv-3 artifact ignored");
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no eligible mv-2 artifact = nothing tutored (CR 701.19a)");
    }

    [Fact]
    public void TributeMage_EtbTrigger_IgnoresNonArtifactsAtMvTwo()
    {
        var alice = new Player("Alice", 20);

        // An mv-2 non-artifact — predicate rejects.
        var creature = new Creature("Grizzly Bears", "{1}{G}", power: 2, toughness: 2);
        creature.SetOwner(alice);
        alice.Zones.Library.AddCard(creature);
        creature.SetZone(ZoneType.Library);

        var mage = TributeMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        creature.Zone.Should().Be(ZoneType.Library, "non-artifact mv-2 card ignored");
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void TributeMage_EtbTrigger_NoEligibleTarget_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var mage = TributeMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("empty candidate set = clean no-op (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void TributeMage_EtbTrigger_FirstEligibleCandidateWins()
    {
        // Deterministic fallback when no agent is registered: first
        // eligible candidate in the library is picked.
        var alice = new Player("Alice", 20);

        var first = new Artifact("First mv-2 Artifact", "{2}");
        first.SetOwner(alice);
        alice.Zones.Library.AddCard(first);
        first.SetZone(ZoneType.Library);

        var second = new Artifact("Second mv-2 Artifact", "{1}{R}".Replace("{R}", "{R}"));
        // Use a different mv-2 cost so identity is distinct; same MV.
        second.SetOwner(alice);
        alice.Zones.Library.AddCard(second);
        second.SetZone(ZoneType.Library);

        var mage = TributeMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        first.Zone.Should().Be(ZoneType.Hand,
            "deterministic fallback picks the first eligible candidate");
        second.Zone.Should().Be(ZoneType.Library, "second candidate is left untouched");
    }
}
