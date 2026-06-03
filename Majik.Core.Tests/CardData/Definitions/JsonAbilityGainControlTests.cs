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
/// Ability-path coverage for the declarative <c>gain_control</c> verb (CR 613.2 /
/// CR 514.2 — the Threaten / Zealous Conscripts "gain control until end of turn"
/// family) wired on an ETB-TRIGGERED ability rather than on a spell.
///
/// <para>PR #2203 built the engine <see cref="TemporaryControlChangeEffect"/> +
/// the <c>gain_control</c> verb but threaded the per-game
/// <see cref="ContinuousEffectsService"/> only on the SPELL path
/// (<see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>). These tests
/// pin the ability-path threading: a JSON <c>etb_self</c> trigger carrying a
/// <see cref="GainControlEffectDef"/> must register the temporary control swap +
/// untap + haste rider against the live continuous-effects service when the card
/// is built through the effects-aware materialization overload.</para>
///
/// <para>The card under test is Zealous Conscripts — "When this creature enters,
/// gain control of target permanent until end of turn. Untap that permanent. It
/// gains haste until end of turn." (target PERMANENT, not just creature).</para>
/// </summary>
public class JsonAbilityGainControlTests
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

    /// <summary>
    /// Build a card from a CardDef carrying one ETB triggered gain_control
    /// effect, routed through the effects-aware materialization overload so the
    /// gain_control verb can reach the live <paramref name="continuous"/>
    /// service. Returns the card's single ETB <see cref="TriggeredAbility"/>.
    /// </summary>
    private TriggeredAbility BuildConscripts(
        ContinuousEffectsService continuous, string targetFilter = "permanent")
    {
        var def = new Majik.Core.CardData.Definitions.CardDefinition
        {
            Name = "Zealous Conscripts",
            Types = new() { "Creature" },
            Subtypes = new() { "Human", "Warrior" },
            ManaCost = "{4}{R}",
            Power = 3,
            Toughness = 3,
            Abilities = new()
            {
                new TriggeredAbilityDefinition
                {
                    Trigger = new EnterBattlefieldSelfTriggerDef(),
                    Effects = new()
                    {
                        new GainControlEffectDef
                        {
                            TargetFilter = targetFilter,
                            Duration = "end_of_turn",
                            Untap = true,
                            GainsHaste = true,
                        },
                    },
                },
            },
        };

        var card = CardDefinitionFactory.Build(
            def, _alice, replacements: null, continuous: continuous);
        return card.Abilities.OfType<TriggeredAbility>().Single();
    }

    /// <summary>Resolve <paramref name="ability"/> against the chosen
    /// <paramref name="target"/> (single slot).</summary>
    private async Task ResolveWith(TriggeredAbility ability, object target)
    {
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new[] { target } });
        await ability.ResolveAsync(agent: null, game: NewContext());
    }

    [Fact]
    public void EtbGainControl_DeclaresSinglePermanentTargetRequest()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var ability = BuildConscripts(continuous);

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task EtbGainControl_StealsChosenPermanent_UntapsAndGrantsHaste()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var ability = BuildConscripts(continuous);

        // Bob's tapped, summoning-sick creature.
        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;
        bear.Tap();

        await ResolveWith(ability, bear);

        bear.Controller.Should().BeSameAs(_alice,
            "the ETB controller gains control of the chosen permanent (CR 613.2)");
        bear.IsTapped.Should().BeFalse("Untap that permanent (CR 701.21)");
        Majik.Core.Combat.CombatAbilities.HasHaste(bear).Should()
            .BeTrue("it gains haste until end of turn (CR 302.6) so it can attack this turn");
    }

    [Fact]
    public async Task EtbGainControl_RevertsToOwner_AtEndOfTurnCleanup()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var ability = BuildConscripts(continuous);

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

    [Fact]
    public async Task EtbGainControl_NonCreaturePermanent_IsStolen()
    {
        // Zealous Conscripts targets any PERMANENT — an artifact is a legal
        // target. Control swaps; the haste/untap rider only matters for
        // creatures but must not throw on a non-creature.
        var continuous = new ContinuousEffectsService(_bus);
        var ability = BuildConscripts(continuous);

        var rock = OnBattlefield(new Artifact("Mind Stone", "{2}"), _bob);
        rock.Tap();

        await ResolveWith(ability, rock);

        rock.Controller.Should().BeSameAs(_alice,
            "gain control of target PERMANENT covers non-creatures (CR 613.2)");
        rock.IsTapped.Should().BeFalse("Untap that permanent (CR 701.21)");
    }

    [Fact]
    public async Task ZealousConscripts_Factory_BuildsEtbGainControl()
    {
        // Drive the real [CardName] factory through its effects-aware overload
        // (the prod instance-swap path) and assert the ETB steal works.
        var continuous = new ContinuousEffectsService(_bus);
        var conscripts = Majik.Core.CardData.Factories.ZealousConscriptsFactory
            .Create(_alice, continuous);

        conscripts.Name.Should().Be("Zealous Conscripts");
        conscripts.Power.Should().Be(3);
        conscripts.Toughness.Should().Be(3);

        var ability = conscripts.Abilities.OfType<TriggeredAbility>().Single();
        ability.TargetRequests.Should().HaveCount(1);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;
        bear.Tap();

        await ResolveWith(ability, bear);

        bear.Controller.Should().BeSameAs(_alice,
            "Zealous Conscripts steals the chosen permanent until end of turn (CR 613.2)");
        bear.IsTapped.Should().BeFalse("Untap that permanent (CR 701.21)");
        Majik.Core.Combat.CombatAbilities.HasHaste(bear).Should()
            .BeTrue("the stolen creature gains haste until end of turn (CR 302.6)");

        continuous.ExpireEndOfTurn();
        bear.Controller.Should().BeSameAs(_bob, "control reverts at cleanup (CR 514.2)");
    }

    [Fact]
    public void ZealousConscripts_Factory_PureShape_NoServiceNoThrow()
    {
        // The single-arg overload (no service) must still build a valid card.
        var conscripts = Majik.Core.CardData.Factories.ZealousConscriptsFactory.Create(_alice);
        conscripts.Should().NotBeNull();
        conscripts.Abilities.OfType<TriggeredAbility>().Should().ContainSingle();
    }

    [Fact]
    public async Task EtbGainControl_IllegalTarget_FizzlesCleanly()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var ability = BuildConscripts(continuous);

        // Already in the graveyard — illegal at resolution (CR 608.2b).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.ActiveEffects = continuous;
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        await ResolveWith(ability, bear);

        bear.Controller.Should().BeSameAs(_bob,
            "illegal target at resolution → no control change (CR 608.2b)");
    }
}
