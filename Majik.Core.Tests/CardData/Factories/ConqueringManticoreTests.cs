using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Conquering Manticore (Born of the Gods, Creature — Manticore {4}{R}{R}, 5/5,
/// Flying) — the ETB-Threaten family member that the now-generic ability-path
/// <c>gain_control</c> verb unblocks.
///
/// Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, gain control of target creature an opponent
///    controls until end of turn. Untap that creature. It gains haste until end
///    of turn."
///
/// <para>Mirrors Zealous Conscripts' ETB steal (PR #2203 / #2395 — the
/// <see cref="GainControlEffectDef"/> on an <c>etb_self</c> trigger registers a
/// CR 613.2 / CR 514.2 <see cref="TemporaryControlChangeEffect"/> + untap + haste
/// rider against the live per-game <see cref="ContinuousEffectsService"/>) but
/// scopes the target to "a creature an opponent controls" (CR 109.5 — the
/// declarative <c>creature_you_dont_control</c> filter) rather than ANY
/// permanent.</para>
/// </summary>
public class ConqueringManticoreTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(_bus));

    private static T OnBattlefield<T>(T permanent, Player owner) where T : Permanent
    {
        permanent.SetOwner(owner);
        permanent.SetController(owner);
        owner.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);
        return permanent;
    }

    private async Task ResolveWith(TriggeredAbility ability, object target)
    {
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new[] { target } });
        await ability.ResolveAsync(agent: null, game: NewContext());
    }

    [Fact]
    public void Factory_BuildsFiveFiveFlyingManticore()
    {
        var manticore = Majik.Core.CardData.Factories.ConqueringManticoreFactory.Create(_alice);

        manticore.Name.Should().Be("Conquering Manticore");
        manticore.Power.Should().Be(5);
        manticore.Toughness.Should().Be(5);
        manticore.HasSubtype(Majik.Core.Cards.Types.CardSubtype.Manticore).Should().BeTrue();
        Majik.Core.Combat.CombatAbilities.HasFlying(manticore).Should()
            .BeTrue("Conquering Manticore has Flying (CR 702.9)");
    }

    [Fact]
    public void Factory_PureShape_NoServiceNoThrow()
    {
        var manticore = Majik.Core.CardData.Factories.ConqueringManticoreFactory.Create(_alice);
        manticore.Should().NotBeNull();
        manticore.Abilities.OfType<TriggeredAbility>().Should().ContainSingle();
    }

    [Fact]
    public async Task EtbSteal_TakesOpponentCreature_UntapsAndGrantsHaste()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var manticore = Majik.Core.CardData.Factories.ConqueringManticoreFactory
            .Create(_alice, continuous);

        var ability = manticore.Abilities.OfType<TriggeredAbility>().Single();
        ability.TargetRequests.Should().HaveCount(1);

        // Bob's tapped, summoning-sick creature (an opponent's).
        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;
        bear.Tap();

        await ResolveWith(ability, bear);

        bear.Controller.Should().BeSameAs(_alice,
            "the ETB controller gains control of the opponent's creature (CR 613.2)");
        bear.IsTapped.Should().BeFalse("Untap that creature (CR 701.21)");
        Majik.Core.Combat.CombatAbilities.HasHaste(bear).Should()
            .BeTrue("it gains haste until end of turn (CR 302.6) so it can attack this turn");
    }

    [Fact]
    public async Task EtbSteal_RevertsToOwner_AtEndOfTurnCleanup()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var manticore = Majik.Core.CardData.Factories.ConqueringManticoreFactory
            .Create(_alice, continuous);
        var ability = manticore.Abilities.OfType<TriggeredAbility>().Single();

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;

        await ResolveWith(ability, bear);
        bear.Controller.Should().BeSameAs(_alice);

        // CR 514.2 — cleanup ends the until-end-of-turn control change.
        continuous.ExpireEndOfTurn();

        bear.Controller.Should().BeSameAs(_bob, "control reverts to the owner at cleanup (CR 514.2)");
        Majik.Core.Combat.CombatAbilities.HasHaste(bear).Should()
            .BeFalse("the until-EOT haste grant ends at cleanup too");
    }
}
