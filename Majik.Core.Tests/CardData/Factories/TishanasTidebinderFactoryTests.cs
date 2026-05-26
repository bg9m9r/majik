using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Tishana's Tidebinder (Lost Caverns of Ixalan, <c>{2}{U}</c>).
///
/// Oracle (Scryfall, LCI):
///   "Flash
///    When this creature enters, counter up to one target activated or
///    triggered ability. If an ability of an artifact, creature, or
///    planeswalker is countered this way, that permanent loses all
///    abilities for as long as this creature remains on the battlefield.
///    (Mana abilities can't be targeted.)"
///
/// Covers:
///   * Card shape (Merfolk Wizard 3/2 at {2}{U}).
///   * Flash keyword marker.
///   * ETB trigger structure (0..1 "target activated or triggered
///     ability", active zone = battlefield).
///   * Resolve: counter a triggered ability on the stack (CR 701.5b).
///   * Resolve: counter an activated ability on the stack (CR 701.5b).
///   * Resolve: spell target → no-op (illegal per printed predicate).
///   * Resolve: zero targets (the "up to one" branch) → clean no-op.
///   * Resolve: target no longer on stack → no-op (CR 608.2b).
///   * <see cref="NamedCardFactory"/> dispatch by name.
/// </summary>
public class TishanasTidebinderFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TishanasTidebinderFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + shape
    // -----------------------------------------------------------------------

    [Fact]
    public void TishanasTidebinder_IsMerfolkWizard_3_2_AtCost2U()
    {
        var t = TishanasTidebinderFactory.Create(_alice);

        t.Name.Should().Be("Tishana's Tidebinder");
        t.ManaCost.Should().Be("{2}{U}");
        t.HasType(CardType.Creature).Should().BeTrue();
        t.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        t.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        t.BasePower.Should().Be(3);
        t.BaseToughness.Should().Be(2);
        t.Owner.Should().BeSameAs(_alice);
        t.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TishanasTidebinder_HasFlash()
    {
        var t = TishanasTidebinderFactory.Create(_alice);

        var keywords = t.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
    }

    [Fact]
    public void TishanasTidebinder_EtbTrigger_DeclaresOptional_0_1_AbilityTarget()
    {
        var t = TishanasTidebinderFactory.Create(_alice);

        var triggers = t.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(0,
            because: "\"up to one\" — optional target (CR 700.2)");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("activated");
        req.Description.Should().Contain("triggered");

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsTidebinderShape()
    {
        var dispatched = NamedCardFactory.Create("Tishana's Tidebinder", _alice);

        dispatched.Should().BeOfType<Creature>();
        dispatched.Name.Should().Be("Tishana's Tidebinder");
        dispatched.ManaCost.Should().Be("{2}{U}");
    }

    // -----------------------------------------------------------------------
    // Counter a triggered ability
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_CountersTriggeredAbility_OnStack()
    {
        var t = TishanasTidebinderFactory.Create(_alice, _stack);
        var etb = t.Abilities.OfType<TriggeredAbility>().Single();

        // Bob has an ETB triggered ability on the stack from one of his
        // creatures.
        var bobSource = new Creature("Bob's Bear", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var ranEffect = false;
        var trigger = new TriggeredAbility(
            bobSource,
            _bob,
            Triggers.OnEnterBattlefieldSelf(bobSource),
            effects: new IEffect[] { new Effect("eff", () => ranEffect = true) });
        _stack.Push(trigger);

        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { trigger },
        });

        foreach (var eff in etb.Effects) eff.Execute();

        _stack.GetAll().Should().NotContain(trigger,
            because: "Tidebinder removes the targeted triggered ability from the stack (CR 701.5b)");
        ranEffect.Should().BeFalse(
            because: "the countered ability's effects never run");
    }

    // -----------------------------------------------------------------------
    // Counter an activated ability
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_CountersActivatedAbility_OnStack()
    {
        var t = TishanasTidebinderFactory.Create(_alice, _stack);
        var etb = t.Abilities.OfType<TriggeredAbility>().Single();

        var bobSource = new Creature("Bob's Pinger", "{1}{U}", 1, 1)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var ranEffect = false;
        var ability = new ActivatedAbility(
            bobSource,
            _bob,
            effects: new IEffect[] { new Effect("eff", () => ranEffect = true) });
        _stack.Push(ability);

        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ability },
        });

        foreach (var eff in etb.Effects) eff.Execute();

        _stack.GetAll().Should().NotContain(ability,
            because: "Tidebinder counters activated abilities too — distinct from Consign to Memory which only counters triggered abilities");
        ranEffect.Should().BeFalse(
            because: "the countered ability never resolves");
    }

    // -----------------------------------------------------------------------
    // Zero-target ("up to one" → chose none) — clean no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_NoTargetChosen_IsCleanNoOp()
    {
        var t = TishanasTidebinderFactory.Create(_alice, _stack);
        var etb = t.Abilities.OfType<TriggeredAbility>().Single();

        // Bob has a triggered ability on the stack but Tidebinder's
        // controller declines to target it ("up to one").
        var bobSource = new Creature("Bob's Bear", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var trigger = new TriggeredAbility(
            bobSource, _bob,
            Triggers.OnEnterBattlefieldSelf(bobSource),
            effects: new IEffect[] { new Effect("eff", () => { }) });
        _stack.Push(trigger);

        // Empty chosen-targets (the "up to one" zero branch).
        etb.SetChosenTargets(Array.Empty<IReadOnlyList<object>>());

        var act = () => { foreach (var eff in etb.Effects) eff.Execute(); };
        act.Should().NotThrow();

        _stack.GetAll().Should().Contain(trigger,
            because: "no target chosen → the trigger stays on the stack");
    }

    // -----------------------------------------------------------------------
    // Target off-stack at resolution — CR 608.2b no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_TargetOffStackAtResolution_IsCleanNoOp()
    {
        var t = TishanasTidebinderFactory.Create(_alice, _stack);
        var etb = t.Abilities.OfType<TriggeredAbility>().Single();

        var bobSource = new Creature("Bob's Pinger", "{1}{U}", 1, 1)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var ability = new ActivatedAbility(
            bobSource, _bob,
            effects: new IEffect[] { new Effect("eff", () => { }) });
        // Note: ability was NEVER pushed onto the stack.

        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ability },
        });

        var act = () => { foreach (var eff in etb.Effects) eff.Execute(); };
        act.Should().NotThrow(
            because: "CR 608.2b — target no longer (or never was) on the stack → clean no-op");
    }

    // -----------------------------------------------------------------------
    // Spell target — illegal per printed predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_DoesNotCounterSpells()
    {
        var t = TishanasTidebinderFactory.Create(_alice, _stack);
        var etb = t.Abilities.OfType<TriggeredAbility>().Single();

        // Bob casts a spell.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobSpell },
        });

        foreach (var eff in etb.Effects) eff.Execute();

        _stack.GetAll().Should().Contain(bobSpell,
            because: "Tidebinder counters abilities only — not spells (per printed oracle)");
        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Tidebinder did NOT counter the spell, so it's not in the graveyard");
    }
}
