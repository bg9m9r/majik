using System.Linq;
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
/// Unit tests for <see cref="SeverancePriestFactory"/>.
///
/// Severance Priest (Modern Horizons 3, {W}{B}{G}) — Creature — Djinn Cleric
/// 3/3. Oracle text (verified against Scryfall):
///   "Deathtouch
///    When this creature enters, target opponent reveals their hand. You may
///    choose a nonland card from it. If you do, exile that card.
///    When this creature leaves the battlefield, the exiled card's owner
///    creates an X/X white Spirit creature token, where X is the mana value
///    of the exiled card."
///
/// Shares the ETB-exile shape with <see cref="TidehollowScullerFactory"/>; the
/// LTB clause differs — instead of returning the exiled card, the exiled card's
/// OWNER mints an X/X white Spirit token sized by the exiled card's mana value
/// (CR 111 / CR 111.4 / CR 202.3). Base shape loads from the embedded JSON
/// (incl. the Deathtouch keyword); the two triggered abilities are layered on
/// in the factory because the JSON ability schema doesn't express exile / token
/// closures.
///
/// Covers:
/// - Identity (Creature — Djinn Cleric 3/3 at {W}{B}{G} + Deathtouch).
/// - Two triggered abilities (ETB exile + LTB token).
/// - ETB exiles a nonland card from a target opponent's hand; skips lands.
/// - LTB mints an X/X white Spirit for the exiled card's owner (X = mana value).
/// - LTB without an exiled card no-ops.
/// </summary>
[Trait("Color", "M")]
public class SeverancePriestFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SeverancePriest_Identity()
    {
        var c = SeverancePriestFactory.Create(_alice);

        c.Name.Should().Be("Severance Priest");
        c.ManaCost.Should().Be("{W}{B}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Djinn).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Deathtouch", "CR 702.2 — Deathtouch is printed");

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB token trigger");
    }

    [Fact]
    public void SeverancePriest_Etb_ExilesNonlandFromOpponentHand()
    {
        var priest = SeverancePriestFactory.Create(_alice);
        priest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(priest);

        var land = new Land("Swamp");
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(land);

        var spell = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        spell.SetOwner(_bob);
        spell.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(spell);

        var etb = priest.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();

        spell.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles a nonland card from the target opponent's hand (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(spell);
        _bob.Zones.Hand.GetCards().Should().Contain(land,
            "lands are skipped by the printed 'nonland' filter");
    }

    [Fact]
    public void SeverancePriest_Ltb_ExiledOwnerCreatesXxWhiteSpirit()
    {
        var priest = SeverancePriestFactory.Create(_alice);
        priest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(priest);

        // Mana value 2 ({1}{G}) → X = 2 → 2/2 white Spirit for Bob (its owner).
        var spell = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        spell.SetOwner(_bob);
        spell.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(spell);

        var etb = priest.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();
        spell.Zone.Should().Be(ZoneType.Exile);

        var ltb = priest.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        var token = _bob.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken && c.Name == "Spirit");

        token.BasePower.Should().Be(2,
            "X is the mana value of the exiled card (CR 202.3 — {1}{G} = 2)");
        token.BaseToughness.Should().Be(2);
        token.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        token.Controller.Should().BeSameAs(_bob,
            "the exiled card's OWNER creates the token (CR 111.4)");
        token.Owner.Should().BeSameAs(_bob);
    }

    [Fact]
    public void SeverancePriest_Ltb_WithoutExile_NoOp()
    {
        var priest = SeverancePriestFactory.Create(_alice);
        priest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(priest);

        var ltb = priest.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(priest);
    }
}
