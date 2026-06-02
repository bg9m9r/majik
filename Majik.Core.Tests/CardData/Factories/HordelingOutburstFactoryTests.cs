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
/// Unit tests for <see cref="HordelingOutburstFactory"/>.
///
/// Oracle text: "Create three 1/1 red Goblin creature tokens." ({1}{R}{R} Sorcery)
///
/// Covers:
/// - Card identity (Sorcery, {1}{R}{R}, red, CMC 3, owner/controller).
/// - NamedCardFactory dispatch by name.
/// - SpellDefinition shape — no modes, no X, no target requests.
/// - Resolve: controller's battlefield gains exactly three 1/1 red Goblin tokens.
/// - Each token: IsToken, Name "Goblin", Power 1, Toughness 1, red, Goblin subtype.
/// </summary>
[Trait("Color", "R")]
public class HordelingOutburstFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HordelingOutburst_HasSorceryShape_Red_AtCost1RR()
    {
        var card = HordelingOutburstFactory.Create(_alice);

        card.Name.Should().Be("Hordeling Outburst");
        card.ManaCost.Should().Be("{1}{R}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HordelingOutburst_SpellDefinition_HasNoTargets_NoModes_NoX()
    {
        var def = HordelingOutburstFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve — token creation
    // -----------------------------------------------------------------------

    [Fact]
    public void HordelingOutburst_Resolve_CreatesThreeGoblinTokensOnCastersBattlefield()
    {
        // Battlefield starts empty.
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();

        var effects = HordelingOutburstFactory.BuildResolveEffect(_alice);
        effects.Should().HaveCount(1, "one atomic effect produces all three tokens");
        effects.Single().Execute();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().HaveCount(3, "Hordeling Outburst creates exactly three tokens");
    }

    [Fact]
    public void HordelingOutburst_Resolve_EachToken_IsOnePowerOneToughness_RedGoblin()
    {
        var effects = HordelingOutburstFactory.BuildResolveEffect(_alice);
        effects.Single().Execute();

        var tokens = _alice.Zones.Battlefield.GetCards().Cast<Creature>().ToList();
        tokens.Should().HaveCount(3);

        foreach (var token in tokens)
        {
            token.Name.Should().Be("Goblin");
            token.IsToken.Should().BeTrue("CR 111 — Hordeling Outburst creates tokens");
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
