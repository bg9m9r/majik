using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RadiantFountainFactory"/>.
///
/// Colorless gain-life land. Oracle text (verified against Scryfall):
///   "When this land enters, you gain 2 life.
///    {T}: Add {C}."
///
/// Same oracle shape as <see cref="AkoumRefugeFactory"/> (a mana ability plus
/// a self-ETB "gain N life" trigger, CR 119) — only the mana colour and the
/// life amount differ. Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, nonbasic, owner/controller).
/// - One colorless mana ability — {C} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 2 life.
/// - ETB effect: controller's life total rises by exactly 2 (CR 119.3).
/// </summary>
[Trait("Color", "C")]
public class RadiantFountainTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RadiantFountain_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Radiant Fountain", _alice);

        land.Name.Should().Be("Radiant Fountain");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Radiant Fountain is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void RadiantFountain_HasManaAbility_ForColorless()
    {
        // CR 107.4c — {C} is colourless mana. ManaCost has no dedicated
        // colourless bucket today; ManaCost.Parse("C") maps it to +1 generic
        // (same posture as Rogue's Passage / Urza's Saga "{T}: Add {C}").
        var land = (Land)NamedCardFactory.Create("Radiant Fountain", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Generic == 1
                && m.ManaGenerated.TotalValue == 1);
    }

    [Fact]
    public void RadiantFountain_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Radiant Fountain", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void RadiantFountain_EtbEffect_GainsExactlyTwoLife()
    {
        // CR 119.3 — "you gain 2 life" raises the controller's life total by 2.
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Radiant Fountain", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(22, "Radiant Fountain's ETB gains its controller 2 life");
    }
}
