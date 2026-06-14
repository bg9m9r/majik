using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 701.5b / 106.4 — the pay-time uncounterable rider's FINAL link inside
/// <see cref="SpellCastFlow"/>. Boseiju, Who Shelters All ("If that mana is
/// spent on an instant or sorcery spell, that spell can't be countered.")
/// stamps <see cref="Card.PendingCastUncounterable"/> on the underlying card
/// during the CR 601.2h mana payment (the
/// <see cref="Majik.Core.Mana.ManaProvenanceSlot.OnSpent"/> reaction —
/// covered end-to-end through the resolver in
/// <c>SpendRestrictionProvenanceGateTests</c>). This suite pins the half the
/// resolver-level gate tests can't reach: that the cast flow then COPIES that
/// pay-time stamp onto the constructed <see cref="Majik.Core.Spells.Spell"/>'s
/// <see cref="Majik.Core.Spells.ISpell.CannotBeCountered"/> and CLEARS the
/// card stamp afterward (so a later non-cast battlefield entry — blink, copy —
/// never reuses it). Mirrors the full-cast harness of
/// <see cref="SpellCastFlowUncounterableControllerStaticTests"/>.
/// </summary>
public class SpellCastFlowPayTimeUncounterableTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellCastFlowPayTimeUncounterableTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private async Task<Majik.Core.Spells.Spell> Cast(Card card)
    {
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        return await _flow.CastAsync(_alice, card,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()), agent, NewContext());
    }

    [Fact]
    public async Task PendingCastUncounterable_Instant_IsStampedOntoSpell()
    {
        // The provenance reaction (Boseiju's {C} spent on this instant) ran at
        // pay time and set the pay-time stamp; the cast flow must copy it.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Zone = ZoneType.Hand };
        bolt.MarkPendingCastUncounterable();

        var spell = await Cast(bolt);

        spell.CannotBeCountered.Should().BeTrue(
            "CR 701.5b — the pay-time stamp set during mana payment makes this spell uncounterable");
    }

    [Fact]
    public async Task PendingCastUncounterable_IsClearedAfterCast()
    {
        // Clearing the card stamp means a later NON-cast battlefield entry
        // (blink, copy) can't reuse the rider — strictly per-spell.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Zone = ZoneType.Hand };
        bolt.MarkPendingCastUncounterable();

        await Cast(bolt);

        bolt.PendingCastUncounterable.Should().BeFalse(
            "StampSpellAndCardSentinels clears the pay-time stamp once it's copied onto the spell");
    }

    [Fact]
    public async Task NoPendingStamp_SpellIsCounterableByDefault()
    {
        // A spell cast without the pay-time stamp is normally counterable —
        // confirms the stamp is what flips the flag, not the cast itself.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Zone = ZoneType.Hand };

        var spell = await Cast(bolt);

        spell.CannotBeCountered.Should().BeFalse(
            "absent the pay-time stamp (CR 106.4 default) a spell can be countered");
    }
}
