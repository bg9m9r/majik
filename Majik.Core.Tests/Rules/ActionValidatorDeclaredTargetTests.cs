using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// CR 601.2c / 602.1b — declared-target-type legality gate in
/// <see cref="ActionValidator"/>. Pays down the
/// "target-legality-in-actionvalidator" deferral: the validator now filters a
/// chosen object against the declared <see cref="TargetSpec"/>'s type / zone /
/// controller predicate at DECLARATION time (Forked Bolt, Demonic Dread,
/// Rogue's Passage and the broad targeted-spell tail), instead of relying only
/// on the resolution-time CR 608.2b fizzle.
///
/// Shares the same legality definition (<see cref="TargetLegality.IsLegal"/>)
/// as the resolution-time recheck, so validation-time and resolution-time
/// legality cannot drift.
///
/// Joins the <see cref="ActivatedAbilityRestrictionsCollection"/> non-parallel
/// collection (the activation paths consult the process-global suppression
/// registry).
/// </summary>
[Collection(nameof(ActivatedAbilityRestrictionsCollection))]
public class ActionValidatorDeclaredTargetTests
{
    private readonly ActionValidator _validator;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ActionValidatorDeclaredTargetTests()
    {
        ActivatedAbilityRestrictions.Clear();
        _validator = new ActionValidator();
    }

    private Creature BattlefieldBear(Player owner)
    {
        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(bear);
        return bear;
    }

    // ---- Cast path -------------------------------------------------------

    [Fact]
    public void Cast_TargetCreature_LegalCreature_OnBattlefield_IsValid()
    {
        // Forked Bolt / Demonic Dread shape: "target creature" pointed at a
        // creature on the battlefield is legal at declaration (CR 601.2c).
        var bolt = new Sorcery("Forked Bolt", "R") { Owner = _alice };
        var bear = BattlefieldBear(_bob);
        var spec = new TargetSpec("target creature").Creatures();

        var action = new CastSpellAction(
            bolt, _alice, sorcerySpeedAvailable: true, fromZone: ZoneType.Hand,
            targets: new object[] { bear }, targetSpec: spec);

        _validator.ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Cast_TargetCreature_PointedAtPlayer_IsRejected_AtDeclaration()
    {
        // "Target creature" can't be pointed at a player — rejected up front
        // (CR 601.2c) rather than fizzling at resolution.
        var bolt = new Sorcery("Forked Bolt", "R") { Owner = _alice };
        var spec = new TargetSpec("target creature").Creatures();

        var action = new CastSpellAction(
            bolt, _alice, sorcerySpeedAvailable: true, fromZone: ZoneType.Hand,
            targets: new object[] { _bob }, targetSpec: spec);

        var result = _validator.ValidateAction(action);
        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("601.2c");
        result.ErrorMessage.Should().Contain("not a legal target");
    }

    [Fact]
    public void Cast_TargetCreature_PointedAtCreatureNotOnBattlefield_IsRejected()
    {
        // A creature card in hand (not on the battlefield) is not a legal
        // "target creature" — CR 115.5 / 601.2c, enforced at declaration.
        var bolt = new Sorcery("Forked Bolt", "R") { Owner = _alice };
        var inHand = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Hand,
        };
        var spec = new TargetSpec("target creature").Creatures();

        var action = new CastSpellAction(
            bolt, _alice, sorcerySpeedAvailable: true, fromZone: ZoneType.Hand,
            targets: new object[] { inHand }, targetSpec: spec);

        _validator.ValidateAction(action).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Cast_AnyTarget_AcceptsCreatureOrPlayer()
    {
        // "Any target" (creature / player / planeswalker) accepts both a
        // battlefield creature and a player.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        var bear = BattlefieldBear(_bob);
        var spec = new TargetSpec("any target").AnyTarget();

        new CastSpellAction(
                bolt, _alice, sorcerySpeedAvailable: true, fromZone: ZoneType.Hand,
                targets: new object[] { bear }, targetSpec: spec)
            .Let(a => _validator.ValidateAction(a).IsValid).Should().BeTrue();

        new CastSpellAction(
                bolt, _alice, sorcerySpeedAvailable: true, fromZone: ZoneType.Hand,
                targets: new object[] { _bob }, targetSpec: spec)
            .Let(a => _validator.ValidateAction(a).IsValid).Should().BeTrue();
    }

    [Fact]
    public void Cast_HexproofCreature_TargetedByOpponent_IsRejected_AtDeclaration()
    {
        // CR 702.11 — an opponent's "target creature" can't be pointed at a
        // hexproof creature; the shared TargetLegality predicate rejects it at
        // declaration (the same definition used at resolution).
        var bolt = new Sorcery("Forked Bolt", "R") { Owner = _alice };
        var bear = BattlefieldBear(_bob);
        bear.AddAbility(new KeywordAbility("Hexproof"));
        var spec = new TargetSpec("target creature").Creatures();

        var action = new CastSpellAction(
            bolt, _alice, sorcerySpeedAvailable: true, fromZone: ZoneType.Hand,
            targets: new object[] { bear }, targetSpec: spec);

        _validator.ValidateAction(action).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Cast_NoDeclaredSpec_KeepsLegacyResolutionOnlyPosture()
    {
        // Backward compatibility: a cast with no declared spec is not
        // type-filtered at declaration (the legacy posture for the many
        // callers that don't stamp a spec yet).
        var bolt = new Sorcery("Forked Bolt", "R") { Owner = _alice };

        var action = new CastSpellAction(
            bolt, _alice, sorcerySpeedAvailable: true, fromZone: ZoneType.Hand,
            targets: new object[] { _bob }); // player target, no spec

        _validator.ValidateAction(action).IsValid.Should().BeTrue();
    }

    // ---- Activation path (Rogue's Passage) -------------------------------

    [Fact]
    public void Activate_TargetCreature_PointedAtPlayer_IsRejected_AtDeclaration()
    {
        // Rogue's Passage "{4}, {T}: Target creature can't be blocked this
        // turn." — the activated-ability target gate rejects a player target
        // at declaration (CR 602.1b / 601.2c).
        var source = new Land("Rogue's Passage") { Owner = _alice, Controller = _alice };
        var ability = new ActivatedAbility(
            source, _alice, null,
            new System.Collections.Generic.List<ICost>(),
            new System.Collections.Generic.List<IEffect>());
        var spec = new TargetSpec("target creature").Creatures();

        var action = new ActivateAbilityAction(
            ability, _alice, sorcerySpeedAvailable: true,
            targets: new object[] { _bob }, targetSpec: spec);

        var result = _validator.ValidateAction(action);
        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("601.2c");
    }

    [Fact]
    public void Activate_TargetCreature_PointedAtBattlefieldCreature_IsValid()
    {
        var source = new Land("Rogue's Passage") { Owner = _alice, Controller = _alice };
        var ability = new ActivatedAbility(
            source, _alice, null,
            new System.Collections.Generic.List<ICost>(),
            new System.Collections.Generic.List<IEffect>());
        var bear = BattlefieldBear(_alice);
        var spec = new TargetSpec("target creature").Creatures();

        var action = new ActivateAbilityAction(
            ability, _alice, sorcerySpeedAvailable: true,
            targets: new object[] { bear }, targetSpec: spec);

        _validator.ValidateAction(action).IsValid.Should().BeTrue();
    }
}

/// <summary>Tiny test-local helper to inline a build-then-evaluate.</summary>
internal static class DeclaredTargetTestExtensions
{
    public static TResult Let<T, TResult>(this T value, System.Func<T, TResult> fn) => fn(value);
}
