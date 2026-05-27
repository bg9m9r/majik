using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Gravedigger (various printings, {3}{B}).
///
/// Creature — Zombie 2/2. Oracle text:
///   "When Gravedigger enters, you may return target creature card from
///    your graveyard to your hand."
///
/// Covers:
///   - Card shape (name, types, subtypes, P/T, mana cost).
///   - ETB trigger structure (declares a target request for a creature card
///     in controller's graveyard, scoped to battlefield active zone).
///   - Creature-card filter: non-creature cards (Instant, Sorcery, Land) are
///     NOT valid candidates.
///   - Single-arg fallback: first creature card in graveyard picked + moved to hand.
///   - Agent-set ChosenTargets: specified creature card returned.
///   - Empty graveyard → no-op, no exception.
///   - Graveyard with only non-creature cards → no-op, no exception.
///   - NamedCardFactory dispatch.
/// </summary>
public class GravediggerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Gravedigger_IsCreature_Zombie_2_2_AtCost3B()
    {
        var gravedigger = GravediggerFactory.Create(_alice);

        gravedigger.Name.Should().Be("Gravedigger");
        gravedigger.ManaCost.Should().Be("{3}{B}");
        gravedigger.HasType(CardType.Creature).Should().BeTrue();
        gravedigger.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        gravedigger.BasePower.Should().Be(2);
        gravedigger.BaseToughness.Should().Be(2);
        gravedigger.Owner.Should().Be(_alice);
        gravedigger.Controller.Should().Be(_alice);
    }

    [Fact]
    public void Gravedigger_Etb_PromptsForCreatureCardInGraveyard()
    {
        // Structural check: a single TriggeredAbility with a TargetRequest
        // describing "target creature card in your graveyard" (mandatory
        // single target, creature cards only — distinct from Eternal
        // Witness's any-card filter).
        var gravedigger = GravediggerFactory.Create(_alice);

        var triggers = gravedigger.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature");
        req.Description.Should().Contain("graveyard");

        // ETB trigger lives on the battlefield (CR 603.6a).
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void Gravedigger_Etb_FallbackPicksFirstCreatureCardFromGraveyard()
    {
        // Two creature cards in Alice's graveyard; no agent-set target.
        // Single-arg dispatcher fallback picks the first creature card.
        var llanowar = MakeCreatureInZone("Llanowar Elves", "{G}", _alice);
        var ragavan = MakeCreatureInZone("Ragavan, Nimble Pilferer", "{R}", _alice);

        var gravedigger = GravediggerFactory.Create(_alice);
        gravedigger.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(gravedigger);

        var etb = gravedigger.Abilities.OfType<TriggeredAbility>().Single();

        // Resolve the trigger effect directly — no target supplied.
        foreach (var effect in etb.Effects) effect.Execute();

        // First creature card (Llanowar Elves) is now in Alice's hand.
        _alice.Zones.Hand.GetCards().Should().Contain(llanowar);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(llanowar);
        llanowar.Zone.Should().Be(ZoneType.Hand);

        // Ragavan stays in the graveyard — "target" is singular (CR 700.6).
        _alice.Zones.Graveyard.GetCards().Should().Contain(ragavan);
    }

    [Fact]
    public void Gravedigger_Etb_AgentSetTargetReturnsThatCreatureCard()
    {
        // Multiple creature cards in graveyard + agent picks the second one.
        // That specific card is returned (NOT the first).
        var llanowar = MakeCreatureInZone("Llanowar Elves", "{G}", _alice);
        var ragavan = MakeCreatureInZone("Ragavan, Nimble Pilferer", "{R}", _alice);

        var gravedigger = GravediggerFactory.Create(_alice);
        gravedigger.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(gravedigger);

        var etb = gravedigger.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ragavan },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(ragavan);
        ragavan.Zone.Should().Be(ZoneType.Hand);

        // Llanowar Elves was not selected → stays in graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(llanowar);
        llanowar.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Gravedigger_EmptyGraveyard_IsCleanNoOp()
    {
        // No cards in graveyard → resolving the ETB must not throw and
        // must not move anything into hand (CR 608.2b — no legal target →
        // ability does nothing).
        var gravedigger = GravediggerFactory.Create(_alice);
        gravedigger.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(gravedigger);

        var etb = gravedigger.Abilities.OfType<TriggeredAbility>().Single();

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
    [InlineData("Land")]
    public void Gravedigger_NonCreatureCardInGraveyard_IsCleanNoOp(string cardType)
    {
        // Gravedigger's oracle says "creature card" — non-creature cards
        // must NOT be returned and must not cause an exception.
        ICard nonCreature = cardType switch
        {
            "Instant" => MakeInstantInZone("Lightning Bolt", "{R}", _alice),
            "Sorcery" => MakeSorceryInZone("Rampant Growth", "{1}{G}", _alice),
            "Land" => MakeLandInZone("Forest", _alice),
            _ => throw new ArgumentOutOfRangeException(nameof(cardType)),
        };

        var gravedigger = GravediggerFactory.Create(_alice);
        gravedigger.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(gravedigger);

        var etb = gravedigger.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();

        // Non-creature card stays in graveyard — never moved to hand.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        nonCreature.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Gravedigger_OnlyCreatureCardsAreTargetCandidates()
    {
        // Mix of creature and non-creature cards in graveyard.
        // The TargetRequest's LegalCandidates must contain only creature cards.
        var bolt = MakeInstantInZone("Lightning Bolt", "{R}", _alice);
        var llanowar = MakeCreatureInZone("Llanowar Elves", "{G}", _alice);
        var forest = MakeLandInZone("Forest", _alice);
        var zombie = MakeCreatureInZone("Zombie Token", "{B}", _alice);

        var gravedigger = GravediggerFactory.Create(_alice);

        var etb = gravedigger.Abilities.OfType<TriggeredAbility>().Single();
        var candidates = etb.TargetRequests[0].LegalCandidates;

        candidates.Should().Contain(llanowar);
        candidates.Should().Contain(zombie);
        candidates.Should().NotContain(bolt);
        candidates.Should().NotContain(forest);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Gravedigger()
    {
        var card = NamedCardFactory.Create("Gravedigger", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Gravedigger");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(2);
        card.Owner.Should().Be(_alice);

        // ETB trigger should be wired by the factory.
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
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
