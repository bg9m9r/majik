using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Tezzeret, Agent of Bolas (Mirrodin Besieged, {2}{U}{B}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Tezzeret, starting loyalty 3,
///     mana cost {2}{U}{B}).
///   - Loyalty ability shape: three abilities at +1 / -1 / -4.
///   - +1: dig 5, first artifact card → hand, rest → bottom of library.
///   - +1: no artifact among the five → all bottomed.
///   - -1: target artifact becomes an artifact creature with base P/T 5/5.
///   - -4: target player loses X, controller gains X, X = 2× artifacts.
///   - Loyalty cost paid even when the effect body no-ops.
///   - NamedCardFactory dispatch.
/// </summary>
public class TezzeretAgentOfBolasFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Tezzeret_IsLegendaryPlaneswalker_Tezzeret_3Loyalty_AtCost2UB()
    {
        var tezz = TezzeretAgentOfBolasFactory.Create(_alice);

        tezz.Name.Should().Be("Tezzeret, Agent of Bolas");
        tezz.ManaCost.Should().Be("{2}{U}{B}");
        tezz.HasType(CardType.Planeswalker).Should().BeTrue();
        tezz.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        tezz.HasSubtype(CardSubtype.Tezzeret).Should().BeTrue();
        tezz.Loyalty.Should().Be(3);
        tezz.StartingLoyalty.Should().Be(3);
        tezz.Owner.Should().BeSameAs(_alice);
        tezz.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Tezzeret_HasThreeLoyaltyAbilities_Plus1_Minus1_Minus4()
    {
        var tezz = TezzeretAgentOfBolasFactory.Create(_alice);
        var loyaltyAbilities = tezz.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(3);
        loyaltyAbilities.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, -1, -4 });
    }

    [Fact]
    public void Tezzeret_Plus1_PutsFirstArtifactToHand_RestToBottom()
    {
        // Library top→bottom: l1, l2, ART (artifact), l4, l5, l6.
        var l1 = new Instant("l1", "{U}") { Owner = _alice };
        var l2 = new Instant("l2", "{U}") { Owner = _alice };
        var art = new Artifact("Mox", "{0}") { Owner = _alice };
        var l4 = new Instant("l4", "{U}") { Owner = _alice };
        var l5 = new Instant("l5", "{U}") { Owner = _alice };
        var l6 = new Instant("l6", "{U}") { Owner = _alice }; // 6th — never looked at
        foreach (var c in new ICard[] { l1, l2, art, l4, l5, l6 })
        {
            _alice.Zones.Library.AddCard(c);
            ((Card)c).SetZone(ZoneType.Library);
        }

        var tezz = TezzeretAgentOfBolasFactory.Create(_alice);
        var plus1 = tezz.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        tezz.Loyalty.Should().Be(4, "3 + 1 = 4");

        // The artifact went to hand.
        _alice.Zones.Hand.GetCards().Should().Contain(art);

        // The other four looked-at cards (l1, l2, l4, l5) are now on the
        // bottom in looked-at order; l6 (never looked at) stays on top.
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(5, "6 - 1 to hand");
        lib[0].Should().BeSameAs(l6, "l6 was the 6th card, never looked at, now on top");
        lib[1].Should().BeSameAs(l1, "looked-at remainder bottomed in order");
        lib[2].Should().BeSameAs(l2);
        lib[3].Should().BeSameAs(l4);
        lib[4].Should().BeSameAs(l5);
    }

    [Fact]
    public void Tezzeret_Plus1_NoArtifactAmongTopFive_AllBottomed()
    {
        var cards = new[] { "a", "b", "c", "d", "e" }
            .Select(n => new Instant(n, "{U}") { Owner = _alice })
            .ToArray();
        foreach (var c in cards)
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var tezz = TezzeretAgentOfBolasFactory.Create(_alice);
        var plus1 = tezz.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        _alice.Zones.Hand.GetCards().Should().BeEmpty("no artifact to reveal");
        _alice.Zones.Library.Count.Should().Be(5, "all five looked-at cards returned to bottom");
    }

    [Fact]
    public void Tezzeret_Minus1_TargetArtifactBecomesArtifactCreature5x5()
    {
        var effects = new ContinuousEffectsService();

        // A non-creature artifact on the battlefield.
        var rock = new Artifact("Mind Stone", "{2}");
        rock.SetOwner(_alice);
        rock.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(rock);
        rock.SetZone(ZoneType.Battlefield);

        var tezz = TezzeretAgentOfBolasFactory.Create(
            _alice,
            targetArtifactResolver: () => new Permanent[] { rock },
            targetPlayerResolver: null,
            effects: effects);

        var minus1 = tezz.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -1);
        minus1.Activate();

        tezz.Loyalty.Should().Be(2, "3 - 1 = 2");

        var chars = effects.Compute(rock);
        // CR 613.1c — Creature added in addition to its other types.
        chars.Types.Should().Contain(CardType.Creature, "Layer 4 adds Creature type");
        chars.Types.Should().Contain(CardType.Artifact,
            "CR 613.1c — types are added; it is still an artifact");
        // CR 613.7b — base power and toughness become 5/5.
        chars.Should().BeOfType<CreatureCharacteristics>(
            "the Layer-4 Creature grant upgrades the Artifact's row to a creature row");
        var cc = (CreatureCharacteristics)chars;
        cc.Power.Should().Be(5, "Tezzeret -1 sets base power 5");
        cc.Toughness.Should().Be(5, "Tezzeret -1 sets base toughness 5");
    }

    [Fact]
    public void Tezzeret_Minus1_NoEffectsService_IsLegalNoOp()
    {
        var rock = new Artifact("Mind Stone", "{2}");
        rock.SetOwner(_alice);
        rock.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(rock);
        rock.SetZone(ZoneType.Battlefield);

        var tezz = TezzeretAgentOfBolasFactory.Create(
            _alice,
            targetArtifactResolver: () => new Permanent[] { rock },
            targetPlayerResolver: null,
            effects: null);

        var minus1 = tezz.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -1);
        minus1.Activate();

        tezz.Loyalty.Should().Be(2, "loyalty change still applies (CR 606.3)");
    }

    [Fact]
    public void Tezzeret_Minus4_TargetLosesTwiceArtifacts_ControllerGains()
    {
        // Alice controls three artifacts → X = 2 × 3 = 6.
        foreach (var n in new[] { "Art1", "Art2", "Art3" })
        {
            var a = new Artifact(n, "{1}");
            a.SetOwner(_alice);
            a.SetController(_alice);
            _alice.Zones.Battlefield.AddCard(a);
            a.SetZone(ZoneType.Battlefield);
        }
        _alice.LifeTotal = 18;
        _bob.LifeTotal = 20;

        var tezz = TezzeretAgentOfBolasFactory.Create(
            _alice,
            targetArtifactResolver: null,
            targetPlayerResolver: () => new[] { _bob },
            effects: null);

        var minus4 = tezz.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -4);
        tezz.AddLoyalty(1); // 3 → 4 so -4 is legal.
        minus4.CanActivate().Should().BeTrue();
        minus4.Activate();

        tezz.Loyalty.Should().Be(0, "4 - 4 = 0");
        _bob.LifeTotal.Should().Be(14, "Bob loses X = 2 × 3 = 6 (20 - 6)");
        _alice.LifeTotal.Should().Be(24, "Alice gains X = 6 (18 + 6)");
    }

    [Fact]
    public void Tezzeret_Minus4_NoResolver_IsLegalNoOp()
    {
        var tezz = TezzeretAgentOfBolasFactory.Create(_alice);
        tezz.AddLoyalty(1); // 3 → 4

        var minus4 = tezz.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -4);
        minus4.Activate();

        tezz.Loyalty.Should().Be(0, "loyalty change still applies");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TezzeretAgentOfBolas()
    {
        var card = NamedCardFactory.Create("Tezzeret, Agent of Bolas", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Tezzeret, Agent of Bolas");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Tezzeret).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(3);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(3);
    }
}
