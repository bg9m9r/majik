using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AtraxaGrandUnifierFactory"/>.
///
/// Covers:
/// - Identity (name, Legendary Creature — Phyrexian Angel, 7/7, mana cost,
///   owner/controller).
/// - Four evergreen keyword markers (Flying, Vigilance, Deathtouch, Lifelink).
/// - NamedCardFactory dispatch.
/// - ETB reveal-and-pick:
///     * one of each card type from a 10-card top with distinct types,
///     * duplicates collapse so each type slot is filled at most once,
///     * fewer than 10 cards in library still resolves on what's there.
/// </summary>
public class AtraxaGrandUnifierTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void AtraxaGrandUnifier_Identity()
    {
        var c = AtraxaGrandUnifierFactory.Create(_alice);

        c.Name.Should().Be("Atraxa, Grand Unifier");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Atraxa, Grand Unifier is a Legendary Creature");
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.BasePower.Should().Be(7);
        c.BaseToughness.Should().Be(7);
        c.ManaCost.Should().Be("{3}{W}{U}{B}{R}{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AtraxaGrandUnifier_HasFlyingVigilanceDeathtouchLifelink()
    {
        var c = AtraxaGrandUnifierFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying", "CR 702.9 — printed evergreen");
        keywords.Should().Contain("Vigilance", "CR 702.20 — printed evergreen");
        keywords.Should().Contain("Deathtouch", "CR 702.2 — printed evergreen");
        keywords.Should().Contain("Lifelink", "CR 702.15 — printed evergreen");

        CombatAbilities.HasDeathtouch(c).Should().BeTrue();
        CombatAbilities.HasLifelink(c).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_DispatchesAtraxa()
    {
        var c = NamedCardFactory.Create("Atraxa, Grand Unifier", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Atraxa, Grand Unifier");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
    }

    [Fact]
    public void AtraxaGrandUnifier_HasEtbTrigger()
    {
        var c = AtraxaGrandUnifierFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Atraxa has a single ETB triggered ability");
    }

    // -----------------------------------------------------------------------
    // ETB resolution — reveal top 10, pick one per card type
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_OneOfEachType_AllTakenToHand()
    {
        var alice = new Player("Alice", 20);

        var artifact     = new Card("Sol Ring", "", new[] { CardType.Artifact });
        var creature     = new Creature("Grizzly Bears", "1G", 2, 2);
        var enchantment  = new Enchantment("Sythis's Sanctum", "1G");
        var instant      = new Instant("Lightning Bolt", "R");
        var land         = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        var planeswalker = new Card("Jace, the Mind Sculptor", "2UU", new[] { CardType.Planeswalker });
        var sorcery      = new Sorcery("Wrath of God", "2WW");

        // Pad to 10 with extras that share types already taken — they
        // must go to the bottom of the library.
        var extra1 = new Creature("Hill Giant", "3R", 3, 3);
        var extra2 = new Creature("Centaur Courser", "2G", 3, 3);
        var extra3 = new Sorcery("Divination", "2U");

        var topTen = new ICard[]
        {
            artifact, creature, enchantment, instant,
            land, planeswalker, sorcery,
            extra1, extra2, extra3,
        };
        foreach (var card in topTen)
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        AtraxaGrandUnifierFactory.ResolveEtb(alice);

        var hand = alice.Zones.Hand.GetCards().ToList();
        hand.Should().Contain(new ICard[]
        {
            artifact, creature, enchantment, instant,
            land, planeswalker, sorcery,
        }, "one card of each card type goes to hand");
        hand.Should().HaveCount(7,
            "exactly seven picks (no Battle type in the engine yet; Tribal has no candidate)");

        // The three extras must have been re-bottomed (still in library).
        var library = alice.Zones.Library.GetCards().ToList();
        library.Should().Contain(new ICard[] { extra1, extra2, extra3 });
        library.Should().HaveCount(3);
    }

    [Fact]
    public void Etb_DuplicatesCollapse_OneOfEachTypeOnly()
    {
        var alice = new Player("Alice", 20);

        // Three creatures, two lands, two sorceries, three instants —
        // total 10 cards across four card types only. The picks should
        // be exactly one creature + one land + one sorcery + one instant
        // = 4 cards, and the remaining 6 go to the bottom.
        var creature1 = new Creature("Bear A", "1G", 2, 2);
        var creature2 = new Creature("Bear B", "1G", 2, 2);
        var creature3 = new Creature("Bear C", "1G", 2, 2);
        var land1     = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        var land2     = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        var sorcery1  = new Sorcery("Sorc A", "2U");
        var sorcery2  = new Sorcery("Sorc B", "2U");
        var instant1  = new Instant("Ins A", "U");
        var instant2  = new Instant("Ins B", "U");
        var instant3  = new Instant("Ins C", "U");

        var topTen = new ICard[]
        {
            creature1, creature2, creature3, land1, land2,
            sorcery1, sorcery2, instant1, instant2, instant3,
        };
        foreach (var card in topTen)
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        AtraxaGrandUnifierFactory.ResolveEtb(alice);

        var hand = alice.Zones.Hand.GetCards().ToList();
        hand.Should().HaveCount(4,
            "four distinct card types present → four picks, no second pick per type");
        hand.Should().Contain(creature1, "first matching creature is taken");
        hand.Should().Contain(land1, "first matching land is taken");
        hand.Should().Contain(sorcery1, "first matching sorcery is taken");
        hand.Should().Contain(instant1, "first matching instant is taken");

        var library = alice.Zones.Library.GetCards().ToList();
        library.Should().HaveCount(6, "the six unselected cards re-bottom");
        library.Should().Contain(new ICard[]
        {
            creature2, creature3, land2, sorcery2, instant2, instant3,
        });
    }

    [Fact]
    public void Etb_FewerThanTenInLibrary_WorksOnWhatsAvailable()
    {
        var alice = new Player("Alice", 20);

        var artifact = new Card("Sol Ring", "", new[] { CardType.Artifact });
        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        var instant  = new Instant("Lightning Bolt", "R");

        foreach (var card in new ICard[] { artifact, creature, instant })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        AtraxaGrandUnifierFactory.ResolveEtb(alice);

        var hand = alice.Zones.Hand.GetCards().ToList();
        hand.Should().BeEquivalentTo(new ICard[] { artifact, creature, instant },
            "all three cards have distinct types — all three go to hand");
        alice.Zones.Library.Count.Should().Be(0, "no leftovers when every peeked card is picked");
    }

    [Fact]
    public void Etb_EmptyLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        AtraxaGrandUnifierFactory.ResolveEtb(alice);

        alice.Zones.Hand.Count.Should().Be(0);
        alice.Zones.Library.Count.Should().Be(0);
    }

    [Fact]
    public void SelectOnePerCardType_ArtifactCreature_TakenOnce()
    {
        // A single Artifact Creature satisfies both the Artifact and the
        // Creature type slots, but the selector must only take it once —
        // "one card of each card type from among them" means a card
        // claims one slot, not two.
        var artifactCreature = new Creature("Walking Ballista", "XX", 0, 0);
        artifactCreature.AddCardType(CardType.Artifact);

        var picks = AtraxaGrandUnifierFactory.SelectOnePerCardType(
            new ICard[] { artifactCreature });

        picks.Should().HaveCount(1, "the multi-type card claims one slot only");
        picks.Should().Contain(artifactCreature);
    }
}
