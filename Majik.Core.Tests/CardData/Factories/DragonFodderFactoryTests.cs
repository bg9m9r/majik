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
/// Unit tests for <see cref="DragonFodderFactory"/>.
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
[Trait("Color", "R")]
public class DragonFodderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DragonFodder_HasSorceryShape_Red_AtCost1R()
    {
        var card = DragonFodderFactory.Create(_alice);

        card.Name.Should().Be("Dragon Fodder");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void DragonFodder_SpellDefinition_HasNoTargets_NoModes_NoX()
    {
        var def = DragonFodderFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve — token creation
    // -----------------------------------------------------------------------

    [Fact]
    public void DragonFodder_Resolve_CreatesTwoGoblinTokensOnCastersBattlefield()
    {
        // Battlefield starts empty.
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();

        var effects = DragonFodderFactory.BuildResolveEffect(_alice);
        effects.Should().HaveCount(1, "one atomic effect produces both tokens");
        effects.Single().Execute();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().HaveCount(2, "Dragon Fodder creates exactly two tokens");
    }

    [Fact]
    public void DragonFodder_Resolve_EachToken_IsOnePowerOneToughness_RedGoblin()
    {
        var effects = DragonFodderFactory.BuildResolveEffect(_alice);
        effects.Single().Execute();

        var tokens = _alice.Zones.Battlefield.GetCards().Cast<Creature>().ToList();
        tokens.Should().HaveCount(2);

        foreach (var token in tokens)
        {
            token.Name.Should().Be("Goblin");
            token.IsToken.Should().BeTrue("CR 111 — Dragon Fodder creates tokens");
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
