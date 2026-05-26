using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Fatal Push (Aether Revolt, {B}, Instant).
///
/// Oracle text:
///   "Destroy target creature if it has mana value 2 or less.
///    Revolt — Destroy that creature if it has mana value 4 or less
///    instead if a permanent left the battlefield under your control this
///    turn."
///
/// Covers:
///   - Card shape + dispatch.
///   - Base clause: mv ≤ 2 → destroy; mv > 2 (no Revolt) → no-op.
///   - Revolt clause: mv ≤ 4 → destroy; mv > 4 even with Revolt → no-op.
///   - Revolt gating: only the spell controller's permanent leaving the
///     battlefield enables Revolt; opponent's losses do not.
///   - No TurnState wired (shape / dispatcher tests) → Revolt inactive.
///   - Off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class FatalPushFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly TurnState _turnState = new();

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FatalPush_HasInstantShape_Black_AtCostB()
    {
        var card = FatalPushFactory.Create(_alice);

        card.Name.Should().Be("Fatal Push");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.ManaCostValue.TotalValue.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsFatalPushShape()
    {
        var dispatched = NamedCardFactory.Create("Fatal Push", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Fatal Push");
        dispatched.ManaCost.Should().Be("{B}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetCreatureRequest()
    {
        var def = FatalPushFactory.BuildSpellDefinition(_alice, () => null, t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
    }

    // -----------------------------------------------------------------------
    // Base clause — mv ≤ 2 destroys; mv > 2 (no Revolt) no-ops.
    // -----------------------------------------------------------------------

    [Fact]
    public void Base_DestroysCreature_WithManaValueTwo()
    {
        // Tarmogoyf {1}{G} — mana value 2, exactly at the base threshold.
        var goyf = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        Resolve(goyf, revoltActive: false);

        goyf.Zone.Should().Be(ZoneType.Graveyard,
            "mana value 2 is at the base threshold (CR 702.104 base clause)");
    }

    [Fact]
    public void Base_DoesNotDestroyCreature_WithManaValueThree_NoRevolt()
    {
        // {2}{G} = mana value 3, exceeds base threshold of 2 without Revolt.
        var creature = NewControlledCreature(_bob, "Bigger Goyf", "{2}{G}");

        Resolve(creature, revoltActive: false);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            "mana value 3 exceeds the base 2; Revolt not active → no destroy");
    }

    // -----------------------------------------------------------------------
    // Revolt clause — mv ≤ 4 destroys; mv > 4 no-ops.
    // -----------------------------------------------------------------------

    [Fact]
    public void Revolt_DestroysCreature_WithManaValueFour()
    {
        // {2}{G}{G} = mana value 4 — exactly at the Revolt threshold.
        var creature = NewControlledCreature(_bob, "Big Threat", "{2}{G}{G}");

        Resolve(creature, revoltActive: true);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "Revolt active → mana value 4 is at the upgraded threshold");
    }

    [Fact]
    public void Revolt_DoesNotDestroyCreature_WithManaValueFive()
    {
        // {3}{G}{G} = mana value 5, exceeds the Revolt threshold of 4.
        var creature = NewControlledCreature(_bob, "Huge Threat", "{3}{G}{G}");

        Resolve(creature, revoltActive: true);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            "even with Revolt, mana value 5 exceeds the 4-threshold");
    }

    [Fact]
    public void Revolt_AlsoDestroysCreature_WithManaValueTwo()
    {
        // Sanity: Revolt is "destroy if mv ≤ 4 INSTEAD" — the base clause
        // is subsumed (any mv ≤ 4 dies, including mv ≤ 2).
        var goyf = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        Resolve(goyf, revoltActive: true);

        goyf.Zone.Should().Be(ZoneType.Graveyard,
            "Revolt's mv ≤ 4 covers the base mv ≤ 2 case");
    }

    // -----------------------------------------------------------------------
    // Revolt gating
    // -----------------------------------------------------------------------

    [Fact]
    public void IsRevoltActive_TracksOnlyControllersPermanents()
    {
        // Opponent loses a permanent — Alice's Revolt is NOT enabled.
        _turnState.RecordPermanentLeftBattlefield(_bob);

        FatalPushFactory
            .IsRevoltActive(_alice, () => _turnState)
            .Should().BeFalse(
                "Revolt requires a permanent the spell controller controlled to leave (CR 702.104a)");

        _turnState.RecordPermanentLeftBattlefield(_alice);

        FatalPushFactory
            .IsRevoltActive(_alice, () => _turnState)
            .Should().BeTrue(
                "a permanent under Alice's control leaving this turn flips the gate");
    }

    [Fact]
    public void IsRevoltActive_NoTurnStateWired_ReturnsFalse()
    {
        // Test / dispatcher path with no TurnState wired — base clause applies.
        FatalPushFactory
            .IsRevoltActive(_alice, () => null)
            .Should().BeFalse();
    }

    [Fact]
    public void TurnState_Reset_ClearsRevoltTally()
    {
        _turnState.RecordPermanentLeftBattlefield(_alice);
        _turnState.RevoltActive(_alice).Should().BeTrue();

        _turnState.Reset();
        _turnState.RevoltActive(_alice).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Off-battlefield target — illegal at resolve (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Target leaves the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature, revoltActive: true);

        // The target is already in the graveyard from the pre-resolve move;
        // Fatal Push neither moves nor touches it again (no exceptions, no
        // double-move). Zone unchanged from the pre-resolve setup.
        creature.Zone.Should().Be(ZoneType.Graveyard,
            "CR 608.2b — illegal target at resolution → effect does nothing");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(Creature target, bool revoltActive)
    {
        if (revoltActive)
        {
            _turnState.RecordPermanentLeftBattlefield(_alice);
        }

        var def = FatalPushFactory.BuildSpellDefinition(
            _alice,
            turnStateResolver: () => _turnState,
            targetResolver: t => t);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
