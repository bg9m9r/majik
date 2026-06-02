using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tests.Helpers;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="PulseOfMurasaFactory"/> (Battle for Zendikar, {2}{G}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Return target creature or land card from a graveyard to its owner's
///    hand. You gain 6 life."
///
/// Covers:
/// - Card identity (Instant, {2}{G}, green, CMC 3, owner/controller).
/// - SpellDefinition shape — no modes, no X, one 1..1 graveyard-card request.
/// - Type filter (CR 700.6): only creature/land cards are legal targets;
///   instants/sorceries are not.
/// - Candidate pool spans ANY graveyard (caster's + opponent's).
/// - Return goes to the card's OWNER's hand, not the caster's (CR 109.4).
/// - Unconditional 6 life gain (CR 119.3), even when the return fizzles.
/// - First-candidate fallback; ZoneService route.
/// </summary>
[Trait("Color", "G")]
public class PulseOfMurasaFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // =========================================================================
    // Identity
    // =========================================================================

    [Fact]
    public void PulseOfMurasa_Identity_Green_Instant_ManaValueThree()
    {
        var card = PulseOfMurasaFactory.Create(_alice);

        card.Name.Should().Be("Pulse of Murasa");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        card.ManaCostValue.TotalValue.Should().Be(3,
            "Pulse of Murasa costs {2}{G} — generic 2 + 1 green = MV 3 (CR 202.3)");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // =========================================================================
    // SpellDefinition shape
    // =========================================================================

    [Fact]
    public void PulseOfMurasa_SpellDefinition_SingleGraveyardCardRequest_NoModesNoX()
    {
        var def = PulseOfMurasaFactory.BuildSpellDefinition(
            _alice, new[] { _alice, _bob }, o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("graveyard");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // =========================================================================
    // Type filter — CR 700.6
    // =========================================================================

    [Fact]
    public void IsLegalTarget_AllowsCreatureAndLand_RejectsInstantSorcery()
    {
        var creature = MakeCreatureInGraveyard(_alice, "Llanowar Elves", "{G}");
        var land = MakeLandInGraveyard(_alice, "Forest");
        var instant = MakeInstantInGraveyard(_alice, "Lightning Bolt", "{R}");
        var sorcery = MakeSorceryInGraveyard(_alice, "Rampant Growth", "{1}{G}");

        PulseOfMurasaFactory.IsLegalTarget(creature).Should().BeTrue();
        PulseOfMurasaFactory.IsLegalTarget(land).Should().BeTrue();
        PulseOfMurasaFactory.IsLegalTarget(instant).Should().BeFalse(
            "CR 700.6 — only creature or land cards are legal targets");
        PulseOfMurasaFactory.IsLegalTarget(sorcery).Should().BeFalse();
    }

    [Fact]
    public void SpellDefinition_Candidates_OnlyCreatureAndLandCards_AcrossAllGraveyards()
    {
        // Alice's graveyard: one creature + one instant. Bob's: one land.
        var aliceCreature = MakeCreatureInGraveyard(_alice, "Llanowar Elves", "{G}");
        MakeInstantInGraveyard(_alice, "Lightning Bolt", "{R}");
        var bobLand = MakeLandInGraveyard(_bob, "Forest");

        var def = PulseOfMurasaFactory.BuildSpellDefinition(
            _alice, new[] { _alice, _bob }, o => o);

        var candidates = def.TargetRequests[0].LegalCandidates;
        candidates.Should().Contain(aliceCreature);
        candidates.Should().Contain(bobLand, "candidates span ANY graveyard");
        candidates.Should().HaveCount(2, "the instant is filtered out (CR 700.6)");
    }

    // =========================================================================
    // Resolution — return + lifegain
    // =========================================================================

    [Fact]
    public void Resolve_ReturnsChosenCreature_ToOwnersHand_AndGainsSixLife()
    {
        var creature = MakeCreatureInGraveyard(_alice, "Llanowar Elves", "{G}");

        ExecuteResolve(target: creature, searchable: new[] { _alice, _bob });

        _alice.Zones.Hand.GetCards().Should().Contain(creature);
        creature.Zone.Should().Be(ZoneType.Hand);
        _alice.LifeTotal.Should().Be(26, "CR 119.3 — 20 + 6 = 26");
    }

    [Fact]
    public void Resolve_ReturnsCardToItsOwnersHand_NotCasters()
    {
        // CR 109.4 — Bob's creature in his graveyard returns to BOB's hand,
        // even though Alice casts the spell. Alice still gains the life.
        var bobsCreature = MakeCreatureInGraveyard(_bob, "Grizzly Bears", "{1}{G}");

        ExecuteResolve(target: bobsCreature, searchable: new[] { _alice, _bob });

        _bob.Zones.Hand.GetCards().Should().Contain(bobsCreature,
            "a card returns to its OWNER's hand (CR 109.4 / 400.3)");
        _alice.Zones.Hand.GetCards().Should().NotContain(bobsCreature);
        bobsCreature.Zone.Should().Be(ZoneType.Hand);
        _alice.LifeTotal.Should().Be(26, "the caster gains the life");
    }

    [Fact]
    public void Resolve_NoTarget_FallsBackToFirstLegalCard()
    {
        // First card in Alice's graveyard is an instant (illegal); the
        // fallback skips it and picks the first creature/land card.
        MakeInstantInGraveyard(_alice, "Lightning Bolt", "{R}");
        var land = MakeLandInGraveyard(_alice, "Forest");

        ExecuteResolve(target: null, searchable: new[] { _alice, _bob });

        _alice.Zones.Hand.GetCards().Should().Contain(land);
        land.Zone.Should().Be(ZoneType.Hand);
        _alice.LifeTotal.Should().Be(26);
    }

    [Fact]
    public void Resolve_EmptyGraveyards_IsCleanNoOp_ButStillGainsLife()
    {
        // No legal target anywhere → return does nothing, but the lifegain
        // is an independent, non-targeted effect and still happens
        // (CR 608.2c — partial resolution).
        Action act = () => ExecuteResolve(target: null, searchable: new[] { _alice, _bob });

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.LifeTotal.Should().Be(26, "CR 119.3 lifegain is unconditional");
    }

    [Fact]
    public void Resolve_TargetNoLongerLegal_IsNoOpReturn_ButStillGainsLife()
    {
        // CR 608.2b — a chosen card that has left the graveyard by resolution
        // fizzles the return; the lifegain still happens.
        var creature = MakeCreatureInGraveyard(_alice, "Llanowar Elves", "{G}");
        _alice.Zones.Graveyard.RemoveCard(creature);
        creature.SetZone(ZoneType.Exile);

        ExecuteResolve(target: creature, searchable: new[] { _alice, _bob });

        _alice.Zones.Hand.GetCards().Should().NotContain(creature);
        creature.Zone.Should().Be(ZoneType.Exile);
        _alice.LifeTotal.Should().Be(26, "lifegain is unconditional");
    }

    [Fact]
    public void Resolve_RoutesThroughZoneService_WhenSupplied()
    {
        var bus = new TestEventBus();
        var zones = new ZoneService(bus);
        var creature = MakeCreatureInGraveyard(_alice, "Llanowar Elves", "{G}");

        var def = PulseOfMurasaFactory.BuildSpellDefinition(
            _alice, new[] { _alice, _bob }, o => o, zones);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { creature } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        creature.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(creature);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(creature);
        _alice.LifeTotal.Should().Be(26);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private void ExecuteResolve(ICard? target, IReadOnlyList<Player> searchable)
    {
        var def = PulseOfMurasaFactory.BuildSpellDefinition(_alice, searchable, o => o);
        var targets = target == null
            ? Array.Empty<IReadOnlyList<object>>()
            : new IReadOnlyList<object>[] { new object[] { target } };
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private static Instant MakeInstantInGraveyard(Player owner, string name, string manaCost)
    {
        var card = new Instant(name, manaCost);
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }

    private static Sorcery MakeSorceryInGraveyard(Player owner, string name, string manaCost)
    {
        var card = new Sorcery(name, manaCost);
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }

    private static Creature MakeCreatureInGraveyard(Player owner, string name, string manaCost)
    {
        var card = new Creature(name, manaCost, power: 1, toughness: 1);
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }

    private static Land MakeLandInGraveyard(Player owner, string name)
    {
        var card = new Land(name);
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }
}
