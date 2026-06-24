using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FelotharDawnOfTheAbzanFactory"/> (Tarkir:
/// Dragonstorm, {W}{B}{G}). Legendary Creature — Human Warrior 3/3. Oracle text
/// (verified against Scryfall 2026-06-24):
///   "Trample
///    Whenever Felothar enters or attacks, you may sacrifice a nonland
///    permanent. When you do, put a +1/+1 counter on each creature you control."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity ({W}{B}{G}, Legendary, Human + Warrior, 3/3).
/// - Trample marker (CR 702.19).
/// - Two enters-or-attacks reflexive triggers (one ETB, one attack), each
///   battlefield-only (CR 603.1 / CR 508.1f).
/// - Accept: sacrifice the chosen nonland permanent, then +1/+1 counter goes on
///   each creature the controller controls (CR 603.2.2).
/// - Decline ("you may"): no permanent sacrificed, no counters placed.
/// - The sacrifice is nonland-only (a Land is not offered as fodder).
/// </summary>
[Trait("Color", "M")]
public class FelotharDawnOfTheAbzanFactoryTests
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

    private Creature MakeFelotharOnBattlefield()
    {
        var felothar = FelotharDawnOfTheAbzanFactory.Create(
            _alice, triggers: null, eventBus: _bus);
        OnBattlefield(felothar, _alice);
        return felothar;
    }

    private static TriggeredAbility EtbTrigger(Creature felothar) =>
        felothar.Abilities.OfType<TriggeredAbility>().First();

    private async Task ResolveWith(TriggeredAbility ability, IPlayerAgent agent) =>
        await ability.ResolveAsync(agent, NewContext());

    [Fact]
    public void Felothar_Identity()
    {
        var c = FelotharDawnOfTheAbzanFactory.Create(_alice);

        c.Name.Should().Be("Felothar, Dawn of the Abzan");
        c.ManaCost.Should().Be("{W}{B}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        CombatAbilities.HasTrample(c).Should().BeTrue("Felothar has Trample (CR 702.19)");
    }

    [Fact]
    public void Felothar_HasEntersAndAttacksReflexiveTriggers()
    {
        var c = FelotharDawnOfTheAbzanFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2,
            "Felothar has one ETB trigger and one attack trigger (CR 603.1 / CR 508.1f).");
        triggers.Should().OnlyContain(t => t.ActiveZones.Contains(ZoneType.Battlefield),
            "the reflexive sacrifice trigger functions only from the battlefield (CR 113.6).");
    }

    [Fact]
    public async Task Accept_SacrificesNonlandPermanent_ThenCounterOnEachCreature()
    {
        var felothar = MakeFelotharOnBattlefield();
        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _alice);
        var fodder = OnBattlefield(new Creature("Servo", "", 1, 1), _alice);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);              // accept the optional sacrifice
        agent.QueueFromBattlefield(fodder);  // sacrifice the Servo

        await ResolveWith(EtbTrigger(felothar), agent);

        fodder.Zone.Should().Be(ZoneType.Graveyard,
            "the chosen nonland permanent is sacrificed (CR 701.16).");

        // CR 603.2.2 — "When you do, put a +1/+1 counter on each creature you
        // control." The fodder is gone; Felothar + the Bear each get one.
        felothar.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Felothar itself is a creature you control (CR 603.2.2).");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "each other creature you control gets a +1/+1 counter.");
    }

    [Fact]
    public async Task Decline_NoSacrifice_NoCounters()
    {
        var felothar = MakeFelotharOnBattlefield();
        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _alice);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false); // decline the optional sacrifice

        await ResolveWith(EtbTrigger(felothar), agent);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "declining the 'you may sacrifice' means the reflexive counters never fire (CR 603.2.2).");
        felothar.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public async Task Sacrifice_OffersNonlandOnly_LandIsNotEligibleFodder()
    {
        var felothar = MakeFelotharOnBattlefield();
        var land = OnBattlefield(new Land("Forest"), _alice);
        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _alice);

        // Agent accepts and would pick the first offered candidate — assert the
        // land is never offered by selecting it explicitly and confirming it is
        // not among the choices (the chooser returns null if the land is absent).
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        agent.QueueFromBattlefield(candidates =>
        {
            candidates.Should().NotContain(land,
                "a Land is not a 'nonland permanent' and must not be offered (CR 305.1).");
            return candidates.OfType<Creature>().First(c => !ReferenceEquals(c, felothar));
        });

        await ResolveWith(EtbTrigger(felothar), agent);

        land.Zone.Should().Be(ZoneType.Battlefield, "the land was never a sacrifice candidate.");
        bear.Zone.Should().Be(ZoneType.Graveyard, "the bear was the sacrificed nonland permanent.");
    }
}
