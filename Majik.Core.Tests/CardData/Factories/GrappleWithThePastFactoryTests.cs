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
/// Tests for Grapple with the Past (Eldritch Moon, {1}{G}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Mill three cards, then you may return a creature or land card from your
///    graveyard to your hand."
///
/// Card shape comes from the embedded JSON (<c>grapple-with-the-past.json</c>);
/// the resolve body (mill 3, then may return a creature/land from the whole
/// graveyard to hand) is the same core the live cast uses via
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Bespoke.GrappleWithThePastPatternTemplate"/>.
///
/// Covers the card's UNIQUE behaviour:
///   - Mill exactly 3 from the library into the graveyard.
///   - A milled (or pre-existing) creature OR land in the graveyard may be
///     returned to hand; the just-milled cards are eligible.
///   - Non-creature/non-land cards are ineligible (stay in the graveyard).
///   - "you may" decline is a no-op (the card stays) but still mills.
///   - Empty library + empty graveyard → clean no-op (no throw).
/// Plus one identity assert for the exact mana cost / Instant type.
/// </summary>
[Trait("Color", "G")]
public class GrappleWithThePastFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Grapple_HasInstantShape_AtCost1G()
    {
        var card = GrappleWithThePastFactory.Create(_alice);

        card.Name.Should().Be("Grapple with the Past");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // ── Resolve: mill 3 / may return creature-or-land / rest stay ─────────────

    [Fact]
    public void Resolve_MillsExactlyThree_FromLibrary()
    {
        SeedLibrary(_alice, 6);

        Resolve(returnSelector: _ => null);

        _alice.Zones.Library.GetCards().Should().HaveCount(3);
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(3);
    }

    [Fact]
    public void Resolve_ReturnsSelectedCreature_ToHand_FromGraveyard()
    {
        var beast = NewCreature("Beast");
        var instant = NewCard("Bolt", CardType.Instant);
        PutInGraveyard(_alice, beast);
        PutInGraveyard(_alice, instant);

        // Empty library so the mill adds nothing new.
        Resolve(returnSelector: c => c.First(x => ReferenceEquals(x, beast)));

        _alice.Zones.Hand.GetCards().Should().Contain(beast);
        beast.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(beast);
        // Ineligible card stays.
        _alice.Zones.Graveyard.GetCards().Should().Contain(instant);
    }

    [Fact]
    public void Resolve_LandCard_IsEligible_GoesToHand()
    {
        var forest = NewLand("Forest");
        PutInGraveyard(_alice, forest);

        GrappleWithThePastFactory.IsEligibleReturn(forest).Should().BeTrue();

        Resolve(returnSelector: c => c.Count > 0 ? c[0] : null);

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(forest, "a land card is a legal pick (creature OR land)");
        forest.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_JustMilledCreature_IsEligible_ToReturn()
    {
        // Top of library is a creature: it gets milled into the graveyard and
        // is then itself eligible to be returned (mill happens first).
        var creature = NewCreature("Milled-Beast");
        creature.SetOwner(_alice);
        creature.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(creature);

        Resolve(returnSelector: c => c.FirstOrDefault());

        _alice.Zones.Hand.GetCards().Should().Contain(creature);
        creature.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_NonCreatureNonLand_IsNotEligible()
    {
        var sorcery = NewCard("Some-Sorcery", CardType.Sorcery);
        PutInGraveyard(_alice, sorcery);

        GrappleWithThePastFactory.IsEligibleReturn(sorcery).Should().BeFalse();

        // Selector would accept the first candidate, but there are none.
        Resolve(returnSelector: c => c.Count > 0 ? c[0] : null);

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(sorcery);
    }

    [Fact]
    public void Resolve_DeclineToReturn_IsNoOp_ButStillMills()
    {
        var beast = NewCreature("Beast");
        PutInGraveyard(_alice, beast);
        SeedLibrary(_alice, 4);

        Resolve(returnSelector: _ => null);

        // Declined — Beast stays in the graveyard.
        _alice.Zones.Hand.GetCards().Should().NotContain(beast);
        _alice.Zones.Graveyard.GetCards().Should().Contain(beast);
        // Still milled three.
        _alice.Zones.Library.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void Resolve_EmptyLibraryAndGraveyard_IsCleanNoOp()
    {
        Action resolve = () => Resolve(returnSelector: c => c.Count > 0 ? c[0] : null);

        resolve.Should().NotThrow("empty library + graveyard is a clean no-op");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Resolve(Func<IReadOnlyList<ICard>, ICard?> returnSelector)
    {
        foreach (var e in GrappleWithThePastFactory.BuildResolveEffect(_alice, returnSelector))
            e.Execute();
    }

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var card = new Card($"Lib-{i}", "");
            card.SetOwner(p);
            card.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(card);
        }
    }

    private static Creature NewCreature(string name)
        => new(name, "{1}{G}", 2, 2);

    private static Land NewLand(string name)
        => new(name, new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });

    private static Card NewCard(string name, CardType type)
    {
        var c = new Card(name, "{1}");
        c.AddCardType(type);
        return c;
    }

    private static void PutInGraveyard(Player p, ICard card)
    {
        card.SetOwner(p);
        card.SetZone(ZoneType.Graveyard);
        p.Zones.Graveyard.AddCard(card);
    }
}
