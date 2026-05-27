using System.Linq;
using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Raise the Alarm ({1}{W} Instant).
///
/// Oracle text: "Create two 1/1 white Soldier creature tokens."
///
/// Coverage:
/// - Card identity (name, type, mana cost {1}{W}, colour white, mana value 2).
/// - <see cref="NamedCardFactory"/> dispatch returns an <see cref="Instant"/>.
/// - Resolve effect creates exactly two 1/1 white Soldier tokens under caster.
/// - Each token: IsToken, Power 1, Toughness 1, Soldier subtype, white colour.
/// - No targets declared (pure create-two spell, no TargetRequests).
/// </summary>
public class RaiseTheAlarmTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void RaiseTheAlarm_Identity_InstantWhite_ManaCost1W()
    {
        var c = RaiseTheAlarmFactory.Create(_alice);

        c.Name.Should().Be("Raise the Alarm");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RaiseTheAlarm_ManaCost_HasManaValue2()
    {
        var c = RaiseTheAlarmFactory.Create(_alice);
        c.ManaCostValue.TotalValue.Should().Be(2, "mana value of {1}{W} is 2");
    }

    [Fact]
    public void RaiseTheAlarm_IsWhite()
    {
        var c = RaiseTheAlarmFactory.Create(_alice);
        CardColors.GetColors(c).Should().Contain(ManaColor.White);
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_RaiseTheAlarm()
    {
        var c = NamedCardFactory.Create("Raise the Alarm", _alice);

        c.Should().BeOfType<Instant>();
        c.Name.Should().Be("Raise the Alarm");
    }

    // -----------------------------------------------------------------------
    // Spell definition — no target requests
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_HasNoTargetRequests()
    {
        var def = RaiseTheAlarmFactory.BuildSpellDefinition(_alice);

        def.TargetRequests.Should().BeEmpty(
            "Raise the Alarm has no targets — it just creates two tokens");
    }

    // -----------------------------------------------------------------------
    // Resolve effect — two 1/1 white Soldier tokens
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_CreatesTwoWhiteSoldierTokens()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var effects = RaiseTheAlarmFactory.BuildResolveEffects(_alice, zones);
        effects.Should().ContainSingle("Raise the Alarm resolves as a single grouped effect");

        foreach (var effect in effects) effect.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();

        tokens.Should().HaveCount(RaiseTheAlarmFactory.TokensCreated,
            "Raise the Alarm creates exactly two tokens");

        tokens.Should().AllSatisfy(t =>
        {
            t.Name.Should().Be("Soldier");
            t.BasePower.Should().Be(RaiseTheAlarmFactory.TokenPower);
            t.BaseToughness.Should().Be(RaiseTheAlarmFactory.TokenToughness);
            t.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
            t.IsToken.Should().BeTrue();
            t.TokenColorsOverride.Should().NotBeNull();
            t.TokenColorsOverride!.Should().Contain(ManaColor.White,
                "Soldier tokens are white per the printed clause (CR 105 / 111.4)");
        });
    }

    [Fact]
    public void Resolve_TokensAreUnderCasterControl()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var effects = RaiseTheAlarmFactory.BuildResolveEffects(_alice, zones);
        foreach (var effect in effects) effect.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(RaiseTheAlarmFactory.TokensCreated);
        tokens.Should().AllSatisfy(t =>
            t.Controller.Should().BeSameAs(_alice,
                "caster controls the tokens they create"));
    }
}
