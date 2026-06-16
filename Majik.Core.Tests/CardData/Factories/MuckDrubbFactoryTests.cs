using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Muck Drubb (Shadows over Innistrad / Eldritch Moon reprint,
/// <c>{3}{B}{B}</c>).
///
/// Oracle (Scryfall):
///   "Flash
///    When this creature enters, change the target of target spell that
///    targets only a single creature to this creature.
///    Madness {2}{B} (If you discard this card, discard it into exile. When
///    you do, cast it for its madness cost or put it into your graveyard.)"
///
/// Covers:
///   * Card shape (Beast 3/3 at {3}{B}{B}).
///   * Flash keyword marker.
///   * Madness {2}{B} (intrinsic catalog entry).
///   * ETB trigger structure (1..1 "target spell that targets only a single
///     creature", active zone = battlefield).
///   * Resolve: redirect a single-creature-target spell to Muck Drubb
///     (CR 114.6 — the spell's chosen target is rewritten in place).
///   * Candidate gate: a spell that targets a NON-creature, or two targets,
///     or zero targets, is not a legal target (printed predicate).
///   * Resolve: target spell off-stack at resolution → clean no-op (608.2b).
///   * <see cref="NamedCardFactory"/> dispatch by name.
/// </summary>
[Trait("Color", "B")]
public class MuckDrubbFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MuckDrubbFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MuckDrubb_IsBeast_3_3_AtCost3BB()
    {
        var m = MuckDrubbFactory.Create(_alice);

        m.Name.Should().Be("Muck Drubb");
        m.ManaCost.Should().Be("{3}{B}{B}");
        m.HasType(CardType.Creature).Should().BeTrue();
        m.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        m.BasePower.Should().Be(3);
        m.BaseToughness.Should().Be(3);
        m.Owner.Should().BeSameAs(_alice);
        m.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MuckDrubb_HasFlash()
    {
        var m = MuckDrubbFactory.Create(_alice);

        var keywords = m.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
    }

    [Fact]
    public void MuckDrubb_HasMadness_2B()
    {
        var m = MuckDrubbFactory.Create(_alice);

        MadnessCatalog.HasMadness(m).Should().BeTrue();
        MadnessCatalog.CostFor(m).Should().Be(Majik.Core.ValueObjects.ManaCost.Parse("{2}{B}"));
    }

    [Fact]
    public void MuckDrubb_EtbTrigger_Declares_1_1_SpellTarget()
    {
        var m = MuckDrubbFactory.Create(_alice);

        var triggers = m.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("spell");
        req.Description.Should().Contain("creature");

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Redirect a single-creature-target spell
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_RedirectsSingleCreatureTargetSpell_ToMuckDrubb()
    {
        var m = MuckDrubbFactory.Create(_alice, _stack);
        var etb = m.Abilities.OfType<TriggeredAbility>().Single();

        // Bob casts Lightning Bolt at Alice's Bear.
        var aliceBear = new Creature("Alice's Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var boltSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        boltSpell.ChosenTargets.Add(aliceBear);
        _stack.Push(boltSpell);

        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { boltSpell },
        });

        foreach (var eff in etb.Effects) eff.Execute();

        boltSpell.ChosenTargets.Should().ContainSingle()
            .Which.Should().BeSameAs(m,
                because: "CR 114.6 — the spell's target is changed to Muck Drubb");
        boltSpell.ChosenTargets.Should().NotContain(aliceBear,
            because: "the original creature target was redirected away");
    }

    // -----------------------------------------------------------------------
    // Candidate gate — only spells that target EXACTLY one creature qualify
    // -----------------------------------------------------------------------

    [Fact]
    public void CandidateGate_RejectsSpellTargetingNonCreature()
    {
        var m = MuckDrubbFactory.Create(_alice, _stack);
        var etb = m.Abilities.OfType<TriggeredAbility>().Single();
        var req = etb.TargetRequests[0];

        // A spell whose single target is a player (not a creature).
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var boltSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        boltSpell.ChosenTargets.Add(_alice);
        _stack.Push(boltSpell);

        var ctx = new GameContext(_bob, new[] { _alice, _bob }, _bob, 1, StepStateType.PreCombatMain, _stack);
        var candidates = req.ResolveCandidates(ctx);

        candidates.Should().NotContain(boltSpell,
            because: "a spell targeting a non-creature is not a legal target");
    }

    [Fact]
    public void CandidateGate_RejectsSpellWithTwoCreatureTargets()
    {
        var m = MuckDrubbFactory.Create(_alice, _stack);
        var etb = m.Abilities.OfType<TriggeredAbility>().Single();
        var req = etb.TargetRequests[0];

        var c1 = new Creature("Bear A", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var c2 = new Creature("Bear B", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var spell = new Majik.Core.Spells.Spell(new Sorcery("Fight", "{1}{G}") { Owner = _bob, Controller = _bob }, _bob);
        spell.ChosenTargets.Add(c1);
        spell.ChosenTargets.Add(c2);
        _stack.Push(spell);

        var ctx = new GameContext(_bob, new[] { _alice, _bob }, _bob, 1, StepStateType.PreCombatMain, _stack);
        var candidates = req.ResolveCandidates(ctx);

        candidates.Should().NotContain(spell,
            because: "the spell must target ONLY a single creature (two targets disqualifies)");
    }

    [Fact]
    public void CandidateGate_AcceptsSpellTargetingExactlyOneCreature()
    {
        var m = MuckDrubbFactory.Create(_alice, _stack);
        var etb = m.Abilities.OfType<TriggeredAbility>().Single();
        var req = etb.TargetRequests[0];

        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var boltSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        boltSpell.ChosenTargets.Add(bear);
        _stack.Push(boltSpell);

        var ctx = new GameContext(_bob, new[] { _alice, _bob }, _bob, 1, StepStateType.PreCombatMain, _stack);
        var candidates = req.ResolveCandidates(ctx);

        candidates.Should().Contain(boltSpell);
    }

    // -----------------------------------------------------------------------
    // Off-stack at resolution — CR 608.2b clean no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_TargetSpellOffStackAtResolution_IsCleanNoOp()
    {
        var m = MuckDrubbFactory.Create(_alice, _stack);
        var etb = m.Abilities.OfType<TriggeredAbility>().Single();

        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var boltSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        boltSpell.ChosenTargets.Add(bear);
        // Note: boltSpell was NEVER pushed onto the stack.

        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { boltSpell },
        });

        var act = () => { foreach (var eff in etb.Effects) eff.Execute(); };
        act.Should().NotThrow(
            because: "CR 608.2b — target spell no longer on the stack → clean no-op");

        boltSpell.ChosenTargets.Should().ContainSingle()
            .Which.Should().BeSameAs(bear,
                because: "the off-stack spell's target was left untouched");
    }

    // -----------------------------------------------------------------------
    // Named factory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedFactory_DispatchesMuckDrubb()
    {
        var card = NamedCardFactory.Create("Muck Drubb", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Muck Drubb");
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }
}
