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
/// Tests for <see cref="GlintNestCraneFactory"/> ({1}{U}, Creature — Bird 1/3).
///
/// Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, look at the top four cards of your library.
///    You may reveal an artifact card from among them and put it into your
///    hand. Put the rest on the bottom of your library in any order."
///
/// Covers:
/// - Identity: name, mana cost, type, subtype (Bird), P/T 1/3, color (blue),
///   owner/controller.
/// - Flying keyword marker (CR 702.9).
/// - <see cref="NamedCardFactory"/> dispatcher entry.
/// - Exactly one ETB <see cref="TriggeredAbility"/> active on the Battlefield.
/// - ETB: artifact in top 4 → goes to hand; the other three go to bottom.
/// - ETB: no artifact in top 4 → nothing to hand; all four go to bottom.
/// - ETB: "may" decline (picker returns null) → nothing to hand; all to bottom.
/// - ETB: short library (fewer than 4 cards) — no throw, partial peek.
/// - ETB: empty library — clean no-op.
/// </summary>
public class GlintNestCraneFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void SeedLibrary(Player p, params ICard[] cards)
    {
        foreach (var c in cards)
        {
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    private static void FireEtb(Creature card)
    {
        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();
    }

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Identity_Name_ManaCost_Type_Subtype_PT()
    {
        var card = GlintNestCraneFactory.Create(_alice);

        card.Name.Should().Be("Glint-Nest Crane");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);

        var creature = card.Should().BeOfType<Creature>().Subject;
        creature.BasePower.Should().Be(1);
        creature.BaseToughness.Should().Be(3);
    }

    [Fact]
    public void Identity_IsBlue()
    {
        var card = GlintNestCraneFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Blue,
            "Glint-Nest Crane has {U} in its mana cost");
    }

    [Fact]
    public void Card_HasFlyingKeyword()
    {
        var card = GlintNestCraneFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "Glint-Nest Crane has Flying (CR 702.9)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GlintNestCrane()
    {
        var card = NamedCardFactory.Create("Glint-Nest Crane", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Glint-Nest Crane");
        card.HasSubtype(CardSubtype.Bird).Should().BeTrue();

        var creature = (Creature)card;
        creature.BasePower.Should().Be(1);
        creature.BaseToughness.Should().Be(3);
    }

    [Fact]
    public void Card_HasExactlyOneEtbTriggeredAbility_ActiveOnBattlefield()
    {
        var card = GlintNestCraneFactory.Create(_alice);

        var etb = card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Glint-Nest Crane has one triggered ability — the ETB look-at-top-4 clause.")
            .And.Subject.Single();

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "the ETB trigger is only active while the permanent is on the Battlefield");
    }

    // ── ETB resolution ───────────────────────────────────────────────────────

    [Fact]
    public void Etb_ArtifactInTopFour_GoesToHand_OthersGoToBottom()
    {
        // Library top → bottom:
        //   creature (ineligible)
        //   artifact (eligible — first eligible artifact)
        //   land     (ineligible)
        //   instant  (ineligible)
        //   deep     (below look window — untouched)
        var creature = new Creature("Bear", "{1}{G}", 2, 2);
        var artifact = new Artifact("Ornithopter", "{0}");
        var land = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        var instant = new Instant("Lightning Bolt", "{R}");
        var deep = new Sorcery("Deep", "{B}");

        SeedLibrary(_alice, creature, artifact, land, instant, deep);

        GlintNestCraneFactory.Result? result = null;
        var card = GlintNestCraneFactory.Create(_alice, triggers: null, choosePick: null, onEtbResolved: r => result = r);

        FireEtb(card);

        result.Should().NotBeNull();
        result!.Peeked.Should().HaveCount(4, "only the top four were looked at");
        result.Eligible.Should().ContainSingle().Which.Should().BeSameAs(artifact);
        result.Picked.Should().BeSameAs(artifact);

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(artifact);
        artifact.Zone.Should().Be(ZoneType.Hand);

        // The other three peeked cards go to the bottom; deep stays above them.
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(4, "creature + land + instant rebottomed; deep untouched");
        lib.Should().NotContain(artifact);
        lib[0].Should().BeSameAs(deep, "deep was below the look window and untouched on top");
    }

    [Fact]
    public void Etb_NoArtifactInTopFour_NothingToHand_AllToBottom()
    {
        var creature = new Creature("Bear", "{1}{G}", 2, 2);
        var land = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        var instant = new Instant("Lightning Bolt", "{R}");
        var sorcery = new Sorcery("Divination", "{2}{U}");

        SeedLibrary(_alice, creature, land, instant, sorcery);

        GlintNestCraneFactory.Result? result = null;
        var card = GlintNestCraneFactory.Create(_alice, triggers: null, choosePick: null, onEtbResolved: r => result = r);

        FireEtb(card);

        result.Should().NotBeNull();
        result!.Eligible.Should().BeEmpty("no artifact among the top four");
        result.Picked.Should().BeNull();

        _alice.Zones.Hand.GetCards().Should().BeEmpty("no eligible artifact → nothing to hand");
        _alice.Zones.Library.GetCards().Should().HaveCount(4, "all four go to the bottom");
    }

    [Fact]
    public void Etb_MayDeclined_PickerReturnsNull_NothingToHand_AllToBottom()
    {
        var artifact = new Artifact("Ornithopter", "{0}");
        var creature = new Creature("Bear", "{1}{G}", 2, 2);

        SeedLibrary(_alice, artifact, creature);

        GlintNestCraneFactory.Result? result = null;
        var card = GlintNestCraneFactory.Create(
            _alice,
            triggers: null,
            choosePick: _ => null, // decline the "may"
            onEtbResolved: r => result = r);

        FireEtb(card);

        result.Should().NotBeNull();
        result!.Eligible.Should().ContainSingle().Which.Should().BeSameAs(artifact);
        result.Picked.Should().BeNull("the controller declined the optional reveal");

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().HaveCount(2, "both go to the bottom");
    }

    [Fact]
    public void Etb_PickerChoosesArtifact_GoesToHand()
    {
        var artifact = new Artifact("Ornithopter", "{0}");
        var creature = new Creature("Bear", "{1}{G}", 2, 2);

        SeedLibrary(_alice, creature, artifact);

        var card = GlintNestCraneFactory.Create(
            _alice,
            triggers: null,
            choosePick: eligible => eligible[0],
            onEtbResolved: null);

        FireEtb(card);

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(artifact);
    }

    [Fact]
    public void Etb_ShortLibrary_FewerThanFour_NoThrow_PartialPeek()
    {
        var artifact = new Artifact("Ornithopter", "{0}");
        var creature = new Creature("Bear", "{1}{G}", 2, 2);

        SeedLibrary(_alice, creature, artifact);

        GlintNestCraneFactory.Result? result = null;
        var card = GlintNestCraneFactory.Create(_alice, triggers: null, choosePick: null, onEtbResolved: r => result = r);

        var act = () => FireEtb(card);
        act.Should().NotThrow("a short library is a graceful partial peek");

        result!.Peeked.Should().HaveCount(2);
        result.Picked.Should().BeSameAs(artifact);
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(artifact);
    }

    [Fact]
    public void Etb_EmptyLibrary_CleanNoOp()
    {
        // Library intentionally empty.
        GlintNestCraneFactory.Result? result = null;
        var card = GlintNestCraneFactory.Create(_alice, triggers: null, choosePick: null, onEtbResolved: r => result = r);

        var act = () => FireEtb(card);
        act.Should().NotThrow("empty library is a valid no-op");

        result!.Peeked.Should().BeEmpty();
        result.Picked.Should().BeNull();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
