using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MalevolentWhispersFactory"/> — Malevolent Whispers
/// (Eldritch Moon, {3}{R}).
///
///   Sorcery. "Gain control of target creature until end of turn. Untap that
///   creature. It gets +2/+0 and gains haste until end of turn.
///   Madness {3}{R}."
///
/// The Threaten template (CR 613.2 / CR 514.2) bundled with a +2/+0 until-EOT
/// pump rider (CR 613.1g) carried declaratively on
/// <see cref="Majik.Core.CardData.Definitions.GainControlEffectDef"/>.
/// </summary>
[Trait("Color", "R")]
public class MalevolentWhispersFactoryTests
{
    private readonly EventBus _bus = new();

    [Fact]
    public void MalevolentWhispers_Identity()
    {
        var alice = new Player("Alice", 20);
        var card = MalevolentWhispersFactory.Create(alice);

        card.Name.Should().Be("Malevolent Whispers");
        card.ManaCost.Should().Be("{3}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void MalevolentWhispers_IsRed()
    {
        var alice = new Player("Alice", 20);
        var card = MalevolentWhispersFactory.Create(alice);
        CardColors.GetColors(card).Should().Contain(ManaColorEnum.Red);
    }

    [Fact]
    public void MalevolentWhispers_DispatchesByName()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Malevolent Whispers", alice);
        card.Should().NotBeNull();
        card!.Name.Should().Be("Malevolent Whispers");
    }

    [Fact]
    public void MalevolentWhispers_HasMadness_3R()
    {
        // CR 702.35 — madness is served intrinsically via the catalog.
        var card = MalevolentWhispersFactory.Create(new Player("Alice", 20));
        MadnessCatalog.HasMadness(card).Should().BeTrue();
        MadnessCatalog.CostFor(card).Should().Be(ManaCost.Parse("{3}{R}"));
    }

    [Fact]
    public void MalevolentWhispers_SpellDefinition_DeclaresSingleCreatureTarget()
    {
        var alice = new Player("Alice", 20);
        var continuous = new ContinuousEffectsService(_bus);
        var def = MalevolentWhispersFactory.BuildSpellDefinition(alice, continuous);

        def.TargetRequests.Should().HaveCount(1, "gain control of target creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public async Task MalevolentWhispers_StealsCreature_UntapsGrantsHastePumpsPlus2_ThenRevertsAtCleanup()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new Majik.Core.Services.ZoneService(_bus);
        var flow = new SpellCastFlow(stack, zones, _bus);
        var resolver = new Majik.Core.Services.StackResolver(_bus, zones);
        var continuous = new ContinuousEffectsService(_bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Bob's tapped, summoning-sick 2/2.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = bob, Controller = bob, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        bob.Zones.Battlefield.AddCard(bear);
        bear.Tap();

        var card = MalevolentWhispersFactory.Create(alice);
        card.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(card);
        var def = MalevolentWhispersFactory.BuildSpellDefinition(alice, continuous);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(alice, new[] { alice, bob }, alice, 3, StepStateType.PreCombatMain, stack);
        await flow.CastAsync(alice, card, def, agent, ctx, alternativeCost: null);
        resolver.ResolveTop(stack);

        // Mid-turn: Alice controls the bear, untapped + hasty + pumped to 4/2.
        bear.Controller.Should().BeSameAs(alice, "gain control until end of turn (CR 613.2)");
        bear.IsTapped.Should().BeFalse("untap that creature (CR 701.21)");
        CombatAbilities.HasHaste(bear).Should().BeTrue("it gains haste until end of turn (CR 302.6)");
        bear.Power.Should().Be(4, "+2/+0 until end of turn (CR 613.1g)");
        bear.Toughness.Should().Be(2, "+2/+0 leaves toughness unchanged");

        var validator = new CombatValidator(continuous);
        validator.CanAttack(bear, alice).Should().BeTrue(
            "the stolen creature can attack for its new controller this turn");

        // CR 514.2 — cleanup ends ALL the until-end-of-turn riders together.
        continuous.ExpireEndOfTurn();
        bear.Controller.Should().BeSameAs(bob, "control reverts to the owner at cleanup (CR 514.2)");
        CombatAbilities.HasHaste(bear).Should().BeFalse("the until-EOT haste grant ends at cleanup");
        bear.Power.Should().Be(2, "the +2/+0 pump ends at cleanup (CR 514.2)");
    }
}
