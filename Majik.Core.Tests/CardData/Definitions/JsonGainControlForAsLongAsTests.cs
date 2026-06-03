using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Ability-path coverage for the declarative <c>gain_control</c> verb's
/// <b>"for as long as &lt;condition&gt;" duration</b> (CR 611.2b — the
/// persistent-steal family, Sower of Temptation: "gain control of target
/// creature <i>for as long as this creature remains on the battlefield</i>").
///
/// <para>Distinct from the until-end-of-turn (Threaten / Eldrazi Obligator)
/// duration covered by <see cref="JsonAbilityGainControlTests"/>: a
/// <c>duration: "while_source_on_battlefield"</c> steal must (a) NOT revert at
/// the cleanup step (it outlasts end of turn), and (b) revert the moment the
/// ability's SOURCE permanent leaves the battlefield — surfaced through the
/// <see cref="TemporaryControlChangeEffect"/>'s <c>until</c> predicate +
/// <see cref="ContinuousEffectsService.Prune"/> firing on the source's
/// departure event.</para>
/// </summary>
public class JsonGainControlForAsLongAsTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(_bus));

    private static T OnBattlefield<T>(T permanent, Player owner, ContinuousEffectsService ces)
        where T : Permanent
    {
        permanent.SetOwner(owner);
        permanent.SetController(owner);
        owner.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);
        permanent.ActiveEffects = ces;
        return permanent;
    }

    /// <summary>
    /// Build a Sower-of-Temptation-shaped card from a CardDef carrying one ETB
    /// triggered <c>gain_control</c> with the persistent-steal duration, routed
    /// through the effects-aware materialization overload. Returns the built
    /// card and its single ETB <see cref="TriggeredAbility"/>.
    /// </summary>
    private (Creature sower, TriggeredAbility ability) BuildSower(ContinuousEffectsService continuous)
    {
        var def = new CardDefinition
        {
            Name = "Sower of Temptation",
            Types = new() { "Creature" },
            Subtypes = new() { "Faerie", "Wizard" },
            ManaCost = "{2}{U}{U}",
            Power = 2,
            Toughness = 2,
            Abilities = new()
            {
                new TriggeredAbilityDefinition
                {
                    Trigger = new EnterBattlefieldSelfTriggerDef(),
                    Effects = new()
                    {
                        new GainControlEffectDef
                        {
                            TargetFilter = "creature",
                            Duration = "while_source_on_battlefield",
                            Untap = false,
                            GainsHaste = false,
                        },
                    },
                },
            },
        };

        var card = (Creature)CardDefinitionFactory.Build(
            def, _alice, replacements: null, continuous: continuous);
        return (card, card.Abilities.OfType<TriggeredAbility>().Single());
    }

    private async Task ResolveWith(TriggeredAbility ability, object target)
    {
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new[] { target } });
        await ability.ResolveAsync(agent: null, game: NewContext());
    }

    [Fact]
    public void TemporaryControlChangeEffect_WithUntilPredicate_DoesNotExpireAtEndOfTurn()
    {
        // Unit-level: the until-condition flips the EOT-expiry posture off.
        var ces = new ContinuousEffectsService(_bus);
        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob, ces);
        var sourceOnBattlefield = true;

        var eot = new TemporaryControlChangeEffect(bear, _alice);
        eot.ExpiresAtEndOfTurn.Should().BeTrue("a null-condition steal is the Threaten until-EOT family");

        var persistent = new TemporaryControlChangeEffect(bear, _alice, () => sourceOnBattlefield);
        persistent.ExpiresAtEndOfTurn.Should()
            .BeFalse("a 'for as long as <condition>' steal outlasts end of turn (CR 611.2b)");
        persistent.IsActive().Should().BeTrue("active while the condition holds");

        sourceOnBattlefield = false;
        persistent.IsActive().Should().BeFalse("inactive once the condition lapses");
    }

    [Fact]
    public async Task ForAsLongAs_StealsChosenCreature()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var (sower, ability) = BuildSower(continuous);
        OnBattlefield(sower, _alice, continuous);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob, continuous);

        await ResolveWith(ability, bear);

        bear.Controller.Should().BeSameAs(_alice,
            "the ETB controller gains control of the chosen creature (CR 613.2)");
    }

    [Fact]
    public async Task ForAsLongAs_DoesNotRevertAtEndOfTurnCleanup()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var (sower, ability) = BuildSower(continuous);
        OnBattlefield(sower, _alice, continuous);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob, continuous);
        await ResolveWith(ability, bear);
        bear.Controller.Should().BeSameAs(_alice);

        // CR 514.2 — the cleanup step ends until-END-OF-TURN effects. A
        // "for as long as this remains on the battlefield" steal is NOT one;
        // control must persist while Sower stays in play.
        continuous.ExpireEndOfTurn();

        bear.Controller.Should().BeSameAs(_alice,
            "a 'for as long as' steal outlasts end of turn while its source stays on the battlefield (CR 611.2b)");
    }

    [Fact]
    public async Task ForAsLongAs_RevertsWhenSourceLeavesBattlefield()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var (sower, ability) = BuildSower(continuous);
        OnBattlefield(sower, _alice, continuous);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob, continuous);
        await ResolveWith(ability, bear);
        bear.Controller.Should().BeSameAs(_alice);

        // Sower dies: move it off the battlefield and announce the departure.
        // The service prunes the now-inactive steal (its until-predicate reads
        // Sower's zone) and OnExpired restores the prior controller (CR 611.2b).
        _alice.Zones.Battlefield.RemoveCard(sower);
        _alice.Zones.Graveyard.AddCard(sower);
        sower.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(sower, ZoneType.Battlefield, ZoneType.Graveyard));

        bear.Controller.Should().BeSameAs(_bob,
            "control reverts to the prior controller when the source leaves the battlefield (CR 611.2b)");
    }

    [Fact]
    public async Task SowerFactory_BuildsEtbForAsLongAsSteal_RevertsOnLeave()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var sower = Majik.Core.CardData.Factories.SowerOfTemptationFactory
            .Create(_alice, continuous);

        sower.Name.Should().Be("Sower of Temptation");
        sower.Power.Should().Be(2);
        sower.Toughness.Should().Be(2);
        OnBattlefield(sower, _alice, continuous);

        var ability = sower.Abilities.OfType<TriggeredAbility>().Single();
        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob, continuous);
        await ResolveWith(ability, bear);
        bear.Controller.Should().BeSameAs(_alice,
            "Sower of Temptation steals the chosen creature (CR 613.2)");

        // Outlasts end of turn...
        continuous.ExpireEndOfTurn();
        bear.Controller.Should().BeSameAs(_alice,
            "the steal persists for as long as Sower remains on the battlefield (CR 611.2b)");

        // ...and reverts when Sower leaves play.
        _alice.Zones.Battlefield.RemoveCard(sower);
        _alice.Zones.Graveyard.AddCard(sower);
        sower.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(sower, ZoneType.Battlefield, ZoneType.Graveyard));
        bear.Controller.Should().BeSameAs(_bob,
            "control reverts when Sower leaves the battlefield (CR 611.2b)");
    }

    [Fact]
    public void SowerFactory_PureShape_NoServiceNoThrow()
    {
        var sower = Majik.Core.CardData.Factories.SowerOfTemptationFactory.Create(_alice);
        sower.Should().NotBeNull();
        sower.Abilities.OfType<TriggeredAbility>().Should().ContainSingle();
    }
}
