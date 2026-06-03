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
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SongMadTreacheryFactory"/> and
/// <see cref="SongMadRuinsFactory"/> — the front + back faces of the Kamigawa:
/// Neon Dynasty modal double-faced card Song-Mad Treachery // Song-Mad Ruins.
///
/// Front face (Song-Mad Treachery, {3}{R}{R}):
///   Sorcery. "Gain control of target creature until end of turn. Untap that
///   creature. It gains haste until end of turn." (Threaten template,
///   CR 613.2 / CR 514.2 / CR 805).
///
/// Back face (Song-Mad Ruins):
///   Land. "This land enters tapped." "{T}: Add {R}."
/// </summary>
[Trait("Color", "R")]
public class SongMadTreacheryFactoryTests
{
    private readonly EventBus _bus = new();

    // =========================================================================
    // Front face — identity + dispatch + MDFC tracker
    // =========================================================================

    [Fact]
    public void SongMadTreachery_Identity()
    {
        var alice = new Player("Alice", 20);
        var card = SongMadTreacheryFactory.Create(alice);

        card.Name.Should().Be("Song-Mad Treachery");
        card.ManaCost.Should().Be("{3}{R}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void SongMadTreachery_IsRed()
    {
        var alice = new Player("Alice", 20);
        var card = SongMadTreacheryFactory.Create(alice);
        CardColors.GetColors(card).Should().Contain(ManaColorEnum.Red);
    }

    [Fact]
    public void SongMadTreachery_DispatchesByName()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Song-Mad Treachery", alice);
        card.Should().NotBeNull();
        card!.Name.Should().Be("Song-Mad Treachery");
    }

    [Fact]
    public void SongMadTreachery_CarriesMdfcState_WithCastableLandBack()
    {
        var alice = new Player("Alice", 20);
        var card = SongMadTreacheryFactory.Create(alice);

        card.MdfcState.Should().NotBeNull();
        card.MdfcState!.FrontFaceName.Should().Be("Song-Mad Treachery");
        card.MdfcState!.BackFaceName.Should().Be("Song-Mad Ruins");
        card.MdfcState!.IsBackFace.Should().BeFalse("front face starts on the front face");
        card.MdfcState!.CanCastEitherFace.Should().BeTrue("the land back face is castable");
    }

    [Fact]
    public void SongMadTreachery_SpellDefinition_DeclaresSingleCreatureTarget()
    {
        var alice = new Player("Alice", 20);
        var continuous = new ContinuousEffectsService(_bus);
        var def = SongMadTreacheryFactory.BuildSpellDefinition(alice, continuous);

        def.TargetRequests.Should().HaveCount(1, "gain control of target creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
    }

    // =========================================================================
    // Front face — Threaten behaviour (steal + untap + haste, revert at EOT)
    // =========================================================================

    [Fact]
    public async Task SongMadTreachery_StealsCreature_UntapsGrantsHaste_ThenRevertsAtCleanup()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new Majik.Core.Services.ZoneService(_bus);
        var flow = new SpellCastFlow(stack, zones, _bus);
        var resolver = new Majik.Core.Services.StackResolver(_bus, zones);
        var continuous = new ContinuousEffectsService(_bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Bob's tapped, summoning-sick creature.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = bob, Controller = bob, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        bob.Zones.Battlefield.AddCard(bear);
        bear.Tap();

        var card = SongMadTreacheryFactory.Create(alice);
        card.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(card);
        var def = SongMadTreacheryFactory.BuildSpellDefinition(alice, continuous);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(alice, new[] { alice, bob }, alice, 3, StepStateType.PreCombatMain, stack);
        await flow.CastAsync(alice, card, def, agent, ctx, alternativeCost: null);
        resolver.ResolveTop(stack);

        // Mid-turn: Alice controls the bear, untapped + hasty (can attack).
        bear.Controller.Should().BeSameAs(alice, "gain control until end of turn (CR 613.2)");
        bear.IsTapped.Should().BeFalse("untap that creature (CR 701.21)");
        CombatAbilities.HasHaste(bear).Should().BeTrue("it gains haste until end of turn (CR 302.6)");

        var validator = new CombatValidator(continuous);
        validator.CanAttack(bear, alice).Should().BeTrue(
            "the stolen creature can attack for its new controller this turn");

        // CR 514.2 — cleanup ends the until-end-of-turn effects.
        continuous.ExpireEndOfTurn();
        bear.Controller.Should().BeSameAs(bob, "control reverts to the owner at cleanup (CR 514.2)");
        CombatAbilities.HasHaste(bear).Should().BeFalse("the until-EOT haste grant ends at cleanup");
    }

    // =========================================================================
    // Back face — Song-Mad Ruins (tapland)
    // =========================================================================

    [Fact]
    public void SongMadRuins_Identity()
    {
        var alice = new Player("Alice", 20);
        var land = SongMadRuinsFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Song-Mad Ruins");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Song-Mad Ruins is non-Basic");
        land.Owner.Should().BeSameAs(alice);
    }

    [Fact]
    public void SongMadRuins_CarriesMdfcState_PreFlippedToBackFace()
    {
        var alice = new Player("Alice", 20);
        var land = SongMadRuinsFactory.Create(alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Song-Mad Treachery");
        land.MdfcState!.BackFaceName.Should().Be("Song-Mad Ruins");
        land.MdfcState!.IsBackFace.Should().BeTrue("back-face card is constructed pre-flipped");
    }

    [Fact]
    public void SongMadRuins_HasSingleManaAbility_AddingRed()
    {
        var alice = new Player("Alice", 20);
        var land = SongMadRuinsFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {R} ability");
        manaAbilities[0].ManaGenerated.Red.Should().BeGreaterThan(0, "produces red mana");
    }

    [Fact]
    public void SongMadRuins_EntersTapped()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = SongMadRuinsFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue("Song-Mad Ruins unconditionally enters tapped");
    }
}
