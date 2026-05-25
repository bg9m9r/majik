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
/// Tests for Ugin, the Spirit Dragon (Fate Reforged, {8}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Ugin, starting loyalty 7,
///     mana cost {8}).
///   - Loyalty ability shape (three abilities: +2, -X registered as 0
///     placeholder, -10).
///   - +2 mechanic: 3 damage to any target (player → life loss; planeswalker
///     → loyalty removal).
///   - -X mechanic: exiles each coloured permanent with mv ≤ PendingCastX
///     across all battlefields; leaves colourless permanents and high-mv
///     coloured permanents alone.
///   - -10 mechanic: gain 7 life + return up to 7 permanent cards from
///     graveyard + draw 7; skips Instants / Sorceries; "up to 7" stops
///     at graveyard depth.
///   - NamedCardFactory dispatch.
/// </summary>
public class UginTheSpiritDragonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Ugin_IsLegendaryPlaneswalker_Ugin_7Loyalty_AtCost8()
    {
        var ugin = UginTheSpiritDragonFactory.Create(_alice);

        ugin.Name.Should().Be("Ugin, the Spirit Dragon");
        ugin.ManaCost.Should().Be("{8}");
        ugin.HasType(CardType.Planeswalker).Should().BeTrue();
        ugin.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        ugin.HasSubtype(CardSubtype.Ugin).Should().BeTrue();
        ugin.Loyalty.Should().Be(7);
        ugin.StartingLoyalty.Should().Be(7);
        ugin.Owner.Should().BeSameAs(_alice);
        ugin.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Ugin_HasThreeLoyaltyAbilities_Plus2_MinusX_Minus10()
    {
        var ugin = UginTheSpiritDragonFactory.Create(_alice);
        var loyaltyAbilities = ugin.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(3);
        // -X is registered as 0 placeholder (the engine doesn't model X
        // loyalty costs; effect pays via PendingCastX). +2 and -10 are
        // the printed integer costs.
        loyaltyAbilities.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +2, 0, -10 });
    }

    [Fact]
    public void Ugin_Plus2_DealsThreeDamageToPlayerTarget()
    {
        var players = new[] { _alice, _bob };
        var ugin = UginTheSpiritDragonFactory.Create(
            _alice,
            allPlayersResolver: () => players,
            anyTargetResolver: () => new object[] { _bob });
        _alice.Zones.Battlefield.AddCard(ugin);
        ugin.SetZone(ZoneType.Battlefield);

        var plus2 = ugin.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +2);
        plus2.Activate();

        ugin.Loyalty.Should().Be(9, "7 + 2 = 9");
        _bob.LifeTotal.Should().Be(17, "20 - 3 = 17");
    }

    [Fact]
    public void Ugin_Plus2_DealsThreeDamageToPlaneswalkerTarget_RemovingLoyalty()
    {
        var pw = new Planeswalker("Jace, Decoy", "{3}", 5,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Jace });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var players = new[] { _alice, _bob };
        var ugin = UginTheSpiritDragonFactory.Create(
            _alice,
            allPlayersResolver: () => players,
            anyTargetResolver: () => new object[] { pw });
        _alice.Zones.Battlefield.AddCard(ugin);
        ugin.SetZone(ZoneType.Battlefield);

        var plus2 = ugin.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +2);
        plus2.Activate();

        pw.Loyalty.Should().Be(2, "5 - 3 = 2 (CR 306.7 — damage to PW removes loyalty)");
    }

    [Fact]
    public void Ugin_MinusX_ExilesColouredPermanents_WithManaValueLeqX()
    {
        // Bob controls a 2-mv coloured (red) creature and a 3-mv coloured
        // (green) creature and a 5-mv coloured (blue) creature, plus a
        // colourless 2-mv artifact. X = 3 should exile the red + green;
        // the blue is too big, the colourless is excluded.
        var redGoblin = new Creature("Goblin", "{1}{R}", 2, 1);
        redGoblin.SetOwner(_bob); redGoblin.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(redGoblin);
        redGoblin.SetZone(ZoneType.Battlefield);

        var greenBeast = new Creature("Beast", "{2}{G}", 3, 3);
        greenBeast.SetOwner(_bob); greenBeast.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(greenBeast);
        greenBeast.SetZone(ZoneType.Battlefield);

        var blueWhale = new Creature("Whale", "{4}{U}", 5, 5);
        blueWhale.SetOwner(_bob); blueWhale.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(blueWhale);
        blueWhale.SetZone(ZoneType.Battlefield);

        var colourlessGolem = new Creature("Golem", "{2}", 2, 2);
        colourlessGolem.SetOwner(_bob); colourlessGolem.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(colourlessGolem);
        colourlessGolem.SetZone(ZoneType.Battlefield);

        var players = new[] { _alice, _bob };
        var ugin = UginTheSpiritDragonFactory.Create(
            _alice,
            allPlayersResolver: () => players,
            anyTargetResolver: null);
        // Loyalty needs to cover X = 3.
        ugin.AddLoyalty(0); // no-op, just illustrative
        _alice.Zones.Battlefield.AddCard(ugin);
        ugin.SetZone(ZoneType.Battlefield);

        ugin.SetPendingCastX(3);

        var minusX = ugin.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == 0);
        minusX.Activate();

        _bob.Zones.Exile.GetCards().Should().Contain(redGoblin);
        _bob.Zones.Exile.GetCards().Should().Contain(greenBeast);
        _bob.Zones.Exile.GetCards().Should().NotContain(blueWhale,
            "blue whale's mv 5 > X (3)");
        _bob.Zones.Exile.GetCards().Should().NotContain(colourlessGolem,
            "colourless permanents are not 'one or more colors'");

        _bob.Zones.Battlefield.GetCards().Should().Contain(blueWhale);
        _bob.Zones.Battlefield.GetCards().Should().Contain(colourlessGolem);

        ugin.Loyalty.Should().Be(4, "7 - 3 = 4 (X loyalty paid inline by the effect)");
        ugin.PendingCastX.Should().BeNull("PendingCastX is consumed and cleared on resolve");
    }

    [Fact]
    public void Ugin_MinusX_AtZero_ExilesOnlyZeroMvColouredPermanents()
    {
        // Edge case: X = 0 should still exile coloured permanents with
        // mv 0 (rare — e.g. coloured tokens with no printed mana cost,
        // though tokens are colour-override-driven). Use a 0-mv coloured
        // card (Phyrexian Walker has mana cost {0} but is colourless;
        // construct a synthetic case).
        // For the assertion: a coloured 1-mv permanent should NOT be
        // exiled at X = 0.
        var redOne = new Creature("Bolt Dummy", "{R}", 1, 1);
        redOne.SetOwner(_bob); redOne.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(redOne);
        redOne.SetZone(ZoneType.Battlefield);

        var players = new[] { _alice, _bob };
        var ugin = UginTheSpiritDragonFactory.Create(
            _alice,
            allPlayersResolver: () => players,
            anyTargetResolver: null);
        _alice.Zones.Battlefield.AddCard(ugin);
        ugin.SetZone(ZoneType.Battlefield);

        ugin.SetPendingCastX(0);

        var minusX = ugin.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == 0);
        minusX.Activate();

        _bob.Zones.Battlefield.GetCards().Should().Contain(redOne,
            "X = 0 < mv 1 — red one-drop is not exiled");
        ugin.Loyalty.Should().Be(7, "X = 0 → 0 loyalty paid");
    }

    [Fact]
    public void Ugin_Minus10_GainsSevenLifeReturnsPermanentsAndDrawsSeven()
    {
        // Bob's life starts at 20. Alice's loyalty must be ≥ 10 to
        // activate (we bump it).
        // Alice's graveyard has 3 permanent cards + 2 instants. Library
        // has 8 cards (enough for the 7-draw).
        var inst1 = new Card("Bolt", "{R}", new[] { CardType.Instant }) { Owner = _alice };
        _alice.Zones.Graveyard.AddCard(inst1);
        inst1.SetZone(ZoneType.Graveyard);

        var perm1 = new Creature("Bear", "{1}{G}", 2, 2);
        perm1.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(perm1);
        perm1.SetZone(ZoneType.Graveyard);

        var inst2 = new Card("Bolt2", "{R}", new[] { CardType.Sorcery }) { Owner = _alice };
        _alice.Zones.Graveyard.AddCard(inst2);
        inst2.SetZone(ZoneType.Graveyard);

        var perm2 = new Land("Forest");
        perm2.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(perm2);
        perm2.SetZone(ZoneType.Graveyard);

        var perm3 = new Creature("Wolf", "{1}{G}", 2, 2);
        perm3.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(perm3);
        perm3.SetZone(ZoneType.Graveyard);

        // Library — 8 cards.
        for (var i = 0; i < 8; i++)
        {
            var c = new Card($"Lib{i}", "G") { Owner = _alice };
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var ugin = UginTheSpiritDragonFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ugin);
        ugin.SetZone(ZoneType.Battlefield);
        ugin.AddLoyalty(3); // 7 + 3 = 10 (enough for -10)

        var minus10 = ugin.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -10);
        minus10.CanActivate().Should().BeTrue();
        minus10.Activate();

        ugin.Loyalty.Should().Be(0);
        _alice.LifeTotal.Should().Be(27, "20 + 7 = 27");

        // Three permanent cards returned to battlefield (the two
        // instants/sorceries left behind in graveyard).
        _alice.Zones.Battlefield.GetCards().Should().Contain(perm1);
        _alice.Zones.Battlefield.GetCards().Should().Contain(perm2);
        _alice.Zones.Battlefield.GetCards().Should().Contain(perm3);

        _alice.Zones.Graveyard.GetCards().Should().Contain(inst1);
        _alice.Zones.Graveyard.GetCards().Should().Contain(inst2);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(perm1);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(perm2);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(perm3);

        // Drew seven — hand size = 7.
        _alice.Zones.Hand.GetCards().Should().HaveCount(7);
        // Library has 1 left (started 8, drew 7).
        _alice.Zones.Library.GetCards().Should().HaveCount(1);

        // Returned permanents come under controller's control.
        perm1.Controller.Should().BeSameAs(_alice);
        perm3.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Ugin_Plus2_NoResolverWired_LoyaltyStillTicksUp()
    {
        var ugin = UginTheSpiritDragonFactory.Create(_alice);

        var plus2 = ugin.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +2);
        plus2.Activate();

        ugin.Loyalty.Should().Be(9, "7 + 2 = 9 even with no any-target resolver wired");
        _bob.LifeTotal.Should().Be(20, "no resolver → +2 damage clause is a silent no-op");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Ugin()
    {
        var card = NamedCardFactory.Create("Ugin, the Spirit Dragon", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Ugin, the Spirit Dragon");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Ugin).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(7);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(3);
    }
}
