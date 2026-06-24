using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Honest Rutstein (Murders at Karlov Manor, {1}{B}{G}, Legendary
/// Creature — Human Warlock 3/2).
///
/// Oracle text (verified against Scryfall):
///   "When Honest Rutstein enters, return target creature card from your
///    graveyard to your hand.
///    Creature spells you cast cost {1} less to cast."
///
/// Covers (unique behaviour only — dispatch + well-formedness are asserted
/// for every implemented card by CardFactoryContractTests):
///   - Identity (Legendary, Human + Warlock, 3/2, {1}{B}{G}, owner/controller).
///   - ETB trigger structure (1..1 target request over the graveyard).
///   - ETB returns the chosen creature card to hand.
///   - ETB fallback picks the first CREATURE card (skips a non-creature card).
///   - Non-creature-only graveyard → clean no-op (creature-card filter).
///   - Empty graveyard → clean no-op.
///   - Creature spell you cast costs {1} less ({2}{G} -> {1}{G}).
///   - Non-creature spell you cast is unaffected.
///   - Opponent's creature spell is unaffected ("spells YOU cast").
/// </summary>
[Trait("Color", "M")]
public class HonestRutsteinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature BuildOnBattlefield(Player owner)
    {
        var card = HonestRutsteinFactory.Create(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        return card;
    }

    private static Creature SeedCreatureInGraveyard(string name, string cost, Player owner)
    {
        var c = new Creature(name, cost, power: 1, toughness: 1);
        c.SetOwner(owner);
        c.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(c);
        return c;
    }

    private static Instant SeedInstantInGraveyard(string name, string cost, Player owner)
    {
        var c = new Instant(name, cost);
        c.SetOwner(owner);
        c.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(c);
        return c;
    }

    [Fact]
    public void HonestRutstein_Identity()
    {
        var r = HonestRutsteinFactory.Create(_alice);

        r.Name.Should().Be("Honest Rutstein");
        r.ManaCost.Should().Be("{1}{B}{G}");
        r.HasType(CardType.Creature).Should().BeTrue();
        r.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        r.HasSubtype(CardSubtype.Human).Should().BeTrue();
        r.HasSubtype(CardSubtype.Warlock).Should().BeTrue();
        r.BasePower.Should().Be(3);
        r.BaseToughness.Should().Be(2);
        r.Owner.Should().BeSameAs(_alice);
        r.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HonestRutstein_Etb_DeclaresSingleGraveyardTargetRequest()
    {
        var r = HonestRutsteinFactory.Create(_alice);

        var etb = r.Abilities.OfType<TriggeredAbility>().Single();
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature");
        req.Description.Should().Contain("graveyard");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield, "CR 603.6a — ETB trigger");
    }

    [Fact]
    public void HonestRutstein_Etb_AgentSetTargetReturnsThatCreature()
    {
        var bear = SeedCreatureInGraveyard("Grizzly Bears", "{1}{G}", _alice);
        var elf = SeedCreatureInGraveyard("Llanowar Elves", "{G}", _alice);

        _ = BuildOnBattlefield(_alice);
        var r = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Single(c => c.Name == "Honest Rutstein");
        var etb = r.Abilities.OfType<TriggeredAbility>().Single();

        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { elf } });
        foreach (var effect in etb.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(elf);
        elf.Zone.Should().Be(ZoneType.Hand);
        // Non-selected creature stays put (singular target — CR 700.6).
        _alice.Zones.Graveyard.GetCards().Should().Contain(bear);
        bear.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void HonestRutstein_Etb_FallbackSkipsNonCreatureCard()
    {
        // A non-creature card sits first in the graveyard; the creature-card
        // filter must skip it and return the creature (CR 109.2 — "creature
        // card" matches printed card type).
        var bolt = SeedInstantInGraveyard("Lightning Bolt", "{R}", _alice);
        var bear = SeedCreatureInGraveyard("Grizzly Bears", "{1}{G}", _alice);

        _ = BuildOnBattlefield(_alice);
        var r = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Single(c => c.Name == "Honest Rutstein");
        var etb = r.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(bear);
        bear.Zone.Should().Be(ZoneType.Hand);
        // The Instant is not a creature card → ineligible, stays in graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(bolt);
        bolt.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void HonestRutstein_Etb_NoCreatureCardInGraveyard_IsCleanNoOp()
    {
        var bolt = SeedInstantInGraveyard("Lightning Bolt", "{R}", _alice);

        _ = BuildOnBattlefield(_alice);
        var r = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Single(c => c.Name == "Honest Rutstein");
        var etb = r.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow();
        // Non-creature card is not returned (CR 608.2b — no legal target).
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt);
        bolt.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void HonestRutstein_Etb_EmptyGraveyard_IsCleanNoOp()
    {
        _ = BuildOnBattlefield(_alice);
        var r = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Single(c => c.Name == "Honest Rutstein");
        var etb = r.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void HonestRutstein_CreatureSpellYouCast_CostsOneLess()
    {
        _ = BuildOnBattlefield(_alice);

        var bear = new Creature("Centaur Courser", "{2}{G}", 3, 3) { Owner = _alice };
        var reduced = CostReduction.GetEffectiveCost(bear, _alice);
        reduced.TotalValue.Should().Be(2, "{2}{G} creature -> {1}{G} (CR 117.7)");
    }

    [Fact]
    public void HonestRutstein_NonCreatureSpellYouCast_Unaffected()
    {
        _ = BuildOnBattlefield(_alice);

        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var cost = CostReduction.GetEffectiveCost(bolt, _alice);
        cost.TotalValue.Should().Be(1, "{R} Instant is not a creature spell");
    }

    [Fact]
    public void HonestRutstein_OpponentCreatureSpell_Unaffected()
    {
        _ = BuildOnBattlefield(_alice);

        // Bob casts a creature; Rutstein's reducer is "spells YOU cast",
        // scoped to the caster's battlefield (CR 117.7).
        var bear = new Creature("Centaur Courser", "{2}{G}", 3, 3) { Owner = _bob };
        var cost = CostReduction.GetEffectiveCost(bear, _bob);
        cost.TotalValue.Should().Be(3, "{2}{G} creature cast by opponent is unreduced");
    }
}
