using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Eidolon of Rhetoric (Journey into Nyx, {2}{W}).
///
/// Oracle (verified against Scryfall):
///   "Each player can't cast more than one spell each turn."
///
/// Coverage:
///   * Identity: Enchantment Creature — Spirit {2}{W} 1/4.
///   * Dispatch through <see cref="NamedCardFactory"/>.
///   * One-spell-per-turn cap (CR 601.3): ActionValidator allows the first cast
///     and blocks the second.
///   * Symmetry (CR 109.5): the cap applies to Eidolon's controller too.
///   * Per-turn reset (CR 514.2): a consumed cap is re-seeded at turn start.
///   * Cap lifts when Eidolon leaves the battlefield.
///   * Single-arg dispatch path registers no rail.
/// </summary>
[Trait("Color", "W")]
public class EidolonOfRhetoricTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ReplacementBus _replacements = new();
    private readonly ZoneService _zones;
    private readonly ActionValidator _validator = new();

    public EidolonOfRhetoricTests()
    {
        _zones = new ZoneService(_bus, _replacements);
        CastingRestrictions.Clear();
    }

    public void Dispose() => CastingRestrictions.Clear();

    private IReadOnlyList<Player> AllPlayers() => new[] { _alice, _bob };

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var eidolon = EidolonOfRhetoricFactory.Create(_alice);

        eidolon.Name.Should().Be("Eidolon of Rhetoric");
        eidolon.HasType(CardType.Creature).Should().BeTrue();
        eidolon.HasType(CardType.Enchantment).Should().BeTrue();
        eidolon.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        eidolon.ManaCost.Should().Be("{2}{W}");
        eidolon.ManaCostValue.Generic.Should().Be(2);
        eidolon.ManaCostValue.White.Should().Be(1);
        eidolon.Power.Should().Be(1);
        eidolon.Toughness.Should().Be(4);
        eidolon.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Dispatch_ByName_ProducesEidolon()
    {
        var card = NamedCardFactory.Create("Eidolon of Rhetoric", _alice);

        card.Should().NotBeNull();
        card.Name.Should().Be("Eidolon of Rhetoric");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Enchantment).Should().BeTrue();
    }

    private Creature EidolonOnBattlefield()
    {
        var eidolon = EidolonOfRhetoricFactory.Create(_alice, _bus, AllPlayers);
        _alice.Zones.Library.AddCard(eidolon);
        eidolon.SetZone(ZoneType.Library);
        _zones.MoveCard(eidolon, ZoneType.Library, ZoneType.Battlefield);
        return eidolon;
    }

    [Fact]
    public void ActionValidator_AllowsFirstSpell_BlocksSecond()
    {
        EidolonOnBattlefield();

        var spell = EidolonOfRhetoricFactory.Create(_bob);

        var first = new CastSpellAction(spell, _bob, sorcerySpeedAvailable: true);
        _validator.ValidateAction(first).IsValid.Should().BeTrue(
            "the first spell of the turn is allowed (cap = 1 remaining)");

        // Simulate the cast consuming the allowance (SpellCastFlow's hook).
        CastingRestrictions.ConsumeAdditionalSpellAllowance(_bob);

        var second = new CastSpellAction(spell, _bob, sorcerySpeedAvailable: true);
        var result = _validator.ValidateAction(second);
        result.IsValid.Should().BeFalse(
            "a second spell is blocked while Eidolon of Rhetoric is out (CR 601.3)");
        result.Violation!.RuleNumber.Should().Be("601.3");
    }

    [Fact]
    public void CastCap_AppliesToBothPlayers_Symmetric()
    {
        EidolonOnBattlefield();

        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeFalse();
        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_bob).Should().BeFalse();

        CastingRestrictions.ConsumeAdditionalSpellAllowance(_alice);
        CastingRestrictions.ConsumeAdditionalSpellAllowance(_bob);

        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeTrue(
            "Each player can't cast more than one spell each turn (CR 109.5 — symmetric)");
        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_bob).Should().BeTrue();
    }

    [Fact]
    public void CastCap_ResetsAtTurnStart()
    {
        EidolonOnBattlefield();

        CastingRestrictions.ConsumeAdditionalSpellAllowance(_alice);
        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeTrue();

        _bus.Publish(new TurnStartedEvent(_bob, 2));

        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeFalse(
            "the per-turn cap is re-seeded at turn start (CR 514.2)");
    }

    [Fact]
    public void CastCap_LiftsWhenEidolonLeavesBattlefield()
    {
        var eidolon = EidolonOnBattlefield();

        CastingRestrictions.ConsumeAdditionalSpellAllowance(_alice);
        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeTrue();

        _zones.MoveCard(eidolon, ZoneType.Battlefield, ZoneType.Graveyard);

        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeFalse(
            "the cast cap lifts when Eidolon of Rhetoric leaves the battlefield");
    }

    [Fact]
    public void SingleArgPath_RegistersNoRail()
    {
        EidolonOfRhetoricFactory.Create(_alice);

        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeFalse(
            "no cast cap is registered on the single-arg path");
    }
}
