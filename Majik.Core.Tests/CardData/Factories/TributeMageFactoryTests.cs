using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TributeMageFactory"/> (Modern Horizons).
///
/// Sibling of TrinketMage (MV ≤ 1); Tribute Mage tutors an artifact
/// with mana value EXACTLY 2.
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB tutor: pulls a mana-value-2 artifact (Sword of the Meek) from
///   library → hand.
/// - ETB tutor: ignores mana-value-1 + mana-value-3 artifacts (strict
///   "== 2" predicate).
/// - ETB tutor: no eligible artifact → no-op.
/// - ETB tutor: no artifacts in library → no-op.
/// </summary>
[Trait("Color", "U")]
public class TributeMageFactoryTests
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
        c.HasSubtype(CardSubtype.Human).Should().BeTrue("Tribute Mage is a Human");
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Tribute Mage is a Wizard");
        c.ManaCost.Should().Be("{2}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // ETB tutor — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void TributeMage_EtbTrigger_PullsManaValueTwoArtifact_FromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        // Seed library: a non-artifact (filter check) + a mana-value-2
        // artifact (Sword of the Meek-style).
        var bait = new Card("Random Card", "");
        bait.SetOwner(alice);
        alice.Zones.Library.AddCard(bait);
        bait.SetZone(ZoneType.Library);

        var sword = new Artifact("Sword of the Meek", "2",
            subtypes: new[] { CardSubtype.Equipment });
        sword.SetOwner(alice);
        alice.Zones.Library.AddCard(sword);
        sword.SetZone(ZoneType.Library);

        var mage = TributeMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        sword.Zone.Should().Be(ZoneType.Hand,
            "ETB tutor pulled the mana-value-2 artifact to hand (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().Contain(sword);
        alice.Zones.Library.GetCards().Should().NotContain(sword);
        bait.Zone.Should().Be(ZoneType.Library,
            "the non-artifact card stays in the library (predicate-filtered)");
    }

    // -----------------------------------------------------------------------
    // ETB tutor — strict MV gate
    // -----------------------------------------------------------------------

    [Fact]
    public void TributeMage_EtbTrigger_IgnoresWrongManaValueArtifacts()
    {
        var alice = new Player("Alice", 20);

        // mv-1 artifact — not eligible (Tribute Mage is strictly MV == 2).
        var trinket = new Artifact("One-Cost Trinket", "1");
        trinket.SetOwner(alice);
        alice.Zones.Library.AddCard(trinket);
        trinket.SetZone(ZoneType.Library);

        // mv-3 artifact — not eligible.
        var big = new Artifact("Three-Cost Artifact", "3");
        big.SetOwner(alice);
        alice.Zones.Library.AddCard(big);
        big.SetZone(ZoneType.Library);

        var mage = TributeMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("no eligible artifact = CR 701.19a decline / no-op");
        trinket.Zone.Should().Be(ZoneType.Library,
            "mv-1 artifact stays in library (Tribute Mage is MV == 2)");
        big.Zone.Should().Be(ZoneType.Library,
            "mv-3 artifact stays in library (Tribute Mage is MV == 2)");
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no eligible artifact = nothing was tutored to hand");
    }

    [Fact]
    public void TributeMage_EtbTrigger_NoArtifactsInLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var unrelated = new Card("Random Card", "");
        unrelated.SetOwner(alice);
        alice.Zones.Library.AddCard(unrelated);
        unrelated.SetZone(ZoneType.Library);

        var mage = TributeMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("no artifact in library = CR 701.19a decline / no-op");
        unrelated.Zone.Should().Be(ZoneType.Library);
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
