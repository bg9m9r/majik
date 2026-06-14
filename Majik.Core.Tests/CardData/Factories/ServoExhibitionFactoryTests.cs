using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ServoExhibitionFactory"/>.
///
/// Oracle text ({1}{W} Sorcery, verified against Scryfall):
///   "Create two 1/1 colorless Servo artifact creature tokens."
///
/// Covers:
/// - Card identity (Sorcery, {1}{W}, white card, CMC 2, owner/controller).
/// - SpellDefinition shape — no modes, no X, no target requests.
/// - Resolve effect creates exactly two 1/1 Servo tokens under the caster, each
///   a colourless artifact creature with the Servo subtype (CR 111 / 111.1 /
///   111.4 — the tokens are colourless even though the card itself is white).
///
/// The card itself is white (white pip in its mana cost, CR 105.2); the *tokens*
/// it makes are colourless. This colour split is the card's distinguishing
/// behaviour vs. the white-Human analogue Gather the Townsfolk.
/// </summary>
[Trait("Color", "W")]
public class ServoExhibitionFactoryTests
{
    private static Player NewPlayer(string name, int life = 20) => new(name, life);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ServoExhibition_HasSorceryShape_White_AtCost1W()
    {
        var alice = NewPlayer("Alice");
        var card = ServoExhibitionFactory.Create(alice);

        card.Name.Should().Be("Servo Exhibition");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        // CR 105.2 — the card is white (white pip in {1}{W}). The tokens it
        // makes are colourless; the card is not.
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ServoExhibition_SpellDefinition_HasNoTargets_NoModes_NoX()
    {
        var alice = NewPlayer("Alice");

        var def = ServoExhibitionFactory.BuildSpellDefinition(alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve — two 1/1 colourless Servo artifact creature tokens
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_CreatesTwoColorlessServoArtifactCreatureTokens()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var alice = NewPlayer("Alice");

        var effects = ServoExhibitionFactory.BuildResolveEffect(alice, zones);
        effects.Should().ContainSingle(
            "Servo Exhibition resolves as a single grouped effect");

        foreach (var effect in effects) effect.Execute();

        var tokens = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(ServoExhibitionFactory.TokenCount,
            "Servo Exhibition creates exactly two tokens (CR 111)");

        tokens.Should().AllSatisfy(t =>
        {
            t.Name.Should().Be("Servo");
            t.BasePower.Should().Be(ServoExhibitionFactory.TokenPower);
            t.BaseToughness.Should().Be(ServoExhibitionFactory.TokenToughness);
            t.HasSubtype(CardSubtype.Servo).Should().BeTrue(
                "CR 111.4 — Servo creature subtype");
            // CR 111.1 — Servo tokens are artifact creatures.
            t.HasType(CardType.Artifact).Should().BeTrue();
            t.HasType(CardType.Creature).Should().BeTrue();
            // CR 105 / 111.4 — "colorless": the token has no colours, even
            // though the spell that made it is white.
            CardColors.GetColors(t).Should().BeEmpty(
                "the Servo token is colourless (CR 105 / 111.4)");
            t.Controller.Should().BeSameAs(alice,
                "caster controls the tokens they create");
        });
    }
}
