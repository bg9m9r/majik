using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Nahiri's Wrath (Eldritch Moon, {4}{R}{R}).
///
/// Oracle:
///   "As an additional cost to cast this spell, discard X cards.
///    Nahiri's Wrath deals damage to each of up to X target creatures,
///    planeswalkers, and/or players equal to the total mana value of the
///    discarded cards."
///
/// Coverage:
///   * Identity — Sorcery {4}{R}{R}.
///   * NamedCardFactory dispatch.
///   * SpellDefinition shape: one 0..∞ "creatures/planeswalkers/players"
///     target request + a <see cref="DiscardXCardsAdditionalCost"/>.
///   * DiscardXCardsAdditionalCost defaults to discarding the whole hand.
///   * DiscardXCardsAdditionalCost honours pre-supplied Targets list.
///   * Resolve deals total-mv damage to each chosen target.
/// </summary>
public class NahirisWrathTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_Sorcery_FourRedRed()
    {
        var card = NahirisWrathFactory.Create(_alice);

        card.Name.Should().Be("Nahiri's Wrath");
        card.ManaCost.Should().Be("{4}{R}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        card.Should().BeOfType<Sorcery>();
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsSorcery()
    {
        var card = NamedCardFactory.Create("Nahiri's Wrath", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Nahiri's Wrath");
    }

    [Fact]
    public void SpellDefinition_HasDiscardXAdditionalCost_AndOneTargetRequest()
    {
        var def = NahirisWrathFactory.BuildSpellDefinition(raw => raw);

        def.HasVariableX.Should().BeFalse(
            "X here is the count of discarded cards (additional cost), not a mana-{X} pip");
        def.AdditionalCostsOrEmpty.Should().HaveCount(1);
        def.AdditionalCostsOrEmpty[0].Should().BeOfType<DiscardXCardsAdditionalCost>();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(0);
        def.TargetRequests[0].MaxTargets.Should().Be(int.MaxValue);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Burn);
    }

    // -----------------------------------------------------------------------
    // DiscardXCardsAdditionalCost behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public void DiscardXCost_Default_DiscardsEntireHand_CanPayAlwaysTrue()
    {
        var a = new Creature("Bear", "{1}{G}", 2, 2);
        a.SetOwner(_alice); _alice.Zones.Hand.AddCard(a); a.SetZone(ZoneType.Hand);
        var b = new Instant("Bolt", "{R}");
        b.SetOwner(_alice); _alice.Zones.Hand.AddCard(b); b.SetZone(ZoneType.Hand);

        var cost = new DiscardXCardsAdditionalCost();

        cost.CanPay(_alice).Should().BeTrue("X = 0 is legal; cost is always payable");
        cost.Pay(_alice).Should().BeTrue();
        cost.Discarded.Should().HaveCount(2);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { a, b });
    }

    [Fact]
    public void DiscardXCost_HonoursPreSuppliedTargets()
    {
        var a = new Creature("Bear", "{1}{G}", 2, 2);
        a.SetOwner(_alice); _alice.Zones.Hand.AddCard(a); a.SetZone(ZoneType.Hand);
        var b = new Instant("Bolt", "{R}");
        b.SetOwner(_alice); _alice.Zones.Hand.AddCard(b); b.SetZone(ZoneType.Hand);
        var c = new Sorcery("Wrath", "{2}{W}{W}");
        c.SetOwner(_alice); _alice.Zones.Hand.AddCard(c); c.SetZone(ZoneType.Hand);

        var cost = new DiscardXCardsAdditionalCost { Targets = new ICard[] { a, c } };

        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();
        cost.Discarded.Should().HaveCount(2);
        _alice.Zones.Hand.GetCards().Should().Contain(b,
            "only the nominated cards are discarded");
        _alice.Zones.Hand.GetCards().Should().NotContain(a);
        _alice.Zones.Hand.GetCards().Should().NotContain(c);
    }

    [Fact]
    public void DiscardXCost_PreSuppliedTargets_NotInHand_CanPayFalse()
    {
        var fake = new Instant("Phantom", "{R}");

        var cost = new DiscardXCardsAdditionalCost { Targets = new ICard[] { fake } };

        cost.CanPay(_alice).Should().BeFalse(
            "nominated card is not in the caster's hand (CR 117.1)");
    }

    // -----------------------------------------------------------------------
    // Resolution: deal total-mv damage to each chosen target
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsTotalManaValueDamage_ToEachChosenTarget()
    {
        // Alice will discard a {1}{G} (mv 2) and a {2}{W}{W} (mv 4) →
        // total mv = 6. Two targets: Bob (player) and Bob's creature.
        var disc1 = new Creature("Bear", "{1}{G}", 2, 2);
        disc1.SetOwner(_alice); _alice.Zones.Hand.AddCard(disc1); disc1.SetZone(ZoneType.Hand);
        var disc2 = new Sorcery("Wrath", "{2}{W}{W}");
        disc2.SetOwner(_alice); _alice.Zones.Hand.AddCard(disc2); disc2.SetZone(ZoneType.Hand);

        // Bob has a 5/5 creature on the battlefield.
        var bobCreature = new Creature("Tarmogoyf", "{1}{G}", 5, 5);
        bobCreature.SetOwner(_bob);
        bobCreature.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobCreature);
        bobCreature.SetZone(ZoneType.Battlefield);

        var def = NahirisWrathFactory.BuildSpellDefinition(raw => raw);

        // Pay the additional cost first (so Discarded is populated).
        var cost = (DiscardXCardsAdditionalCost)def.AdditionalCostsOrEmpty[0];
        cost.Pay(_alice).Should().BeTrue();
        cost.Discarded.Should().HaveCount(2);

        // Two chosen targets: Bob (player) + Bob's creature.
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob, bobCreature } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }

        // Total mv = 2 + 4 = 6. Bob loses 6 life; Tarmogoyf takes 6 damage
        // (lethal since toughness == 5).
        _bob.LifeTotal.Should().Be(14,
            "Bob takes 6 damage equal to the total mana value of the discarded cards");
        bobCreature.Damage.Should().BeGreaterThanOrEqualTo(6,
            "Tarmogoyf takes 6 damage too");
    }

    [Fact]
    public void Resolve_ZeroDiscarded_DealsNoDamage()
    {
        // Empty hand; cost still pays trivially (X = 0).
        var def = NahirisWrathFactory.BuildSpellDefinition(raw => raw);

        var cost = (DiscardXCardsAdditionalCost)def.AdditionalCostsOrEmpty[0];
        cost.Pay(_alice).Should().BeTrue();
        cost.Discarded.Should().BeEmpty();

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }

        _bob.LifeTotal.Should().Be(20,
            "X = 0 → total mana value = 0 → no damage dealt");
    }

    [Fact]
    public void Resolve_DamageScalesWithDiscardSize()
    {
        // Discard a single {4}{R}{R} card → mv 6; one target.
        var disc = new Sorcery("Big", "{4}{R}{R}");
        disc.SetOwner(_alice); _alice.Zones.Hand.AddCard(disc); disc.SetZone(ZoneType.Hand);

        var def = NahirisWrathFactory.BuildSpellDefinition(raw => raw);
        var cost = (DiscardXCardsAdditionalCost)def.AdditionalCostsOrEmpty[0];
        cost.Targets = new ICard[] { disc };
        cost.Pay(_alice).Should().BeTrue();

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }

        _bob.LifeTotal.Should().Be(14, "discarded card mv = 6 → 6 damage to Bob");
    }
}
