using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Eternal Witness (Fifth Dawn, {1}{G}{G}).
///
/// Covers:
///   - Card shape (name, types, subtypes, P/T, mana cost).
///   - ETB trigger structure (declares a target request for any card in
///     controller's graveyard, scoped to battlefield active zone).
///   - Single-arg fallback: first card in graveyard picked + moved to hand.
///   - Agent-set ChosenTargets: specified card returned.
///   - Empty graveyard → no-op, no exception.
///   - Card-type agnostic: Instant / Sorcery / Creature / Land all returnable.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "G")]
public class EternalWitnessFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void EternalWitness_IsCreature_HumanShaman_2_1_AtCost1GG()
    {
        var witness = EternalWitnessFactory.Create(_alice);

        witness.Name.Should().Be("Eternal Witness");
        witness.ManaCost.Should().Be("{1}{G}{G}");
        witness.HasType(CardType.Creature).Should().BeTrue();
        witness.HasSubtype(CardSubtype.Human).Should().BeTrue();
        witness.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        witness.BasePower.Should().Be(2);
        witness.BaseToughness.Should().Be(1);
        witness.Owner.Should().Be(_alice);
        witness.Controller.Should().Be(_alice);
    }

    [Fact]
    public void EternalWitness_Etb_PromptsForCardInGraveyard()
    {
        // Structural check: a single TriggeredAbility with a TargetRequest
        // describing "target card in your graveyard" (mandatory single
        // target, ANY card type — distinct from Animate Dead's creature
        // filter).
        var witness = EternalWitnessFactory.Create(_alice);

        var triggers = witness.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("graveyard");

        // ETB trigger lives on the battlefield (CR 603.6a).
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void EternalWitness_Etb_FallbackPicksFirstCardFromGraveyard()
    {
        // Two cards in Alice's graveyard; no agent-set target on the ETB.
        // Single-arg dispatcher fallback picks the first card.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var rampant = new Sorcery("Rampant Growth", "{1}{G}");
        rampant.SetOwner(_alice);
        rampant.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(rampant);

        var witness = EternalWitnessFactory.Create(_alice);
        witness.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(witness);

        var etb = witness.Abilities.OfType<TriggeredAbility>().Single();

        // Resolve the trigger effect directly — no target supplied.
        foreach (var effect in etb.Effects) effect.Execute();

        // First card in graveyard (Bolt) is now in Alice's hand.
        _alice.Zones.Hand.GetCards().Should().Contain(bolt);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bolt);
        bolt.Zone.Should().Be(ZoneType.Hand);

        // Rampant Growth stays in the graveyard — "target" is singular
        // (CR 700.6).
        _alice.Zones.Graveyard.GetCards().Should().Contain(rampant);
    }

    [Fact]
    public void EternalWitness_Etb_AgentSetTargetReturnsThatCard()
    {
        // Multiple cards in graveyard + agent picks the second one via
        // ChosenTargets. That specific card is returned (NOT the first).
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var rampant = new Sorcery("Rampant Growth", "{1}{G}");
        rampant.SetOwner(_alice);
        rampant.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(rampant);

        var witness = EternalWitnessFactory.Create(_alice);
        witness.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(witness);

        var etb = witness.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { rampant },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(rampant);
        rampant.Zone.Should().Be(ZoneType.Hand);

        // Bolt was not selected → stays in graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(bolt);
        bolt.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void EternalWitness_EmptyGraveyard_IsCleanNoOp()
    {
        // No cards in graveyard → resolving the ETB must not throw and
        // must not move anything into hand (CR 608.2b — empty target set
        // / no legal target → spell or ability does nothing).
        var witness = EternalWitnessFactory.Create(_alice);
        witness.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(witness);

        var etb = witness.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Instant")]
    [InlineData("Sorcery")]
    [InlineData("Creature")]
    [InlineData("Land")]
    public void EternalWitness_ReturnsAnyCardType(string cardType)
    {
        // CR 700.6 — Eternal Witness's printed oracle says "card", with
        // no type restriction. Validate by seeding one of each type into
        // the graveyard alone and confirming the return.
        ICard seed = cardType switch
        {
            "Instant" => MakeInstantInZone("Lightning Bolt", "{R}", _alice),
            "Sorcery" => MakeSorceryInZone("Rampant Growth", "{1}{G}", _alice),
            "Creature" => MakeCreatureInZone("Llanowar Elves", "{G}", _alice),
            "Land" => MakeLandInZone("Forest", _alice),
            _ => throw new ArgumentOutOfRangeException(nameof(cardType)),
        };

        var witness = EternalWitnessFactory.Create(_alice);
        witness.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(witness);

        var etb = witness.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        seed.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(seed);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(seed);
    }
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Instant MakeInstantInZone(string name, string manaCost, Player owner)
    {
        var card = new Instant(name, manaCost);
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }

    private static Sorcery MakeSorceryInZone(string name, string manaCost, Player owner)
    {
        var card = new Sorcery(name, manaCost);
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }

    private static Creature MakeCreatureInZone(string name, string manaCost, Player owner)
    {
        var card = new Creature(name, manaCost, power: 1, toughness: 1);
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }

    private static Land MakeLandInZone(string name, Player owner)
    {
        var card = new Land(name);
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }
}
