using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BriaRiptideRogueFactory"/> (Bloomburrow,
/// Legendary Creature — Otter Rogue {1}{U}{R} 2/2).
///
/// Oracle text:
///   "Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    Other creatures you control have prowess.
///    Whenever you cast a noncreature spell, target creature you control
///    can't be blocked this turn."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (mana cost, Legendary supertype, Otter Rogue subtypes, 2/2).
/// - Prowess on Bria herself (CR 702.108) pumps +1/+1 on a noncreature cast.
/// - "Other creatures you control have prowess" — a controlled OTHER creature
///   gains a prowess triggered ability registered with the live manager
///   (CR 613.1f / 702.108b); an opponent's creature does NOT.
/// - "Whenever you cast a noncreature spell, target creature you control can't
///   be blocked this turn" — a controlled creature becomes unblockable
///   (CR 509.1b), expiring at end of turn (CR 514.2).
/// </summary>
[Trait("Color", "M")]
public class BriaRiptideRogueFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;

    public BriaRiptideRogueFactoryTests()
    {
        _effects = new ContinuousEffectsService(_bus);
        _zones = new ZoneService(_bus);
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
    }

    private Creature MakeBear(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.ActiveEffects = _effects;
        return c;
    }

    private void PutOnBattlefield(ICard card, Player owner)
    {
        owner.Zones.Library.AddCard(card);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);
    }

    private void CastNoncreatureSpell(Player caster)
    {
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = caster };
        var spell = new Majik.Core.Spells.Spell(bolt, caster);
        _bus.Publish(new SpellCastEvent(spell));
        _triggers.PutPendingTriggersOnStack(caster);
        while (_stack.Count > 0)
        {
            _stack.Pop()!.Resolve();
        }
    }

    private static TriggeredAbility ProwessTriggerOf(ICard c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<SpellCastEvent>
                && t.Effects.Any(e => e.Description.Contains("prowess")));

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void BriaRiptideRogue_Identity()
    {
        var c = BriaRiptideRogueFactory.Create(_alice);

        c.Name.Should().Be("Bria, Riptide Rogue");
        c.ManaCost.Should().Be("{1}{U}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Otter).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // ── Prowess on Bria herself ──────────────────────────────────────────

    [Fact]
    public void BriaRiptideRogue_OwnProwess_PumpsOnNoncreatureCast()
    {
        // CR 702.108 — Bria has prowess: +1/+1 on a noncreature cast.
        var bria = BriaRiptideRogueFactory.Create(_alice, _effects, _triggers);
        PutOnBattlefield(bria, _alice);

        CastNoncreatureSpell(_alice);

        bria.GetPower().Should().Be(3, "Bria's own prowess pumps +1/+1.");
        bria.GetToughness().Should().Be(3);

        // CR 514.2 — the pump expires at end of turn.
        _effects.ExpireEndOfTurn();
        bria.GetPower().Should().Be(2);
    }

    // ── "Other creatures you control have prowess" ───────────────────────

    [Fact]
    public void BriaRiptideRogue_GrantsProwessToOtherControlledCreature_AndItFires()
    {
        // CR 613.1f / 702.108b — another creature Alice controls gains prowess,
        // registered with the live manager so it actually fires.
        var bria = BriaRiptideRogueFactory.Create(_alice, _effects, _triggers);
        PutOnBattlefield(bria, _alice);

        var bear = MakeBear(_alice);
        PutOnBattlefield(bear, _alice);

        // Reconcile the Layer-6 group grant.
        _effects.Compute(bear);

        var granted = bear.Abilities.OfType<ITriggeredAbility>()
            .Where(t => t.Condition is EventTriggerCondition<SpellCastEvent>)
            .ToList();
        granted.Should().HaveCount(1,
            "Bria grants prowess to another creature Alice controls.");
        _triggers.IsRegistered(granted[0]).Should().BeTrue(
            "the granted prowess trigger must be registered so it fires.");

        // A noncreature cast pumps the granted creature +1/+1.
        CastNoncreatureSpell(_alice);
        bear.GetPower().Should().Be(3, "granted prowess pumps the bear +1/+1.");
        bear.GetToughness().Should().Be(3);
    }

    [Fact]
    public void BriaRiptideRogue_DoesNotGrantProwessToOpponentCreature()
    {
        var bria = BriaRiptideRogueFactory.Create(_alice, _effects, _triggers);
        PutOnBattlefield(bria, _alice);

        var bobBear = MakeBear(_bob);
        PutOnBattlefield(bobBear, _bob);

        _effects.Compute(bobBear);

        bobBear.Abilities.OfType<ITriggeredAbility>().Should().BeEmpty(
            "prowess is granted only to creatures YOU control.");
    }

    // ── Cast-trigger: target creature you control can't be blocked ───────

    [Fact]
    public void BriaRiptideRogue_NoncreatureCast_MakesControlledCreatureUnblockable()
    {
        // CR 603.1 / 509.1b — on a noncreature cast, a creature Alice controls
        // can't be blocked this turn.
        var bria = BriaRiptideRogueFactory.Create(_alice, _effects, _triggers);
        PutOnBattlefield(bria, _alice);

        var blocker = MakeBear(_bob, "Bob Blocker");
        PutOnBattlefield(blocker, _bob);

        // Sanity: before the cast, Bria can be blocked.
        BlockLegality.CanBlock(blocker, bria, out _).Should().BeTrue(
            "no restriction yet — the block is legal.");

        CastNoncreatureSpell(_alice);

        // CR 509.1b — the unblockable grant landed on a creature Alice controls
        // (v1 deterministic: the first, i.e. Bria). No blocker can be declared.
        BlockLegality.CanBlock(blocker, bria, out var reason).Should().BeFalse(
            "Bria's cast trigger made a controlled creature unblockable.");
        reason.Should().Contain("can't be blocked");

        // CR 514.2 — "this turn" expires at end of turn.
        _effects.ExpireEndOfTurn();
        BlockLegality.CanBlock(blocker, bria, out _).Should().BeTrue(
            "the unblockable grant is only for this turn.");
    }
}
