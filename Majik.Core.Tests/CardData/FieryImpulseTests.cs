using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Fiery Impulse (Magic Origins, {R}, Instant).
///
/// Oracle text:
///   "Fiery Impulse deals 2 damage to target creature.
///    Spell mastery — If there are two or more instant and/or sorcery cards
///    in your graveyard, Fiery Impulse deals 3 damage instead."
///
/// Covers:
///   - Card identity (Instant, {R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve damage to the targeted creature:
///     * empty graveyard -> 2 damage (base).
///     * one instant in graveyard -> still 2 damage (threshold is 2).
///     * two instant/sorcery cards -> 3 damage (spell mastery on).
///     * three instant/sorcery cards -> 3 damage (still on).
///     * spell mastery counts ONLY the controller's graveyard, not the
///       opponent's.
///     * non-instant/sorcery cards (creatures, lands) do NOT count.
///   - Damage hits only a creature target (resolution-time legality).
/// </summary>
public class FieryImpulseTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FieryImpulseTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FieryImpulse_IsInstant_AtCostR()
    {
        var fi = FieryImpulseFactory.Create(_alice);

        fi.Name.Should().Be("Fiery Impulse");
        fi.ManaCost.Should().Be("{R}");
        fi.HasType(CardType.Instant).Should().BeTrue();
        fi.Owner.Should().BeSameAs(_alice);
        fi.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FieryImpulse()
    {
        var card = NamedCardFactory.Create("Fiery Impulse", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Fiery Impulse");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — base 2 / spell-mastery 3
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FieryImpulse_EmptyGraveyard_Deals2DamageToCreature()
    {
        // No instant/sorcery cards in graveyard -> base 2 damage.
        var target = MakeCreature(_bob, "Tarmogoyf", 4, 5);

        await CastAndResolveTargeting(target);

        target.Damage.Should().Be(2);
    }

    [Fact]
    public async Task FieryImpulse_OneInstantInGraveyard_StillDeals2Damage()
    {
        // Threshold is TWO; a single instant is not enough.
        AddToGraveyard(_alice, new Instant("Lightning Bolt", "{R}"));

        var target = MakeCreature(_bob, "Tarmogoyf", 4, 5);
        await CastAndResolveTargeting(target);

        target.Damage.Should().Be(2,
            "spell mastery needs 2+ instant/sorcery cards; one is below threshold");
    }

    [Fact]
    public async Task FieryImpulse_TwoInstantOrSorceryInGraveyard_Deals3Damage()
    {
        // One instant + one sorcery = 2 -> spell mastery active -> 3 damage.
        AddToGraveyard(_alice, new Instant("Lightning Bolt", "{R}"));
        AddToGraveyard(_alice, new Sorcery("Lava Spike", "{R}"));

        var target = MakeCreature(_bob, "Tarmogoyf", 4, 5);
        await CastAndResolveTargeting(target);

        target.Damage.Should().Be(3,
            "two instant/sorcery cards in the controller's graveyard turns on spell mastery");
    }

    [Fact]
    public async Task FieryImpulse_ThreeInstantOrSorceryInGraveyard_Deals3Damage()
    {
        AddToGraveyard(_alice, new Instant("Lightning Bolt", "{R}"));
        AddToGraveyard(_alice, new Sorcery("Lava Spike", "{R}"));
        AddToGraveyard(_alice, new Instant("Opt", "{U}"));

        var target = MakeCreature(_bob, "Tarmogoyf", 4, 5);
        await CastAndResolveTargeting(target);

        target.Damage.Should().Be(3);
    }

    [Fact]
    public async Task FieryImpulse_OpponentGraveyardInstants_DoNotEnableSpellMastery()
    {
        // Bob (opponent) has 5 instants in his graveyard; Alice's is empty.
        for (var i = 0; i < 5; i++)
            AddToGraveyard(_bob, new Instant($"Bolt{i}", "{R}"));

        var target = MakeCreature(_bob, "Tarmogoyf", 4, 5);
        await CastAndResolveTargeting(target);

        target.Damage.Should().Be(2,
            "spell mastery counts cards in YOUR graveyard only");
    }

    [Fact]
    public async Task FieryImpulse_NonInstantOrSorceryCards_DoNotCount()
    {
        // Two creatures + a land in graveyard do not satisfy spell mastery.
        AddToGraveyard(_alice, new Creature("Grizzly Bears", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Creature("Hill Giant", "{3}{R}", 3, 3));

        var target = MakeCreature(_bob, "Tarmogoyf", 4, 5);
        await CastAndResolveTargeting(target);

        target.Damage.Should().Be(2,
            "only instant and/or sorcery cards count toward spell mastery");
    }

    // -----------------------------------------------------------------------
    // Spell-mastery helper (programmatic)
    // -----------------------------------------------------------------------

    [Fact]
    public void IsSpellMasteryActive_CountsInstantsAndSorceriesInControllerGraveyard()
    {
        FieryImpulseFactory.IsSpellMasteryActive(_alice).Should().BeFalse("empty graveyard");

        AddToGraveyard(_alice, new Instant("Lightning Bolt", "{R}"));
        FieryImpulseFactory.IsSpellMasteryActive(_alice).Should().BeFalse("one is below threshold");

        AddToGraveyard(_alice, new Sorcery("Lava Spike", "{R}"));
        FieryImpulseFactory.IsSpellMasteryActive(_alice).Should().BeTrue("two reaches threshold");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature MakeCreature(Player owner, string name, int p, int t)
    {
        var c = new Creature(name, "{1}{G}", p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static void AddToGraveyard(Player owner, Card card)
    {
        card.SetOwner(owner);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Cast Fiery Impulse from Alice's hand at <paramref name="target"/> and
    /// resolve the resulting stack object. Mirrors the UnholyHeatTests /
    /// GalvanicDischargeTests cast harness — direct cast/resolve, no priority
    /// loop.
    /// </summary>
    private async Task CastAndResolveTargeting(object target)
    {
        var fi = FieryImpulseFactory.Create(_alice);
        fi.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fi);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, fi,
            FieryImpulseFactory.BuildSpellDefinition(_alice, t => t),
            agent, ctx);

        fi.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }
}
