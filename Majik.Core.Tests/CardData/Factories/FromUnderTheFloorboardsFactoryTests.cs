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
/// Unit tests for <see cref="FromUnderTheFloorboardsFactory"/>.
///
/// Oracle text (verified against Scryfall 2026-06-10):
///   "Madness {X}{B}{B}
///    Create three tapped 2/2 black Zombie creature tokens and you gain 3 life.
///    If this spell's madness cost was paid, instead create X of those tokens
///    and you gain X life." ({3}{B}{B} Sorcery)
///
/// Madness itself is intrinsic (MadnessCatalog + Fx.DiscardCard funnel) and is
/// covered by MadnessDiscardFunnelTests — NOT re-tested here. These tests cover
/// only the card's non-madness spell body:
/// - Card identity (Sorcery, {3}{B}{B}, black, owner/controller).
/// - SpellDefinition shape — no modes, HasVariableX, no target requests.
/// - Normal cast (X null) → create 3 tapped 2/2 black Zombie tokens + gain 3 life.
/// - Madness cast (X set) → create X tapped tokens + gain X life.
/// - X = 0 creates zero tokens and gains zero life (CR 107.3).
/// - Each token: IsToken, Name "Zombie", 2/2, black, Zombie subtype, TAPPED.
/// </summary>
[Trait("Color", "B")]
public class FromUnderTheFloorboardsFactoryTests
{
    private Player NewAlice() => new("Alice", 20);

    private static IReadOnlyList<Creature> Resolve(Player alice, int? x)
    {
        var def = FromUnderTheFloorboardsFactory.BuildSpellDefinition(alice);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: x,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty));

        foreach (var effect in effects)
        {
            effect.Execute();
        }

        return alice.Zones.Battlefield.GetCards().Cast<Creature>().ToList();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FromUnderTheFloorboards_HasSorceryShape_Black_AtCost3BB()
    {
        var alice = NewAlice();
        var card = FromUnderTheFloorboardsFactory.Create(alice);

        card.Name.Should().Be("From Under the Floorboards");
        card.ManaCost.Should().Be("{3}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void FromUnderTheFloorboards_SpellDefinition_HasVariableX_NoTargets_NoModes()
    {
        var def = FromUnderTheFloorboardsFactory.BuildSpellDefinition(NewAlice());

        def.HasVariableX.Should().BeTrue(
            "the madness {X}{B}{B} cast chooses X at cast time (CR 107.3 / CR 702.35)");
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty(
            "From Under the Floorboards has no targets (CR 115.1)");
    }

    // -----------------------------------------------------------------------
    // Normal cast (no madness) — X null → 3 tokens, gain 3 life
    // -----------------------------------------------------------------------

    [Fact]
    public void FromUnderTheFloorboards_NormalCast_CreatesThreeTokens_AndGainsThreeLife()
    {
        var alice = NewAlice();
        alice.Zones.Battlefield.GetCards().Should().BeEmpty();

        // Normal cast — X is null (the printed {3}{B}{B} cost has no X).
        var tokens = Resolve(alice, x: null);

        tokens.Should().HaveCount(3,
            "a normal cast creates exactly three tokens (oracle: 'Create three ...')");
        alice.LifeTotal.Should().Be(23, "a normal cast gains 3 life (CR 119.3)");
    }

    // -----------------------------------------------------------------------
    // Madness cast — X set → X tokens, gain X life
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(7, 7)]
    public void FromUnderTheFloorboards_MadnessCast_CreatesXTokens_AndGainsXLife(int x, int expectedTokens)
    {
        var alice = NewAlice();

        // Madness cast — X is supplied (the {X}{B}{B} madness cost was paid).
        var tokens = Resolve(alice, x: x);

        tokens.Should().HaveCount(expectedTokens,
            $"a madness cast with X={x} creates exactly X tokens (oracle: 'instead create X ...')");
        alice.LifeTotal.Should().Be(20 + x, $"a madness cast with X={x} gains X life");
    }

    // -----------------------------------------------------------------------
    // Token characteristics
    // -----------------------------------------------------------------------

    [Fact]
    public void FromUnderTheFloorboards_EachToken_IsTwoTwo_BlackZombie_Tapped()
    {
        var alice = NewAlice();
        var tokens = Resolve(alice, x: null);

        tokens.Should().HaveCount(3);

        foreach (var token in tokens)
        {
            token.Name.Should().Be("Zombie");
            token.IsToken.Should().BeTrue("CR 111 — the spell creates tokens");
            token.BasePower.Should().Be(2);
            token.BaseToughness.Should().Be(2);
            token.HasSubtype(CardSubtype.Zombie).Should().BeTrue(
                "CR 111.4 — token carries the Zombie creature subtype");
            token.Controller.Should().BeSameAs(alice);
            CardColors.GetColors(token).Should().Contain(ManaColor.Black,
                "CR 111.4 — the token is explicitly black");
            token.IsTapped.Should().BeTrue(
                "CR 110.5h — the tokens are created tapped");
        }
    }
}
