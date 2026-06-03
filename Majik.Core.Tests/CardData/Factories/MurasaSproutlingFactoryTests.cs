using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Murasa Sproutling (Modern Horizons 3, {2}{G}).
///
/// Creature — Plant Elemental 3/3. Oracle text:
///   "Kicker {1}{G} (You may pay an additional {1}{G} as you cast this spell.)
///    When this creature enters, if it was kicked, return target card with a
///    kicker ability from your graveyard to your hand."
///
/// Covers:
///   - Card shape (name, types, subtypes, P/T, mana cost) + Kicker marker.
///   - BuildAdditionalCost shape (Kicker {1}{G}).
///   - ETB trigger structure (a 1..1 target request for a "card with a kicker
///     ability" in controller's graveyard, scoped to battlefield).
///   - Intervening-if (CR 603.4): NOT kicked → ETB is a clean no-op even when
///     a legal candidate exists in the graveyard.
///   - Kicked + agent-set target: that specific kicker card is returned.
///   - Kicked + fallback: first kicker card in graveyard returned.
///   - Candidate filter: only cards with a kicker ability are legal candidates.
///   - Empty / no-kicker-card graveyard → no-op, no exception.
/// </summary>
[Trait("Color", "G")]
public class MurasaSproutlingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity()
    {
        var c = MurasaSproutlingFactory.Create(_alice);

        c.Name.Should().Be("Murasa Sproutling");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Plant).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().Be(_alice);
        c.Controller.Should().Be(_alice);
    }

    [Fact]
    public void HasKickerMarker()
    {
        var c = MurasaSproutlingFactory.Create(_alice);
        KickerAbilityDetector.HasKickerAbility(c).Should().BeTrue();
    }

    [Fact]
    public void BuildAdditionalCost_IsKicker1G()
    {
        var c = MurasaSproutlingFactory.Create(_alice);
        var cost = MurasaSproutlingFactory.BuildAdditionalCost(c);

        cost.Should().BeOfType<KickerAdditionalCost>();
        ((KickerAdditionalCost)cost).KickerCost.Should().Be(ManaCost.Parse("{1}{G}"));
    }

    [Fact]
    public void Etb_DeclaresTargetRequestForKickerCardInGraveyard()
    {
        var c = MurasaSproutlingFactory.Create(_alice);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("kicker");
        req.Description.Should().Contain("graveyard");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void Etb_OnlyKickerCardsAreCandidates()
    {
        var kickerCard = MakeKickerCardInGraveyard("Vines of Vastwood");
        var plainCard = MakeCreatureInGraveyard("Llanowar Elves", "{G}");

        var c = MurasaSproutlingFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        var candidates = etb.TargetRequests[0].LegalCandidates;

        candidates.Should().Contain(kickerCard);
        candidates.Should().NotContain(plainCard);
    }

    [Fact]
    public void Etb_NotKicked_IsCleanNoOp_EvenWithLegalCandidate()
    {
        // Intervening-if (CR 603.4 / CR 702.33b) — if it was NOT kicked,
        // the ETB does nothing even though a kicker card sits in the
        // graveyard.
        var kickerCard = MakeKickerCardInGraveyard("Vines of Vastwood");

        var c = MurasaSproutlingFactory.Create(_alice);
        c.SetWasKicked(false);
        PutOnBattlefield(c);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        Action act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow();
        // Kicker card stays in graveyard — trigger no-op'd (not kicked).
        _alice.Zones.Graveyard.GetCards().Should().Contain(kickerCard);
        _alice.Zones.Hand.GetCards().Should().NotContain(kickerCard);
    }

    [Fact]
    public void Etb_Kicked_FallbackReturnsFirstKickerCard()
    {
        var kickerCard = MakeKickerCardInGraveyard("Vines of Vastwood");

        var c = MurasaSproutlingFactory.Create(_alice);
        c.SetWasKicked(true);
        PutOnBattlefield(c);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(kickerCard);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(kickerCard);
        kickerCard.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Etb_Kicked_AgentSetTargetReturnsThatCard()
    {
        var firstKicker = MakeKickerCardInGraveyard("Vines of Vastwood");
        var secondKicker = MakeKickerCardInGraveyard("Burst Lightning");

        var c = MurasaSproutlingFactory.Create(_alice);
        c.SetWasKicked(true);
        PutOnBattlefield(c);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { secondKicker },
        });

        foreach (var e in etb.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(secondKicker);
        secondKicker.Zone.Should().Be(ZoneType.Hand);
        // The first kicker card was not selected → stays in graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(firstKicker);
    }

    [Fact]
    public void Etb_Kicked_NoKickerCardInGraveyard_IsCleanNoOp()
    {
        // Graveyard holds only a non-kicker card. Even kicked, the return
        // has no legal target → clean no-op (CR 608.2b).
        var plain = MakeCreatureInGraveyard("Llanowar Elves", "{G}");

        var c = MurasaSproutlingFactory.Create(_alice);
        c.SetWasKicked(true);
        PutOnBattlefield(c);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        Action act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        plain.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Etb_Kicked_EmptyGraveyard_IsCleanNoOp()
    {
        var c = MurasaSproutlingFactory.Create(_alice);
        c.SetWasKicked(true);
        PutOnBattlefield(c);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        Action act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void NamedFactoryDispatch()
    {
        var card = NamedCardFactory.Create("Murasa Sproutling", _alice);
        card.Should().NotBeNull();
        card!.Name.Should().Be("Murasa Sproutling");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void PutOnBattlefield(Creature c)
    {
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
    }

    private ICard MakeKickerCardInGraveyard(string name)
    {
        var card = new Instant(name, "{G}");
        card.AddAbility(new KeywordAbility(KickerAbilityDetector.KickerKeyword, card, _alice));
        card.SetOwner(_alice);
        card.SetController(_alice);
        card.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(card);
        return card;
    }

    private ICard MakeCreatureInGraveyard(string name, string manaCost)
    {
        var card = new Creature(name, manaCost, power: 1, toughness: 1);
        card.SetOwner(_alice);
        card.SetController(_alice);
        card.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(card);
        return card;
    }
}
