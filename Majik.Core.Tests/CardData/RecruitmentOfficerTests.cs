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
/// Tests for Recruitment Officer (Modern Horizons 3, {W}, Creature —
/// Human Soldier 1/1).
///
/// Covers:
///   - Card identity (Creature, {W}, 1/1, Human + Soldier subtypes).
///   - NamedCardFactory dispatch.
///   - "Can block as if it had reach" via KeywordAbility("Reach") so
///     CombatAbilities.CanBlockFlying returns true.
///   - {2}{W} activated ability shape (cost + effects attached).
///   - Look-6 resolution: picks first eligible creature with mv ≤ 2,
///     puts the rest on the bottom.
///   - Eligibility predicate: creature + mana value ≤ 2.
///   - Empty / short library is a clean no-op.
/// </summary>
public class RecruitmentOfficerTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RecruitmentOfficer_Identity()
    {
        var c = RecruitmentOfficerFactory.Create(_alice);

        c.Name.Should().Be("Recruitment Officer");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RecruitmentOfficer()
    {
        var card = NamedCardFactory.Create("Recruitment Officer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Recruitment Officer");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Reach rider
    // -----------------------------------------------------------------------

    [Fact]
    public void RecruitmentOfficer_CanBlockFlying_ViaReachRider()
    {
        var c = RecruitmentOfficerFactory.Create(_alice);

        CombatAbilities.HasReach(c).Should().BeTrue();
        CombatAbilities.CanBlockFlying(c).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void RecruitmentOfficer_HasActivatedAbility_AtTwoWhite()
    {
        var c = RecruitmentOfficerFactory.Create(_alice);

        var activated = c.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1);
        // The cost set should mention {2}{W}.
        var desc = string.Join(",", activated[0].Costs.Select(x => x.Description ?? ""));
        desc.Should().Contain("2").And.Contain("W");
    }

    // -----------------------------------------------------------------------
    // Eligibility predicate
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("{W}", true)]       // 1-mv creature
    [InlineData("{1}{W}", true)]    // 2-mv creature
    [InlineData("{2}{W}", false)]   // 3-mv creature (over cap)
    [InlineData("{W}{W}{W}", false)]
    public void IsEligible_Creature_RespectsManaValueCap(string cost, bool expected)
    {
        var card = MakeCreature("Sample", cost);
        RecruitmentOfficerFactory.IsEligible(card).Should().Be(expected);
    }

    [Fact]
    public void IsEligible_NonCreature_Rejected()
    {
        var bolt = new Card("Lightning Bolt", "{R}");
        bolt.AddCardType(CardType.Instant);
        RecruitmentOfficerFactory.IsEligible(bolt).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_PicksFirstEligibleCreature_RestToBottom()
    {
        // 6 top cards: bolt, eligible creature, expensive creature, land,
        // another eligible creature (should be ignored — first wins), bolt.
        var top = new List<ICard>
        {
            SeedLibrary(_alice, MakeNoncreature("Lightning Bolt", "{R}", CardType.Instant)),
            SeedLibrary(_alice, MakeCreature("Llanowar Elves", "{G}")),
            SeedLibrary(_alice, MakeCreature("Tarmogoyf", "{1}{G}{G}")), // mv 3 — over cap
            SeedLibrary(_alice, MakeNoncreature("Plains", "", CardType.Land)),
            SeedLibrary(_alice, MakeCreature("Soldier Token", "{W}")),
            SeedLibrary(_alice, MakeNoncreature("Counterspell", "{U}{U}", CardType.Instant)),
        };
        var elves = top[1];

        var effects = RecruitmentOfficerFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
        _alice.Zones.Hand.GetCards().Should().Contain(elves);

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(5);
        lib.Should().NotContain(elves);
        // The other 5 peeked cards must all be in the library.
        lib.Should().Contain(new[] { top[0], top[2], top[3], top[4], top[5] });
    }

    [Fact]
    public void Resolve_NoEligibleCreature_AllSixToBottom()
    {
        // No creature with mv ≤ 2 in the top 6.
        var top = new List<ICard>
        {
            SeedLibrary(_alice, MakeCreature("Tarmogoyf", "{1}{G}{G}")),  // mv 3
            SeedLibrary(_alice, MakeCreature("Reya Dawnbringer", "{6}{W}{W}")),
            SeedLibrary(_alice, MakeNoncreature("Plains", "", CardType.Land)),
            SeedLibrary(_alice, MakeNoncreature("Counterspell", "{U}{U}", CardType.Instant)),
            SeedLibrary(_alice, MakeNoncreature("Lightning Bolt", "{R}", CardType.Instant)),
            SeedLibrary(_alice, MakeNoncreature("Sol Ring", "{1}", CardType.Artifact)),
        };

        var effects = RecruitmentOfficerFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(6);
        lib.Should().Contain(top);
    }

    [Fact]
    public void Resolve_LibraryWithFewerThanSix_WorksOnWhatsAvailable()
    {
        var elves = SeedLibrary(_alice, MakeCreature("Llanowar Elves", "{G}"));
        var bolt = SeedLibrary(_alice, MakeNoncreature("Lightning Bolt", "{R}", CardType.Instant));

        var effects = RecruitmentOfficerFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(elves);
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(1);
        lib.Should().Contain(bolt);
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoOp()
    {
        var effects = RecruitmentOfficerFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void DefaultPickSelector_PicksFirstEligible()
    {
        var peeked = new List<ICard>
        {
            MakeCreature("Tarmogoyf", "{1}{G}{G}"),      // over cap
            MakeNoncreature("Bolt", "{R}", CardType.Instant),
            MakeCreature("Llanowar Elves", "{G}"),       // first eligible
            MakeCreature("Phyrexian Dreadnought", "{1}"), // also eligible (mv 1) — ignored
        };

        var (toHand, toBottom) = RecruitmentOfficerFactory.DefaultPickSelector(peeked);

        toHand.Should().HaveCount(1);
        toHand[0].Name.Should().Be("Llanowar Elves");
        toBottom.Should().HaveCount(3);
        toBottom.Should().Contain(c => c.Name == "Tarmogoyf");
        toBottom.Should().Contain(c => c.Name == "Bolt");
        toBottom.Should().Contain(c => c.Name == "Phyrexian Dreadnought");
        toBottom.Should().NotContain(c => c.Name == "Llanowar Elves");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ICard SeedLibrary(Player p, ICard card)
    {
        card.SetOwner(p);
        card.SetZone(ZoneType.Library);
        p.Zones.Library.AddCard(card);
        return card;
    }

    private static Card MakeCreature(string name, string manaCost)
    {
        var c = new Card(name, manaCost);
        c.AddCardType(CardType.Creature);
        return c;
    }

    private static Card MakeNoncreature(string name, string manaCost, CardType type)
    {
        var c = new Card(name, manaCost);
        c.AddCardType(type);
        return c;
    }
}
