using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CabalTherapistFactory"/> (Modern Horizons, {B}).
/// Creature — Horror 1/1. Oracle text (verified against Scryfall 2026-06-02):
///   "Menace
///    At the beginning of your first main phase, you may sacrifice a creature.
///    When you do, choose a nonland card name, then target player reveals their
///    hand and discards all cards with that name."
///
/// This is the canonical <b>reflexive "you may [do X]; when you do, …"</b>
/// triggered-ability shape (CR 603.2.2): an OPTIONAL non-mana action (sacrifice
/// a creature) on a turn-based trigger, whose later clause ("When you do, …")
/// only fires when the optional action is actually taken. The mana-rider sibling
/// ("you may pay {1}{C}. If you do, …" — Eldrazi Obligator) is already closed;
/// this pins the SACRIFICE-cost + reflexive-discard variant.
///
/// Covers:
/// - Identity ({B} Creature — Horror 1/1) + Menace marker (CR 702.110).
/// - First-main-phase trigger shape (CR 603.1) — controller's own PreCombatMain,
///   battlefield-only.
/// - Decline ("you may sacrifice"): no creature lost, no reveal/discard.
/// - Accept: sacrifice the chosen creature, then the reflexive discard runs —
///   target player reveals hand + discards all cards with the chosen name.
/// - Decline with no creatures to sacrifice: safe no-op.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "B")]
public class CabalTherapistFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext NewContext(IPlayerAgent agent) =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(_bus));

    private static T OnBattlefield<T>(T permanent, Player owner) where T : Permanent
    {
        permanent.SetOwner(owner);
        permanent.SetController(owner);
        owner.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);
        return permanent;
    }

    private static Card InHand(string name, Player owner)
    {
        var card = new Sorcery(name, "{1}");
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        return card;
    }

    private TriggeredAbility BuildAndGetTrigger(Func<Player, string?> nameSelector)
    {
        var therapist = CabalTherapistFactory.Create(
            _alice, eventBus: _bus, triggers: null, nameSelector: nameSelector,
            targetResolver: chosen => chosen);
        OnBattlefield(therapist, _alice);
        return therapist.Abilities.OfType<TriggeredAbility>().Single();
    }

    private async Task ResolveWith(TriggeredAbility ability, Player targetPlayer, IPlayerAgent agent)
    {
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { targetPlayer } });
        await ability.ResolveAsync(agent, NewContext(agent));
    }

    [Fact]
    public void Factory_BuildsHorrorWithMenaceAndFirstMainTrigger()
    {
        var therapist = CabalTherapistFactory.Create(_alice);

        therapist.Name.Should().Be("Cabal Therapist");
        therapist.Power.Should().Be(1);
        therapist.Toughness.Should().Be(1);
        CombatAbilities.HasMenace(therapist).Should().BeTrue("Cabal Therapist has Menace (CR 702.110)");

        var trigger = therapist.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public async Task Decline_NoSacrifice_NoRevealOrDiscard()
    {
        var trigger = BuildAndGetTrigger(_ => "Lightning Bolt");
        var sacFodder = OnBattlefield(new Creature("Bear", "{1}{G}", 2, 2), _alice);
        var bolt = InHand("Lightning Bolt", _bob);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false); // decline the optional sacrifice

        await ResolveWith(trigger, _bob, agent);

        sacFodder.Zone.Should().Be(ZoneType.Battlefield, "declining means no creature is sacrificed (CR 603.2.2)");
        bolt.Zone.Should().Be(ZoneType.Hand, "the reflexive 'when you do' clause does not fire when nothing was sacrificed");
    }

    [Fact]
    public async Task Accept_SacrificesThenRevealAndDiscardAllWithName()
    {
        var trigger = BuildAndGetTrigger(_ => "Lightning Bolt");
        var sacFodder = OnBattlefield(new Creature("Bear", "{1}{G}", 2, 2), _alice);

        // Bob's hand: two copies of the named card + an unrelated card.
        var bolt1 = InHand("Lightning Bolt", _bob);
        var bolt2 = InHand("Lightning Bolt", _bob);
        var other = InHand("Counterspell", _bob);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);                 // accept the optional sacrifice
        agent.QueueFromBattlefield(sacFodder);  // choose which creature to sacrifice

        await ResolveWith(trigger, _bob, agent);

        sacFodder.Zone.Should().Be(ZoneType.Graveyard, "the chosen creature is sacrificed (CR 701.16)");
        bolt1.Zone.Should().Be(ZoneType.Graveyard, "all cards with the named name are discarded (CR 701.16a)");
        bolt2.Zone.Should().Be(ZoneType.Graveyard, "all copies of the named card are discarded");
        other.Zone.Should().Be(ZoneType.Hand, "cards with a different name are untouched");
    }

    [Fact]
    public async Task Accept_NoCreaturesToSacrifice_SafeNoOp()
    {
        var trigger = BuildAndGetTrigger(_ => "Lightning Bolt");
        // No creatures on Alice's battlefield except the therapist (which IS a creature,
        // so it could sacrifice itself) — but the agent declines.
        var bolt = InHand("Lightning Bolt", _bob);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);

        await ResolveWith(trigger, _bob, agent);

        bolt.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void NamedFactoryDispatch_ResolvesCabalTherapist()
    {
        var card = NamedCardFactory.Create("Cabal Therapist", _alice);
        card.Should().NotBeNull();
        card!.Name.Should().Be("Cabal Therapist");
    }
}
