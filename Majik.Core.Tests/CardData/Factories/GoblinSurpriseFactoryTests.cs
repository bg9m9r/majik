using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GoblinSurpriseFactory"/>.
///
/// Oracle text (verified against Scryfall): ({2}{R} Instant)
///   "Choose one —
///     • Creatures you control get +2/+0 until end of turn.
///     • Create two 1/1 red Goblin creature tokens."
///
/// Covers the card's UNIQUE behaviour (the two-mode choose-one body):
/// - Identity: Instant, {2}{R}, red, mana value 3.
/// - SpellDefinition shape — two modes, no X, no real targets.
/// - Mode 0 resolve: every creature the caster controls gets +2/+0 until EOT.
/// - Mode 1 resolve: exactly two 1/1 red Goblin tokens enter under the caster.
/// - Choose-one cap: picking both indices honours only the first (CR 700.2d).
///
/// (Dispatch + well-formedness are covered for every card by
/// CardFactoryContractTests — not re-asserted here.)
/// </summary>
[Trait("Color", "R")]
public class GoblinSurpriseFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (non-vanilla stats → single *_Identity assert)
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinSurprise_HasInstantShape_Red_AtCost2R()
    {
        var card = GoblinSurpriseFactory.Create(_alice);

        card.Name.Should().Be("Goblin Surprise");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — modal structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinSurprise_SpellDefinition_HasTwoModes_NoX_NoRealTargets()
    {
        var def = GoblinSurpriseFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().HaveCount(2);
        // CR 601.2c — both modes are targetless; the requests are zero-target
        // placeholders so the cast never prompts for a target.
        def.TargetRequests.Should().OnlyContain(r => r.MaxTargets == 0);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — creatures you control get +2/+0 until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinSurprise_Mode0_PumpsEachControlledCreature_By2_0()
    {
        var effects = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "", 2, 2, null, null)
        {
            ActiveEffects = effects,
        };
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var elf = new Creature("Llanowar Elves", "", 1, 1, null, null)
        {
            ActiveEffects = effects,
        };
        elf.SetOwner(_alice);
        elf.SetController(_alice);
        elf.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(elf);

        var effect = GoblinSurpriseFactory.BuildPumpEffect(_alice);
        effect.Execute();

        // CR 613.1c Layer 7c — +2/+0; toughness unchanged.
        var bearChars = effects.Compute(bear);
        bearChars.Power.Should().Be(4);
        bearChars.Toughness.Should().Be(2);
        var elfChars = effects.Compute(elf);
        elfChars.Power.Should().Be(3);
        elfChars.Toughness.Should().Be(1);

        // CR 514.2 — the pump expires at cleanup.
        effects.ExpireEndOfTurn();
        effects.Compute(bear).Power.Should().Be(2);
        effects.Compute(elf).Power.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Mode 1 — create two 1/1 red Goblin creature tokens
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinSurprise_Mode1_CreatesTwoOnePowerRedGoblinTokens()
    {
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();

        var effect = GoblinSurpriseFactory.BuildTokensEffect(_alice);
        effect.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards().Cast<Creature>().ToList();
        tokens.Should().HaveCount(2, "Goblin Surprise creates exactly two tokens");

        foreach (var token in tokens)
        {
            token.Name.Should().Be("Goblin");
            token.IsToken.Should().BeTrue("CR 111 — these are tokens");
            token.BasePower.Should().Be(1);
            token.BaseToughness.Should().Be(1);
            token.HasSubtype(CardSubtype.Goblin).Should().BeTrue(
                "CR 111.4 — token carries the Goblin creature subtype");
            token.Controller.Should().BeSameAs(_alice);
            CardColors.GetColors(token).Should().Contain(ManaColor.Red,
                "CR 111.4 — the token is explicitly red");
        }
    }

    // -----------------------------------------------------------------------
    // Choose-one cap — CR 700.2d
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinSurprise_ChooseOne_HonoursOnlyFirstPickedMode()
    {
        var def = GoblinSurpriseFactory.BuildSpellDefinition(_alice);

        // Adversarially pass BOTH mode indices — a Choose-one spell must take
        // only the first (CR 700.2d pick-count cap).
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            ModeIndexes: new[] { GoblinSurpriseFactory.ModeTokens, GoblinSurpriseFactory.ModePump });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1, "Choose-one caps the resolved modes at one (CR 700.2d)");

        // First pick was the tokens mode → executing it makes two tokens, and
        // no pump is applied.
        effects.Single().Execute();
        _alice.Zones.Battlefield.GetCards().Should().HaveCount(2);
    }
}
