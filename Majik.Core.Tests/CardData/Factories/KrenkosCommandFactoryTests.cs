using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KrenkosCommandFactory"/>.
///
/// Oracle text: "Create two 1/1 red Goblin creature tokens." ({1}{R} Sorcery)
///
/// Covers:
/// - Card identity (Sorcery, {1}{R}, red, CMC 2, owner/controller).
/// - NamedCardFactory dispatch by name.
/// - SpellDefinition shape — no modes, no X, no target requests.
/// - Resolve: controller's battlefield gains exactly two 1/1 red Goblin tokens.
/// - Each token: IsToken, Name "Goblin", Power 1, Toughness 1, red, Goblin subtype.
/// </summary>
public class KrenkosCommandFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void KrenkosCommand_HasSorceryShape_Red_AtCost1R()
    {
        var card = KrenkosCommandFactory.Create(_alice);

        card.Name.Should().Be("Krenko's Command");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsKrenkosCommandShape()
    {
        var dispatched = NamedCardFactory.Create("Krenko's Command", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Krenko's Command");
        dispatched.ManaCost.Should().Be("{1}{R}");
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void KrenkosCommand_SpellDefinition_HasNoTargets_NoModes_NoX()
    {
        var def = KrenkosCommandFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve — token creation
    // -----------------------------------------------------------------------

    [Fact]
    public void KrenkosCommand_Resolve_CreatesTwoGoblinTokensOnCastersBattlefield()
    {
        // Battlefield starts empty.
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();

        var effects = KrenkosCommandFactory.BuildResolveEffect(_alice);
        effects.Should().HaveCount(1, "one atomic effect produces both tokens");
        effects.Single().Execute();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().HaveCount(2, "Krenko's Command creates exactly two tokens");
    }

    [Fact]
    public void KrenkosCommand_Resolve_EachToken_IsOnePowerOneToughness_RedGoblin()
    {
        var effects = KrenkosCommandFactory.BuildResolveEffect(_alice);
        effects.Single().Execute();

        var tokens = _alice.Zones.Battlefield.GetCards().Cast<Creature>().ToList();
        tokens.Should().HaveCount(2);

        foreach (var token in tokens)
        {
            token.Name.Should().Be("Goblin");
            token.IsToken.Should().BeTrue("CR 111 — Krenko's Command creates tokens");
            token.BasePower.Should().Be(1);
            token.BaseToughness.Should().Be(1);
            token.HasSubtype(CardSubtype.Goblin).Should().BeTrue(
                "CR 111.4 — token carries the Goblin creature subtype");
            token.Controller.Should().BeSameAs(_alice);
            CardColors.GetColors(token).Should().Contain(ManaColor.Red,
                "CR 111.4 — the token is explicitly red");
        }
    }
}
