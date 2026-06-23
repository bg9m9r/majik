using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BrightglassGearhulkFactory"/>.
///
/// Brightglass Gearhulk — Artifact Creature — Construct {G}{G}{W}{W} 4/4.
/// Oracle: "First strike, trample. When this creature enters, you may search
/// your library for up to two artifact, creature, and/or enchantment cards
/// with mana value 1 or less, reveal them, put them into your hand, then
/// shuffle."
///
/// Covers ONLY the card's unique behaviour (the ETB tutor) plus a single
/// identity assert for the non-vanilla stats/types. Dispatch + well-formedness
/// are covered for every implemented card by CardFactoryContractTests.
/// </summary>
[Trait("Color", "M")] // {G}{G}{W}{W} — multicolour (Selesnya).
public class BrightglassGearhulkFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity (non-vanilla stats / dual type / subtype / keywords)
    // -----------------------------------------------------------------------

    [Fact]
    public void BrightglassGearhulk_Identity()
    {
        var alice = new Player("Alice", 20);
        var c = BrightglassGearhulkFactory.Create(alice);

        c.Name.Should().Be("Brightglass Gearhulk");
        c.HasType(CardType.Creature).Should().BeTrue("it is a creature");
        c.HasType(CardType.Artifact).Should().BeTrue("it is an Artifact Creature");
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.ManaCost.Should().Be("{G}{G}{W}{W}");
        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("First strike", "First strike is printed (CR 702.7)");
        keywords.Should().Contain("Trample", "Trample is printed (CR 702.19)");
        c.Owner.Should().BeSameAs(alice);
        c.Controller.Should().BeSameAs(alice);
    }

    // -----------------------------------------------------------------------
    // ETB tutor — up to TWO eligible cards pulled to hand
    // -----------------------------------------------------------------------

    [Fact]
    public void BrightglassGearhulk_EtbTrigger_PullsUpToTwoEligibleCards_FromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        // Three eligible cards (artifact mv0, creature mv1, enchantment mv1).
        // Only the FIRST TWO should be tutored — "up to two".
        var moxOpal = Seed(alice, new Artifact("Mox Opal", "0"));
        var oneDrop = Seed(alice, new Creature("Tiny Beast", "{G}", 1, 1));
        var aura = Seed(alice, new Enchantment("Cheap Aura", "{W}"));

        Resolve(alice);

        // Exactly two cards moved to hand (CR 701.19a — "up to two").
        alice.Zones.Hand.GetCards().Should().HaveCount(2,
            "the ETB tutors up to two eligible cards");
        alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { moxOpal, oneDrop });
        aura.Zone.Should().Be(ZoneType.Library,
            "only the first two eligible cards are taken — the third stays in library");
    }

    [Fact]
    public void BrightglassGearhulk_EtbTrigger_AcceptsArtifactCreatureAndEnchantment_MvOneOrLess()
    {
        var alice = new Player("Alice", 20);

        // Exactly two eligible: a mv-1 enchantment and a mv-0 artifact.
        var aura = Seed(alice, new Enchantment("Cheap Aura", "{W}"));
        var mox = Seed(alice, new Artifact("Mox Opal", "0"));

        Resolve(alice);

        aura.Zone.Should().Be(ZoneType.Hand, "mv-1 enchantment is eligible");
        mox.Zone.Should().Be(ZoneType.Hand, "mv-0 artifact is eligible");
    }

    // -----------------------------------------------------------------------
    // ETB tutor — ineligibility filters (mana value & card type)
    // -----------------------------------------------------------------------

    [Fact]
    public void BrightglassGearhulk_EtbTrigger_SkipsManaValueTwoOrMore()
    {
        var alice = new Player("Alice", 20);

        var bigArtifact = Seed(alice, new Artifact("Big Artifact", "2"));
        var bigCreature = Seed(alice, new Creature("Big Beast", "{2}{G}", 3, 3));

        Resolve(alice);

        bigArtifact.Zone.Should().Be(ZoneType.Library, "mv-2 is not <= 1");
        bigCreature.Zone.Should().Be(ZoneType.Library, "mv-3 is not <= 1");
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void BrightglassGearhulk_EtbTrigger_SkipsIneligibleCardTypes()
    {
        var alice = new Player("Alice", 20);

        // A land (mv 0) and a sorcery (mv 1) are NOT artifact/creature/enchantment.
        var land = Seed(alice, new Land("Mountain"));
        var bolt = Seed(alice, new Sorcery("Cheap Sorcery", "{R}"));

        Resolve(alice);

        land.Zone.Should().Be(ZoneType.Library, "a land is not an eligible type");
        bolt.Zone.Should().Be(ZoneType.Library, "a sorcery is not an eligible type");
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void BrightglassGearhulk_EtbTrigger_NoEligibleCards_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        Seed(alice, new Land("Forest"));

        var act = () => Resolve(alice);

        act.Should().NotThrow("an empty/ineligible search is a legal no-op (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static T Seed<T>(Player owner, T card) where T : Card
    {
        card.SetOwner(owner);
        owner.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }

    private static void Resolve(Player owner)
    {
        var hulk = BrightglassGearhulkFactory.Create(owner);
        var etb = hulk.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();
    }
}
