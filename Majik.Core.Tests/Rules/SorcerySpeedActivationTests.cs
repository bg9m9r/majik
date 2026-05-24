using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.Factories;
using Majik.Core.Players;
using Majik.Core.Rules;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// CR 117.1a / 307.5 — "Activate only as a sorcery" gate.
/// <para>
/// Verifies that the <see cref="ActionValidator"/> rejects an
/// <see cref="ActivateAbilityAction"/> whose underlying
/// <see cref="IActivatedAbility.IsSorcerySpeed"/> is true unless the
/// caller signals sorcery-speed timing is available
/// (<see cref="ActivateAbilityAction.SorcerySpeedAvailable"/>: the
/// controller's main phase with an empty stack).
/// </para>
/// <para>
/// The validator is intentionally stateless — these tests model the
/// four timing windows explicitly via the <c>sorcerySpeedAvailable</c>
/// flag, mirroring the cast-side <see cref="CastSpellAction"/> pattern
/// already exercised in <c>ActionValidatorTimingTests</c>.
/// </para>
/// </summary>
public class SorcerySpeedActivationTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------- Walking Ballista {4}: put +1/+1 counter --------
    // Walking Ballista is JSON-driven; its first ability flags
    // sorcerySpeed: true via the JSON definition + CardDefinitionFactory
    // path. These four tests sweep the canonical "own main phase, empty
    // stack" timing window.

    [Fact]
    public void WalkingBallista_ShootAbility_LegalInOwnMainPhaseWithEmptyStack()
    {
        var ballista = WalkingBallistaFactory.Create(_alice);
        var sorceryAbility = GetSorceryActivated(ballista);

        var action = new ActivateAbilityAction(sorceryAbility, _alice, sorcerySpeedAvailable: true);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeTrue(
            "the {4}: put-counter ability is sorcery-speed and the timing window matches");
    }

    [Fact]
    public void WalkingBallista_ShootAbility_RejectedDuringCombat()
    {
        var ballista = WalkingBallistaFactory.Create(_alice);
        var sorceryAbility = GetSorceryActivated(ballista);

        // Combat is not a main phase — sorcery-speed unavailable.
        var action = new ActivateAbilityAction(sorceryAbility, _alice, sorcerySpeedAvailable: false);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("307.5");
    }

    [Fact]
    public void WalkingBallista_ShootAbility_RejectedOnOpponentsTurn()
    {
        var ballista = WalkingBallistaFactory.Create(_alice);
        var sorceryAbility = GetSorceryActivated(ballista);

        // On Bob's turn Alice is never the active player → sorcery-speed
        // window is unavailable for an Alice-controlled activation.
        var action = new ActivateAbilityAction(sorceryAbility, _alice, sorcerySpeedAvailable: false);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("307.5");
    }

    [Fact]
    public void WalkingBallista_ShootAbility_RejectedWithNonEmptyStack()
    {
        var ballista = WalkingBallistaFactory.Create(_alice);
        var sorceryAbility = GetSorceryActivated(ballista);

        // Even in Alice's main phase, a non-empty stack closes the
        // sorcery-speed window (CR 307.5).
        var action = new ActivateAbilityAction(sorceryAbility, _alice, sorcerySpeedAvailable: false);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("307.5");
    }

    // -------- Wishclaw Talisman {T}, Pay 3 life: tutor + give away ------

    [Fact]
    public void WishclawTalisman_Tutor_LegalAtSorcerySpeed()
    {
        var talisman = WishclawTalismanFactory.Create(_alice);
        var ability = GetActivated(talisman);

        ability.IsSorcerySpeed.Should().BeTrue(
            "Wishclaw Talisman's printed activation is 'Activate only as a sorcery'");

        var result = new ActionValidator().ValidateAction(
            new ActivateAbilityAction(ability, _alice, sorcerySpeedAvailable: true));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void WishclawTalisman_Tutor_RejectedAtInstantSpeed()
    {
        var talisman = WishclawTalismanFactory.Create(_alice);
        var ability = GetActivated(talisman);

        var result = new ActionValidator().ValidateAction(
            new ActivateAbilityAction(ability, _alice, sorcerySpeedAvailable: false));

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("307.5");
        result.ErrorMessage.Should().Contain("Wishclaw Talisman");
    }

    // -------- Tasigur, the Golden Fang {B}{G}{U}: opponent picks ------

    [Fact]
    public void Tasigur_GraveyardActivation_LegalAtSorcerySpeed()
    {
        var tasigur = TasigurTheGoldenFangFactory.Create(_alice);
        var ability = GetActivated(tasigur);

        ability.IsSorcerySpeed.Should().BeTrue(
            "Tasigur's {B}{G}{U} activation is 'Activate only as a sorcery'");

        var result = new ActionValidator().ValidateAction(
            new ActivateAbilityAction(ability, _alice, sorcerySpeedAvailable: true));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Tasigur_GraveyardActivation_RejectedAtInstantSpeed()
    {
        var tasigur = TasigurTheGoldenFangFactory.Create(_alice);
        var ability = GetActivated(tasigur);

        var result = new ActionValidator().ValidateAction(
            new ActivateAbilityAction(ability, _alice, sorcerySpeedAvailable: false));

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("307.5");
        result.ErrorMessage.Should().Contain("Tasigur");
    }

    // -------- Backward-compat: instant-speed abilities never gate -------

    [Fact]
    public void InstantSpeedAbility_NotGated_RegardlessOfTimingFlag()
    {
        // A vanilla activated ability with sorcerySpeed: false (the
        // default) must remain legal even when the caller signals
        // sorcery-speed isn't available — instant-speed activations
        // bypass the gate entirely.
        var card = new Creature("Stub", "", 1, 1) { Owner = _alice };
        card.SetController(_alice);
        var ability = new ActivatedAbility(source: card, controller: _alice);

        ability.IsSorcerySpeed.Should().BeFalse();

        var result = new ActionValidator().ValidateAction(
            new ActivateAbilityAction(ability, _alice, sorcerySpeedAvailable: false));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ActivateAbilityAction_DefaultsToSorcerySpeedAvailable()
    {
        // The single-arg constructor used by the bulk of the existing
        // suite defaults SorcerySpeedAvailable=true so legacy callers
        // don't suddenly fail the new gate. Reaffirmed here so the
        // backward-compat contract is locked in.
        var card = new Creature("Stub2", "", 1, 1) { Owner = _alice };
        card.SetController(_alice);
        var ability = new ActivatedAbility(source: card, controller: _alice, sorcerySpeed: true);
        var action = new ActivateAbilityAction(ability, _alice);

        action.SorcerySpeedAvailable.Should().BeTrue();
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    // ----------- helpers -----------

    private static IActivatedAbility GetActivated(ICard card)
    {
        var ability = card.Abilities.OfType<IActivatedAbility>().FirstOrDefault();
        ability.Should().NotBeNull(
            $"{card.Name} should have at least one activated ability");
        return ability!;
    }

    /// <summary>
    /// Walking Ballista carries two activated abilities; the first
    /// ({4}: counter) is sorcery-speed, the second (remove counter:
    /// damage) is instant. Pick the sorcery-speed one explicitly.
    /// </summary>
    private static IActivatedAbility GetSorceryActivated(ICard card)
    {
        var ability = card.Abilities.OfType<IActivatedAbility>()
            .FirstOrDefault(a => a.IsSorcerySpeed);
        ability.Should().NotBeNull(
            $"{card.Name} should expose at least one sorcery-speed activated ability");
        return ability!;
    }
}
