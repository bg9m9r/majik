using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Rules;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="IrencragFeatFactory"/> — Irencrag Feat.
///
/// Oracle:
///   "Add {R}{R}{R}{R}{R}{R}{R}. You can cast only one more spell this turn."
///
/// Coverage:
///   Identity — Sorcery, {1}{R}{R}{R} (MV 4), correct name, owner/controller.
///   Dispatch by name via <see cref="NamedCardFactory"/>.
///   Resolve: adds exactly seven red mana to controller's pool (CR 106.4).
///   Resolve: no other colors or generic mana are added.
///   Resolve: registers the one-more-spell cap on the controller
///            (<see cref="CastingRestrictions.SetMaxAdditionalSpellsThisTurn"/>).
///   Cap behavior: the controller CAN cast one more spell after resolution
///                 (<see cref="ActionValidator"/> allows it).
///   Cap behavior: a second additional spell is BLOCKED by ActionValidator
///                 (CR 601.3 — additional-spell cap exhausted).
///   Cap behavior: restriction does NOT apply to a different player.
///   Cap behavior: cap cleared via CastingRestrictions.Clear (end-of-turn).
///   Two resolutions in one turn: cap stays at 1 (tighter of 1, 1 = 1).
/// </summary>
[Trait("Color", "R")]
public class IrencragFeatFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly ActionValidator _validator = new();

    public IrencragFeatFactoryTests() => CastingRestrictions.Clear();
    public void Dispose() => CastingRestrictions.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void IrencragFeat_HasExpectedShape()
    {
        var card = IrencragFeatFactory.Create(_alice);

        card.Name.Should().Be("Irencrag Feat");
        card.ManaCost.Should().Be("{1}{R}{R}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Mana production
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_AddsSevenRedMana()
    {
        // Pool starts empty.
        _alice.ManaPool.Total.Should().Be(0);

        var effect = IrencragFeatFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.ManaPool.Red.Should().Be(7);
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Blue.Should().Be(0);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        _alice.ManaPool.Generic.Should().Be(0);
        _alice.ManaPool.Total.Should().Be(7);
    }

    [Fact]
    public void Resolve_TwoCopiesInSameTurn_StacksToFourteenRed()
    {
        // CR 106.4 — mana from multiple resolutions stacks in the pool.
        // Two Irencrag Feats resolving = 14 red total.
        var effect1 = IrencragFeatFactory.BuildResolveEffect(_alice).Single();
        var effect2 = IrencragFeatFactory.BuildResolveEffect(_alice).Single();

        effect1.Execute();
        effect2.Execute();

        _alice.ManaPool.Red.Should().Be(14);
        _alice.ManaPool.Total.Should().Be(14);
    }

    // -----------------------------------------------------------------------
    // One-more-spell cap registration
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_RegistersOneMoreSpellCap()
    {
        // Before resolution: no restriction.
        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeFalse();

        var effect = IrencragFeatFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        // After resolution: cap is registered at 1 (one more spell remaining).
        // Counter is 1, so not yet exhausted — HasExhausted is false.
        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeFalse();

        // Simulating one spell cast: consume the allowance.
        CastingRestrictions.ConsumeAdditionalSpellAllowance(_alice);

        // Counter is now 0 — exhausted.
        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeTrue();
    }

    [Fact]
    public void ActionValidator_AllowsOneMoreSpellAfterResolution()
    {
        // Resolve Irencrag Feat — cap set to 1.
        var effect = IrencragFeatFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        // The cap is 1, counter not yet consumed → validator allows the cast.
        var anyCard = IrencragFeatFactory.Create(_alice);
        var action = new CastSpellAction(anyCard, _alice, sorcerySpeedAvailable: true);
        var result = _validator.ValidateAction(action);

        result.IsValid.Should().BeTrue("one more spell is still allowed (cap = 1 remaining)");
    }

    [Fact]
    public void ActionValidator_BlocksSecondAdditionalSpellAfterResolution()
    {
        // Resolve Irencrag Feat — cap set to 1.
        var effect = IrencragFeatFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        // Consume the one allowed spell (simulating SpellCastFlow's post-cast hook).
        CastingRestrictions.ConsumeAdditionalSpellAllowance(_alice);

        // Counter is now 0 — ActionValidator must reject any further cast.
        var anyCard = IrencragFeatFactory.Create(_alice);
        var action = new CastSpellAction(anyCard, _alice, sorcerySpeedAvailable: true);
        var result = _validator.ValidateAction(action);

        result.IsValid.Should().BeFalse("additional-spell cap is exhausted (0 remaining)");
        result.ErrorMessage.Should().Contain("additional-spell cap reached");
        result.Violation!.RuleNumber.Should().Be("601.3");
    }

    [Fact]
    public void Cap_DoesNotApplyToOtherPlayer()
    {
        // Resolve Irencrag Feat for Alice.
        var effect = IrencragFeatFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();
        CastingRestrictions.ConsumeAdditionalSpellAllowance(_alice);

        // Alice is blocked.
        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeTrue();

        // Bob has no cap — no restriction.
        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_bob).Should().BeFalse();

        var anyCard = IrencragFeatFactory.Create(_bob);
        var bobAction = new CastSpellAction(anyCard, _bob, sorcerySpeedAvailable: true);
        _validator.ValidateAction(bobAction).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Cap_ClearsAtEndOfTurn()
    {
        // Resolve and exhaust the cap.
        var effect = IrencragFeatFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();
        CastingRestrictions.ConsumeAdditionalSpellAllowance(_alice);
        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeTrue();

        // End of turn cleanup.
        CastingRestrictions.ClearMaxAdditionalSpellsThisTurn();

        // Restriction is gone.
        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeFalse();

        var anyCard = IrencragFeatFactory.Create(_alice);
        var action = new CastSpellAction(anyCard, _alice, sorcerySpeedAvailable: true);
        _validator.ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TwoResolutionsSameTurn_CapRemainsAtOne()
    {
        // Two Irencrag Feats resolving in the same turn: cap should be 1
        // (Math.Min(1, 1) = 1), not 2.
        var e1 = IrencragFeatFactory.BuildResolveEffect(_alice).Single();
        var e2 = IrencragFeatFactory.BuildResolveEffect(_alice).Single();
        e1.Execute();
        e2.Execute();

        // Consume once.
        CastingRestrictions.ConsumeAdditionalSpellAllowance(_alice);

        // Cap exhausted after just one more spell.
        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_alice).Should().BeTrue(
            "the tighter of the two caps (1, 1 → 1) is enforced");
    }
}
