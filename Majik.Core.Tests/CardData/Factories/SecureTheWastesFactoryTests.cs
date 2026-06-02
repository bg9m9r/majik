using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SecureTheWastesFactory"/>.
///
/// Oracle text (verified against Scryfall):
///   "Create X 1/1 white Warrior creature tokens." ({X}{W} Instant)
///
/// Covers:
/// - Card identity (Instant, {X}{W}, white, owner/controller).
/// - SpellDefinition shape — no modes, HasVariableX, no target requests.
/// - Resolve: controller's battlefield gains exactly X 1/1 white Warrior tokens.
/// - X = 0 creates zero tokens (CR 107.3).
/// - Each token: IsToken, Name "Warrior", Power 1, Toughness 1, white, Warrior subtype.
/// </summary>
[Trait("Color", "W")]
public class SecureTheWastesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SecureTheWastes_HasInstantShape_White_AtCostXW()
    {
        var card = SecureTheWastesFactory.Create(_alice);

        card.Name.Should().Be("Secure the Wastes");
        card.ManaCost.Should().Be("{X}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SecureTheWastes_SpellDefinition_HasVariableX_NoTargets_NoModes()
    {
        var def = SecureTheWastesFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeTrue("{X}{W} — X is chosen at cast time (CR 107.3)");
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty("Secure the Wastes has no targets (CR 115.1)");
    }

    // -----------------------------------------------------------------------
    // Resolve — token creation
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void SecureTheWastes_Resolve_CreatesXWarriorTokens(int x)
    {
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();

        var def = SecureTheWastesFactory.BuildSpellDefinition(_alice);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: x,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty));

        foreach (var effect in effects)
        {
            effect.Execute();
        }

        _alice.Zones.Battlefield.GetCards().Should().HaveCount(
            x, $"Secure the Wastes with X={x} creates exactly X tokens");
    }

    [Fact]
    public void SecureTheWastes_Resolve_EachToken_IsOnePowerOneToughness_WhiteWarrior()
    {
        var def = SecureTheWastesFactory.BuildSpellDefinition(_alice);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: 3,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty));

        foreach (var effect in effects)
        {
            effect.Execute();
        }

        var tokens = _alice.Zones.Battlefield.GetCards().Cast<Creature>().ToList();
        tokens.Should().HaveCount(3);

        foreach (var token in tokens)
        {
            token.Name.Should().Be("Warrior");
            token.IsToken.Should().BeTrue("CR 111 — Secure the Wastes creates tokens");
            token.BasePower.Should().Be(1);
            token.BaseToughness.Should().Be(1);
            token.HasSubtype(CardSubtype.Warrior).Should().BeTrue(
                "CR 111.4 — token carries the Warrior creature subtype");
            token.Controller.Should().BeSameAs(_alice);
            CardColors.GetColors(token).Should().Contain(ManaColor.White,
                "CR 111.4 — the token is explicitly white");
        }
    }
}
