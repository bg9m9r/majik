using FluentAssertions;
using Majik.Core.CardData;
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
/// Tests for <see cref="RegrowthFactory"/> — Regrowth (Alpha + reprints,
/// {1}{G}).
///
/// Sorcery. "Return target card from your graveyard to your hand."
///
/// Regrowth is the bare sorcery version of Bala Ged Recovery's front-face
/// effect (ANY card type, no restriction — CR 700.6) with no MDFC back face.
///
/// Covers:
/// - Identity (name, cost, type, colour, owner, MV).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - SpellDefinition shape (1..1 graveyard-card request).
/// - Resolve: agent-set target returned; first-card fallback; empty
///   graveyard no-op; any-card-type returnable; illegal-on-resolution no-op;
///   ZoneService route.
/// </summary>
public class RegrowthFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Identity + dispatch
    // =========================================================================

    [Fact]
    public void Regrowth_Identity_Green_Sorcery_ManaValueTwo()
    {
        var card = RegrowthFactory.Create(_alice);

        card.Name.Should().Be("Regrowth");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(2,
            "Regrowth costs {1}{G} — generic 1 + 1 green = MV 2 (CR 202.3)");
    }

    [Fact]
    public void Regrowth_IsGreen()
    {
        var card = RegrowthFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Regrowth()
    {
        var card = NamedCardFactory.Create("Regrowth", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Regrowth");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // =========================================================================
    // SpellDefinition shape
    // =========================================================================

    [Fact]
    public void Regrowth_BuildDefinition_SingleGraveyardCardRequest()
    {
        var def = RegrowthFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("graveyard");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // =========================================================================
    // Resolution
    // =========================================================================

    [Fact]
    public void Regrowth_Resolve_ReturnsChosenTarget()
    {
        var bolt = MakeInstantInGraveyard("Lightning Bolt", "{R}");
        var rampant = MakeSorceryInGraveyard("Rampant Growth", "{1}{G}");

        ExecuteResolve(target: rampant);

        _alice.Zones.Hand.GetCards().Should().Contain(rampant);
        rampant.Zone.Should().Be(ZoneType.Hand);

        // Bolt was not chosen → stays in graveyard ("target" is singular,
        // CR 700.6).
        _alice.Zones.Graveyard.GetCards().Should().Contain(bolt);
        bolt.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Regrowth_Resolve_NoTarget_FallsBackToFirstCardInGraveyard()
    {
        var bolt = MakeInstantInGraveyard("Lightning Bolt", "{R}");
        var rampant = MakeSorceryInGraveyard("Rampant Growth", "{1}{G}");

        // No target supplied — deterministic fallback picks the first card.
        ExecuteResolve(target: null);

        _alice.Zones.Hand.GetCards().Should().Contain(bolt);
        bolt.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Graveyard.GetCards().Should().Contain(rampant);
    }

    [Fact]
    public void Regrowth_Resolve_EmptyGraveyard_IsCleanNoOp()
    {
        Action act = () => ExecuteResolve(target: null);

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Instant")]
    [InlineData("Sorcery")]
    [InlineData("Creature")]
    [InlineData("Land")]
    public void Regrowth_ReturnsAnyCardType(string cardType)
    {
        // CR 700.6 — the oracle says "card", with no type restriction.
        ICard seed = cardType switch
        {
            "Instant" => MakeInstantInGraveyard("Lightning Bolt", "{R}"),
            "Sorcery" => MakeSorceryInGraveyard("Rampant Growth", "{1}{G}"),
            "Creature" => MakeCreatureInGraveyard("Llanowar Elves", "{G}"),
            "Land" => MakeLandInGraveyard("Forest"),
            _ => throw new ArgumentOutOfRangeException(nameof(cardType)),
        };

        ExecuteResolve(target: seed);

        seed.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(seed);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(seed);
    }

    [Fact]
    public void Regrowth_Resolve_TargetNoLongerInGraveyard_IsNoOp()
    {
        // CR 608.2b — a chosen card that has left the graveyard by
        // resolution fizzles the return.
        var bolt = MakeInstantInGraveyard("Lightning Bolt", "{R}");
        _alice.Zones.Graveyard.RemoveCard(bolt);
        bolt.SetZone(ZoneType.Exile);

        ExecuteResolve(target: bolt);

        _alice.Zones.Hand.GetCards().Should().NotContain(bolt);
        bolt.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Regrowth_Resolve_RoutesThroughZoneService_WhenSupplied()
    {
        var bus = new TestEventBus();
        var zones = new ZoneService(bus);
        var rampant = MakeSorceryInGraveyard("Rampant Growth", "{1}{G}");

        var def = RegrowthFactory.BuildDefinition(_alice, o => o, zones);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { rampant } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // The ZoneService route moves the card Graveyard → Hand (and
        // publishes a CardMovedEvent so any "leaves graveyard" triggers fire —
        // CR 603.6a / CR 701.20).
        rampant.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(rampant);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(rampant);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private void ExecuteResolve(ICard? target)
    {
        var def = RegrowthFactory.BuildDefinition(_alice, o => o);
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

    private Instant MakeInstantInGraveyard(string name, string manaCost)
    {
        var card = new Instant(name, manaCost);
        card.SetOwner(_alice);
        card.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(card);
        return card;
    }

    private Sorcery MakeSorceryInGraveyard(string name, string manaCost)
    {
        var card = new Sorcery(name, manaCost);
        card.SetOwner(_alice);
        card.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(card);
        return card;
    }

    private Creature MakeCreatureInGraveyard(string name, string manaCost)
    {
        var card = new Creature(name, manaCost, power: 1, toughness: 1);
        card.SetOwner(_alice);
        card.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(card);
        return card;
    }

    private Land MakeLandInGraveyard(string name)
    {
        var card = new Land(name);
        card.SetOwner(_alice);
        card.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(card);
        return card;
    }
}
