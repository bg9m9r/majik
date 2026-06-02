using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TreasureVaultFactory"/> (The Brothers' War).
///
/// Treasure Vault — Artifact Land. Oracle text (Scryfall, verified
/// 2026-06-02):
///   "{T}: Add {C}.
///    {X}{X}, {T}, Sacrifice this land: Create X Treasure tokens."
///
/// Covers:
/// - Identity (Artifact + Land, nonbasic) + <see cref="NamedCardFactory"/>
///   dispatch.
/// - One {C} mana ability (CR 605.1).
/// - One {X}{X}, {T}, Sacrifice activated ability with the correct cost shape
///   ({X}{X} mana + tap + sacrifice).
/// - The activated ability's effect mints X Treasure tokens under the
///   controller and sacrifices Treasure Vault itself (CR 701.16).
/// - X = 0 mints no Treasures (but still sacrifices the land).
/// </summary>
[Trait("Color", "C")]
public class TreasureVaultFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TreasureVault_Identity()
    {
        var land = TreasureVaultFactory.Create(_alice);

        land.Name.Should().Be("Treasure Vault");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Artifact).Should().BeTrue(
            "Treasure Vault is an Artifact Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Treasure Vault is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TreasureVault()
    {
        var card = NamedCardFactory.Create("Treasure Vault", _alice);

        card.Should().BeOfType<Land>();
        card!.Name.Should().Be("Treasure Vault");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();

        // One {C} mana ability.
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the base {T}: Add {C} mana ability");

        // One {X}{X}, {T}, Sacrifice activated ability. ManaAbility is not an
        // ActivatedAbility subclass, so OfType already excludes the {C} ability.
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {X}{X}, {T}, Sacrifice: Create X Treasures activated ability");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void TreasureVault_HasColorlessManaAbility()
    {
        var land = TreasureVaultFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var colorless = land.Abilities.OfType<ManaAbility>().Single();

        colorless.CanActivate().Should().BeTrue(
            "an untapped Treasure Vault on the battlefield can tap for {C}");

        var produced = colorless.Activate();
        produced.Generic.Should().Be(1, "{C} folds into Generic like other colourless producers");
        produced.TotalValue.Should().Be(1);
        land.IsTapped.Should().BeTrue("CR 605 — the activation cost includes {T}");
    }

    // -----------------------------------------------------------------------
    // {X}{X}, {T}, Sacrifice this land: Create X Treasure tokens
    // -----------------------------------------------------------------------

    [Fact]
    public void TreasureVault_SacrificeAbility_HasCorrectCostShape()
    {
        var land = TreasureVaultFactory.Create(_alice);

        var sac = land.Abilities.OfType<ActivatedAbility>().Single();

        sac.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the printed cost has a single mana component {X}{X}");
        sac.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Tap)
            .Should().Be(1, "the tap rider");
        sac.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Sacrifice)
            .Should().Be(1, "the sacrifice-this-land rider");
    }

    [Fact]
    public void TreasureVault_SacrificeAbility_CreatesXTreasures_AndSacrificesItself()
    {
        var (zones, _) = BuildEngine();

        var land = TreasureVaultFactory.Create(_alice, treasureXValueProvider: () => 3, zoneService: zones);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var sac = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in sac.Effects) e.Execute();

        var treasures = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.HasSubtype(CardSubtype.Treasure))
            .ToList();
        treasures.Should().HaveCount(3, "X = 3 → three Treasure tokens (CR 111.10)");
        treasures.Should().OnlyContain(t => t.IsToken);

        // CR 701.16 — the sacrifice cost moves Treasure Vault to its owner's
        // graveyard.
        land.Zone.Should().Be(ZoneType.Graveyard,
            "the sacrifice cost moves Treasure Vault to its owner's graveyard");
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void TreasureVault_SacrificeAbility_WithX0_CreatesNoTreasures()
    {
        var (zones, _) = BuildEngine();

        var land = TreasureVaultFactory.Create(_alice, treasureXValueProvider: () => 0, zoneService: zones);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var sac = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in sac.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Any(a => a.HasSubtype(CardSubtype.Treasure))
            .Should().BeFalse("X = 0 → no Treasure tokens");

        land.Zone.Should().Be(ZoneType.Graveyard,
            "the land is still sacrificed even at X = 0");
    }

    private static (ZoneService zones, Majik.Core.Stack.Stack stack) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        return (zones, stack);
    }
}
