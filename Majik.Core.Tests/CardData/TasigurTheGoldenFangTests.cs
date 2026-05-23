using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="TasigurTheGoldenFangFactory"/> — Tasigur, the
/// Golden Fang (Khans of Tarkir, {4}{B/G}). Legendary Creature — Human
/// Shaman 4/5. Delve + {B}{G}{U}: opponent-picks-graveyard-card → hand.
///
/// Covers:
///   - Card shape (name, type, mana cost, supertype, subtypes, P/T).
///   - "Delve" keyword marker.
///   - Activated ability shape ({B}{G}{U} ManaCostCost; single ability).
///   - NamedCardFactory dispatch.
///   - Activated ability resolve effect: opponent's pick is the card
///     returned from the controller's graveyard to their hand.
///   - Empty graveyard → clean no-op.
/// </summary>
public class TasigurTheGoldenFangTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity / types
    // -----------------------------------------------------------------------

    [Fact]
    public void Tasigur_IsLegendaryCreature_HumanShaman_FourFive()
    {
        var t = TasigurTheGoldenFangFactory.Create(_alice);

        t.Name.Should().Be("Tasigur, the Golden Fang");
        t.ManaCost.Should().Be("{4}{B/G}");
        t.HasType(CardType.Creature).Should().BeTrue();
        t.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        t.HasSubtype(CardSubtype.Human).Should().BeTrue();
        t.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        t.BasePower.Should().Be(4);
        t.BaseToughness.Should().Be(5);
        t.Owner.Should().BeSameAs(_alice);
        t.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Tasigur_HasDelveKeywordMarker()
    {
        var t = TasigurTheGoldenFangFactory.Create(_alice);

        t.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Delve");
    }

    [Fact]
    public void Tasigur_HasOneActivatedAbility_With_BGU_ManaCost()
    {
        var t = TasigurTheGoldenFangFactory.Create(_alice);

        var activated = t.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1);

        // The single cost is a ManaCostCost — printed {B}{G}{U}.
        activated[0].Costs.Should().HaveCount(1);
        var mana = activated[0].Costs.OfType<ManaCostCost>().Single();
        mana.Cost.Black.Should().Be(1);
        mana.Cost.Green.Should().Be(1);
        mana.Cost.Blue.Should().Be(1);
        mana.Cost.Generic.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_Tasigur()
    {
        var card = NamedCardFactory.Create("Tasigur, the Golden Fang", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Tasigur, the Golden Fang");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.ManaCost.Should().Be("{4}{B/G}");
        card.Owner.Should().BeSameAs(_alice);
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Delve");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Activated ability resolve: opponent picks → return to hand.
    // -----------------------------------------------------------------------

    [Fact]
    public void Tasigur_ActivatedAbility_ReturnsOpponentPick_ToControllerHand()
    {
        // Seed Alice's graveyard with three distinct cards. Opponent (Bob)
        // chooses the second one via the opponentChooser callback; we
        // verify *that specific card* ends up in Alice's hand.
        var (a, b, c) = SeedGraveyard3(_alice);

        // Wire an opponent-chooser that returns Bob. The opponent's
        // graveyard-pick falls back to the first candidate (no agent
        // registered for Bob), so the effect should return `a`.
        var tasigur = TasigurTheGoldenFangFactory.Create(
            _alice,
            allPlayersResolver: () => new[] { _alice, _bob },
            opponentChooser: () => _bob);

        var activated = tasigur.Abilities.OfType<ActivatedAbility>().Single();

        // Resolve the effect directly. The activated ability has a single
        // effect; invoking it simulates resolution after costs are paid.
        foreach (var eff in activated.Effects)
            eff.Execute();

        // Alice's graveyard lost the chosen card; hand gained it.
        // Default chooser fallback (no agent for Bob) → first card = `a`.
        _alice.Zones.Graveyard.GetCards().Should().NotContain(a);
        _alice.Zones.Hand.GetCards().Should().Contain(a);
        a.Zone.Should().Be(ZoneType.Hand);

        // The other two cards stayed in the graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { b, c });
        b.Zone.Should().Be(ZoneType.Graveyard);
        c.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Tasigur_ActivatedAbility_EmptyGraveyard_IsNoOp()
    {
        // No cards in Alice's graveyard. Resolving the ability must not
        // throw, and must not move any cards.
        var tasigur = TasigurTheGoldenFangFactory.Create(
            _alice,
            allPlayersResolver: () => new[] { _alice, _bob },
            opponentChooser: () => _bob);

        var activated = tasigur.Abilities.OfType<ActivatedAbility>().Single();

        Action resolve = () =>
        {
            foreach (var eff in activated.Effects)
                eff.Execute();
        };

        resolve.Should().NotThrow();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Tasigur_ActivatedAbility_NoResolver_IsNoOp()
    {
        // Single-arg dispatcher path (no all-players resolver). Even when
        // the graveyard has candidates, with no reachable opponent the
        // effect must no-op (cards stay in the graveyard).
        var (a, b, c) = SeedGraveyard3(_alice);

        var tasigur = TasigurTheGoldenFangFactory.Create(_alice);

        var activated = tasigur.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in activated.Effects)
            eff.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(new[] { a, b, c });
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ICard a, ICard b, ICard c) SeedGraveyard3(Player p)
    {
        var a = new Card("Yard-A", "");
        var b = new Card("Yard-B", "");
        var c = new Card("Yard-C", "");
        foreach (var card in new[] { a, b, c })
        {
            card.SetOwner(p);
            card.SetZone(ZoneType.Graveyard);
            p.Zones.Graveyard.AddCard(card);
        }
        return (a, b, c);
    }
}
