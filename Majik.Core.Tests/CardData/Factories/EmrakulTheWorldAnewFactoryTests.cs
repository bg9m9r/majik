using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EmrakulTheWorldAnewFactory"/> (Modern Horizons 3,
/// {12}).
///
/// Legendary Creature — Eldrazi 12/12. Oracle text (Scryfall, verified):
///   "When you cast this spell, gain control of all creatures target player
///    controls.
///    Flying, protection from spells and from permanents that were cast this
///    turn
///    When Emrakul leaves the battlefield, sacrifice all creatures you
///    control.
///    Madness—Pay six {C}."
///
/// Covers:
///   - Identity (Legendary Creature — Eldrazi, {12}, 12/12, owner/controller).
///   - Madness catalog entry (six colorless pips — the deferral this closes).
///   - Flying marker + protection-from-spells predicate.
///   - Cast trigger shape + condition + resolution (control-change for every
///     creature the target player controls, via the live CES).
///   - LTB trigger shape + resolution (sacrifice all creatures the controller
///     controls).
/// </summary>
[Trait("Color", "C")]
public class EmrakulTheWorldAnewFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose()
    {
        ContinuousEffectsServiceProvider.Clear();
        EventBusRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Emrakul_Identity()
    {
        var emrakul = EmrakulTheWorldAnewFactory.Create(_alice);

        emrakul.Name.Should().Be("Emrakul, the World Anew");
        emrakul.ManaCost.Should().Be("{12}");
        emrakul.HasType(CardType.Creature).Should().BeTrue();
        emrakul.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        emrakul.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        emrakul.BasePower.Should().Be(12);
        emrakul.BaseToughness.Should().Be(12);
        emrakul.Owner.Should().BeSameAs(_alice);
        emrakul.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Emrakul_DispatchesThroughNamedFactory()
    {
        var card = NamedCardFactory.Create("Emrakul, the World Anew", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Emrakul, the World Anew");
    }

    // -----------------------------------------------------------------------
    // Madness (CR 702.35) — the deferral this factory closes
    // -----------------------------------------------------------------------

    [Fact]
    public void Emrakul_HasSixColorlessMadnessCost()
    {
        var emrakul = EmrakulTheWorldAnewFactory.Create(_alice);

        MadnessCatalog.HasMadness(emrakul).Should().BeTrue();
        MadnessCatalog.CostFor(emrakul).Should().Be(ManaCost.Parse("{C}{C}{C}{C}{C}{C}"));
    }

    // -----------------------------------------------------------------------
    // Keyword + protection markers
    // -----------------------------------------------------------------------

    [Fact]
    public void Emrakul_HasFlyingMarker()
    {
        var emrakul = EmrakulTheWorldAnewFactory.Create(_alice);
        emrakul.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Flying", "CR 702.9 — Flying");
    }

    [Fact]
    public void Emrakul_ProtectionFromSpells_MatchesEverySpell()
    {
        var emrakul = EmrakulTheWorldAnewFactory.Create(_alice);
        var prot = emrakul.Abilities.OfType<ProtectionAbility>().Single();
        prot.SpellPredicate.Should().NotBeNull();

        // "protection from spells" — every spell on the stack is being cast.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var boltSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        prot.SpellPredicate!(boltSpell).Should().BeTrue();
        Protection.HasProtectionFromSpell(emrakul, boltSpell).Should().BeTrue();

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob };
        var bearSpell = new Majik.Core.Spells.Spell(bear, _bob);
        prot.SpellPredicate!(bearSpell).Should().BeTrue(
            "protection from spells covers any spell type");
    }

    // -----------------------------------------------------------------------
    // Cast trigger (CR 603.6a / CR 613.2) — gain control of target's creatures
    // -----------------------------------------------------------------------

    [Fact]
    public void CastTrigger_Shape_StackActive_WithTargetPlayerRequest()
    {
        var emrakul = EmrakulTheWorldAnewFactory.Create(_alice);
        var castTrigger = CastTrigger(emrakul);

        castTrigger.ActiveZones.Should().Contain(ZoneType.Stack);
        castTrigger.TargetRequests.Should().HaveCount(1);
        castTrigger.TargetRequests[0].MinTargets.Should().Be(1);
        castTrigger.TargetRequests[0].MaxTargets.Should().Be(1);
        castTrigger.TargetRequests[0].Description.Should().Contain("player");
    }

    [Fact]
    public void CastTrigger_Condition_FiresOnEmrakulsOwnCast_NotOthers()
    {
        var emrakul = EmrakulTheWorldAnewFactory.Create(_alice);
        var castTrigger = CastTrigger(emrakul);

        var ownCast = new Majik.Core.Domain.DomainEvents.SpellCastEvent(
            new Majik.Core.Spells.Spell(emrakul, _alice));
        castTrigger.Condition.Matches(ownCast, castTrigger).Should().BeTrue();

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        var otherCast = new Majik.Core.Domain.DomainEvents.SpellCastEvent(
            new Majik.Core.Spells.Spell(other, _bob));
        castTrigger.Condition.Matches(otherCast, castTrigger).Should().BeFalse();
    }

    [Fact]
    public void CastTrigger_TargetGatherer_IncludesAllPlayers()
    {
        var emrakul = EmrakulTheWorldAnewFactory.Create(_alice);
        var req = CastTrigger(emrakul).TargetRequests[0];

        var ctx = new Majik.Core.Game.GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: Majik.Core.StateMachine.StepStateType.PreCombatMain,
            stack: new Majik.Core.Stack.Stack());

        var candidates = req.ResolveCandidates(ctx);

        // CR 109.5 — "target player": any player (no opponent restriction).
        candidates.Should().Contain(_bob);
        candidates.Should().Contain(_alice);
    }

    [Fact]
    public void CastTrigger_Resolution_GainsControlOfAllTargetsCreatures()
    {
        var ces = new ContinuousEffectsService();
        ContinuousEffectsServiceProvider.Set(_alice, ces);

        var emrakul = EmrakulTheWorldAnewFactory.Create(_alice);

        // Bob controls two creatures on the battlefield.
        var goblin = PutOnBattlefield(_bob, new Creature("Goblin", "{R}", 1, 1));
        var bear = PutOnBattlefield(_bob, new Creature("Bear", "{1}{G}", 2, 2));

        ces.EffectiveController(goblin).Should().BeSameAs(_bob, "Bob controls his creatures pre-resolution");

        var castTrigger = CastTrigger(emrakul);
        castTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in castTrigger.Effects) e.Execute();

        // CR 613.2 — every creature Bob controlled is now Alice's.
        ces.EffectiveController(goblin).Should().BeSameAs(_alice);
        ces.EffectiveController(bear).Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // LTB trigger (CR 603.6c / 603.6d) — sacrifice all creatures you control
    // -----------------------------------------------------------------------

    [Fact]
    public void LtbTrigger_Shape_BattlefieldActive_FiresOnLeave()
    {
        var emrakul = EmrakulTheWorldAnewFactory.Create(_alice);
        var ltb = LtbTrigger(emrakul);

        ltb.ActiveZones.Should().Contain(ZoneType.Battlefield);

        // Fires when Emrakul leaves the battlefield (any destination).
        var leave = new CardMovedEvent(emrakul, ZoneType.Battlefield, ZoneType.Graveyard);
        ltb.Condition.Matches(leave, ltb).Should().BeTrue();

        var bounce = new CardMovedEvent(emrakul, ZoneType.Battlefield, ZoneType.Hand);
        ltb.Condition.Matches(bounce, ltb).Should().BeTrue();

        // Does NOT fire on a non-leave move, or some other card leaving.
        var enter = new CardMovedEvent(emrakul, ZoneType.Hand, ZoneType.Battlefield);
        ltb.Condition.Matches(enter, ltb).Should().BeFalse();

        var other = new Creature("Bear", "{1}{G}", 2, 2);
        var otherLeave = new CardMovedEvent(other, ZoneType.Battlefield, ZoneType.Graveyard);
        ltb.Condition.Matches(otherLeave, ltb).Should().BeFalse();
    }

    [Fact]
    public void LtbTrigger_Resolution_SacrificesAllControllersCreatures()
    {
        var bus = new EventBus();
        EventBusRegistry.Set(_alice, bus);

        var emrakul = EmrakulTheWorldAnewFactory.Create(_alice);
        // Alice controls some creatures.
        var a = PutOnBattlefield(_alice, new Creature("Soldier", "{W}", 1, 1));
        var b = PutOnBattlefield(_alice, new Creature("Knight", "{1}{W}", 2, 2));
        // Bob's creature must NOT be sacrificed.
        var bobCreature = PutOnBattlefield(_bob, new Creature("Goblin", "{R}", 1, 1));

        var ltb = LtbTrigger(emrakul);
        foreach (var e in ltb.Effects) e.Execute();

        a.Zone.Should().Be(ZoneType.Graveyard, "Alice's creature is sacrificed");
        b.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Should().BeEmpty();
        bobCreature.Zone.Should().Be(ZoneType.Battlefield, "only the controller's creatures are sacrificed");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static TriggeredAbility CastTrigger(Creature emrakul) =>
        emrakul.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Stack));

    private static TriggeredAbility LtbTrigger(Creature emrakul) =>
        emrakul.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Battlefield));

    private static Creature PutOnBattlefield(Player p, Creature c)
    {
        c.SetOwner(p);
        c.SetController(p);
        p.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
