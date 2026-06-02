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
/// Tests for Cadaver Imp (Planeshift, {1}{B}{B}).
///
/// Covers:
///   - Card shape: name, types, subtypes (Imp), P/T (1/1), mana cost {1}{B}{B},
///     mana value 3, black colour.
///   - Flying keyword marker.
///   - ETB trigger structure: one TriggeredAbility, one TargetRequest for a
///     creature card in controller's graveyard (MinTargets 1, MaxTargets 1).
///   - Dispatch: NamedCardFactory resolves to correct shape.
///   - ETB resolution: creature card from graveyard moves to hand (fallback + agent paths).
///   - Noncreature card in graveyard is NOT a legal candidate.
///   - Empty graveyard is a clean no-op.
/// </summary>
[Trait("Color", "B")]
public class CadaverImpFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void CadaverImp_IsCreature_Imp_1_1_AtCost1BB()
    {
        var imp = CadaverImpFactory.Create(_alice);

        imp.Name.Should().Be("Cadaver Imp");
        imp.ManaCost.Should().Be("{1}{B}{B}");
        imp.HasType(CardType.Creature).Should().BeTrue();
        imp.HasSubtype(CardSubtype.Imp).Should().BeTrue();
        imp.BasePower.Should().Be(1);
        imp.BaseToughness.Should().Be(1);
        imp.Owner.Should().Be(_alice);
        imp.Controller.Should().Be(_alice);
    }

    // ── Flying ───────────────────────────────────────────────────────────────

    [Fact]
    public void CadaverImp_HasFlyingKeyword()
    {
        var imp = CadaverImpFactory.Create(_alice);

        var keywords = imp.Abilities.OfType<KeywordAbility>().ToList();
        keywords.Should().Contain(k => k.Keyword == "Flying",
            "Cadaver Imp has Flying per oracle text");
    }

    // ── ETB trigger shape ────────────────────────────────────────────────────

    [Fact]
    public void CadaverImp_HasSingleEtbTrigger_WithCreatureCardTargetRequest()
    {
        var imp = CadaverImpFactory.Create(_alice);

        var triggers = imp.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB trigger");

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("graveyard");

        // Active zone is battlefield (CR 603.6a).
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // ── Dispatch ─────────────────────────────────────────────────────────────
    // ── ETB resolution: fallback picks first creature card ───────────────────

    [Fact]
    public void CadaverImp_Etb_FallbackPicksFirstCreatureCardFromGraveyard()
    {
        // Two creature cards in Alice's graveyard; no agent-set target.
        var elf = MakeCreatureInZone("Llanowar Elves", "{G}", _alice);
        var bear = MakeCreatureInZone("Grizzly Bears", "{1}{G}", _alice);

        var imp = CadaverImpFactory.Create(_alice);
        imp.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(imp);

        var etb = imp.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // First creature (elf) moves to hand.
        _alice.Zones.Hand.GetCards().Should().Contain(elf);
        elf.Zone.Should().Be(ZoneType.Hand);

        // Second creature stays in graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(bear);
    }

    // ── ETB resolution: agent-set target ────────────────────────────────────

    [Fact]
    public void CadaverImp_Etb_AgentSetTargetReturnsThatCreatureCard()
    {
        var elf = MakeCreatureInZone("Llanowar Elves", "{G}", _alice);
        var bear = MakeCreatureInZone("Grizzly Bears", "{1}{G}", _alice);

        var imp = CadaverImpFactory.Create(_alice);
        imp.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(imp);

        var etb = imp.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(bear);
        bear.Zone.Should().Be(ZoneType.Hand);

        // Elf was not selected → stays in graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(elf);
        elf.Zone.Should().Be(ZoneType.Graveyard);
    }

    // ── Noncreature in graveyard is not a legal candidate ────────────────────

    [Fact]
    public void CadaverImp_Etb_NoncreatureInGraveyard_IsNotLegalCandidate()
    {
        // A Sorcery lives in Alice's graveyard — Cadaver Imp says "creature
        // card" so it must not appear among TargetRequest.LegalCandidates.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var imp = CadaverImpFactory.Create(_alice);

        var req = imp.Abilities.OfType<TriggeredAbility>().Single().TargetRequests[0];
        req.LegalCandidates.Should().NotContain(bolt,
            "Lightning Bolt is not a creature card");
    }

    [Fact]
    public void CadaverImp_Etb_CreatureInGraveyard_IsLegalCandidate()
    {
        // Seed one creature card into the graveyard; confirm it appears
        // in the TargetRequest's LegalCandidates at factory time.
        var elf = MakeCreatureInZone("Llanowar Elves", "{G}", _alice);

        var imp = CadaverImpFactory.Create(_alice);

        var req = imp.Abilities.OfType<TriggeredAbility>().Single().TargetRequests[0];
        req.LegalCandidates.Should().Contain(elf,
            "Llanowar Elves is a creature card in the graveyard");
    }

    // ── Empty graveyard: no-op ───────────────────────────────────────────────

    [Fact]
    public void CadaverImp_Etb_EmptyGraveyard_IsCleanNoOp()
    {
        var imp = CadaverImpFactory.Create(_alice);
        imp.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(imp);

        var etb = imp.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Creature MakeCreatureInZone(string name, string manaCost, Player owner)
    {
        var card = new Creature(name, manaCost, power: 1, toughness: 1);
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }
}
