using FluentAssertions;
using Majik.Core.Abilities;
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
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Midnight Haunting ({2}{W} Instant).
///
/// Oracle text: "Create two 1/1 white Spirit creature tokens with flying."
///
/// Coverage:
/// - Card identity (name, type, mana cost {2}{W}, colour white, mana value 3).
/// - <see cref="NamedCardFactory"/> dispatch returns an <see cref="Instant"/>.
/// - Resolve effect creates exactly two 1/1 white Spirit tokens with Flying
///   under the caster.
/// - Each token: IsToken, Power 1, Toughness 1, Spirit subtype, white colour,
///   Flying keyword ability.
/// - No targets declared (pure create-two spell, no TargetRequests).
/// </summary>
public class MidnightHauntingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MidnightHaunting_Identity_InstantWhite_ManaCost2W()
    {
        var c = MidnightHauntingFactory.Create(_alice);

        c.Name.Should().Be("Midnight Haunting");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MidnightHaunting_ManaCost_HasManaValue3()
    {
        var c = MidnightHauntingFactory.Create(_alice);
        c.ManaCostValue.TotalValue.Should().Be(3, "mana value of {2}{W} is 3");
    }

    [Fact]
    public void MidnightHaunting_IsWhite()
    {
        var c = MidnightHauntingFactory.Create(_alice);
        CardColors.GetColors(c).Should().Contain(ManaColor.White);
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_MidnightHaunting()
    {
        var c = NamedCardFactory.Create("Midnight Haunting", _alice);

        c.Should().BeOfType<Instant>();
        c.Name.Should().Be("Midnight Haunting");
    }

    // -----------------------------------------------------------------------
    // Spell definition — no target requests
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_HasNoTargetRequests()
    {
        var def = MidnightHauntingFactory.BuildSpellDefinition(_alice);

        def.TargetRequests.Should().BeEmpty(
            "Midnight Haunting has no targets — it just creates two tokens");
    }

    // -----------------------------------------------------------------------
    // Resolve effect — two 1/1 white Spirit tokens with Flying
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_CreatesTwoWhiteSpiritTokensWithFlying()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var effects = MidnightHauntingFactory.BuildResolveEffects(_alice, zones);
        effects.Should().ContainSingle("Midnight Haunting resolves as a single grouped effect");

        foreach (var effect in effects) effect.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();

        tokens.Should().HaveCount(MidnightHauntingFactory.TokensCreated,
            "Midnight Haunting creates exactly two tokens");

        tokens.Should().AllSatisfy(t =>
        {
            t.Name.Should().Be("Spirit");
            t.BasePower.Should().Be(MidnightHauntingFactory.TokenPower);
            t.BaseToughness.Should().Be(MidnightHauntingFactory.TokenToughness);
            t.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
            t.IsToken.Should().BeTrue();
            t.Abilities.OfType<KeywordAbility>()
                .Should().Contain(k => k.Keyword == "Flying",
                    "each Spirit token has flying");
            t.TokenColorsOverride.Should().NotBeNull();
            t.TokenColorsOverride!.Should().Contain(ManaColor.White,
                "Spirit tokens are white per the printed clause (CR 105 / 111.4)");
        });
    }

    [Fact]
    public void Resolve_TokensAreUnderCasterControl()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var effects = MidnightHauntingFactory.BuildResolveEffects(_alice, zones);
        foreach (var effect in effects) effect.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(MidnightHauntingFactory.TokensCreated);
        tokens.Should().AllSatisfy(t =>
            t.Controller.Should().BeSameAs(_alice,
                "caster controls the tokens they create"));
    }
}
