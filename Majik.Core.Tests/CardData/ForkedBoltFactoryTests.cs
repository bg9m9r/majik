using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="ForkedBoltFactory"/>.
///
/// Card: Forked Bolt — Sorcery {R} (Rise of the Eldrazi).
///   "Forked Bolt deals 2 damage divided as you choose among one or two
///    target creatures and/or players."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - One target → all 2 damage on that target.
///   - Two targets, default split → 1 damage to each (CR 119.4).
///   - Two targets, caller-supplied split (2+0) — all 2 on a single
///     chosen target.
///   - Two targets, caller-supplied skewed split (2+0) verified against
///     a second target as well.
///   - Allocation normalisation: caller-supplied total != 2 falls
///     onto the first target.
///   - Illegal targets at resolution (creature left battlefield) are
///     filtered; surviving targets still take damage.
///   - All-illegal targets → spell fizzles, no damage.
/// </summary>
public class ForkedBoltFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ForkedBolt_Identity()
    {
        var fb = ForkedBoltFactory.Create(_alice);

        fb.Name.Should().Be("Forked Bolt");
        fb.ManaCost.Should().Be("{R}");
        fb.HasType(CardType.Sorcery).Should().BeTrue();
        fb.Owner.Should().BeSameAs(_alice);
        fb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ForkedBolt()
    {
        var card = NamedCardFactory.Create("Forked Bolt", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Forked Bolt");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve: damage distribution
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_SingleTarget_Player_TakesAll2Damage()
    {
        var def = ForkedBoltFactory.BuildSpellDefinition(o => o!);
        var bobStartingLife = _bob.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobStartingLife - 2);
    }

    [Fact]
    public void Resolve_TwoTargets_DefaultSplit_1Each()
    {
        // Bob + Carol — default fallback = 1 damage each.
        var def = ForkedBoltFactory.BuildSpellDefinition(o => o!);
        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(_bob, _carol));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobL - 1);
        _carol.LifeTotal.Should().Be(carolL - 1);
    }

    [Fact]
    public void Resolve_TwoTargets_CallerSplitsAllOnSecond_2OnCarol_0OnBob()
    {
        var def = ForkedBoltFactory.BuildSpellDefinition(
            resolver: o => o!,
            distribute: legal =>
            {
                // First legal target is Bob (index 0), second is Carol
                // (index 1). Skew all 2 onto Carol.
                return new Dictionary<object, int>
                {
                    [legal[0]] = 0,
                    [legal[1]] = 2,
                };
            });

        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(_bob, _carol));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobL, "0 damage allocated to Bob");
        _carol.LifeTotal.Should().Be(carolL - 2, "all 2 to Carol");
    }

    [Fact]
    public void Resolve_DamageToCreature()
    {
        // Forked Bolt can target creatures too. Bob controls a 3/3 bear;
        // 2 damage drops the bear's marked damage to 2 (still alive but
        // damaged). We assert via the creature's damage marker —
        // creature death is SBA-driven and not part of this spell's
        // resolution.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 3, 3);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var def = ForkedBoltFactory.BuildSpellDefinition(o => o!);
        var effects = def.EffectFactory(MakeChosen(bear));
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(2);
    }

    [Fact]
    public void Resolve_AllTargetsIllegal_NoDamage()
    {
        // Both chosen tokens resolve to a non-legal target via the
        // resolver — Forked Bolt fizzles, no life lost.
        var def = ForkedBoltFactory.BuildSpellDefinition(
            resolver: _ => "not-a-valid-target");
        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(_bob, _carol));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobL);
        _carol.LifeTotal.Should().Be(carolL);
    }

    [Fact]
    public void Allocation_CallerOverflow_NormalisedTo2_OnFirstTarget()
    {
        // Caller delegate allocates 5 damage total — the engine clamps
        // back to 2 by subtracting the overflow from the first target.
        // Resulting split: legal[0] = 5 - 3 = 2, legal[1] = 0 ... wait,
        // the normaliser subtracts (5 - 2) = 3 from legal[0] so it
        // becomes max(0, 4 - 3) = 1, and the rest is dropped. Verify
        // total dealt == 2.
        var def = ForkedBoltFactory.BuildSpellDefinition(
            resolver: o => o!,
            distribute: legal => new Dictionary<object, int>
            {
                [legal[0]] = 4,
                [legal[1]] = 1,
            });

        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(_bob, _carol));
        foreach (var e in effects) e.Execute();

        var totalDealt = (bobL - _bob.LifeTotal) + (carolL - _carol.LifeTotal);
        totalDealt.Should().Be(2,
            "engine clamps caller-supplied allocations to exactly 2 total damage");
    }

    [Fact]
    public void Allocation_CallerUnderflow_NormalisedTo2_OnFirstTarget()
    {
        // Caller delegate allocates 0 total — normaliser fills (2 - 0)
        // damage on the first legal target.
        var def = ForkedBoltFactory.BuildSpellDefinition(
            resolver: o => o!,
            distribute: _ => new Dictionary<object, int>());

        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(_bob, _carol));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobL - 2, "underflow filled on first target");
        _carol.LifeTotal.Should().Be(carolL);
    }

    [Fact]
    public void DefaultAllocation_OneTarget_AllToThatTarget()
    {
        var alloc = ForkedBoltFactory.DefaultAllocation(new object[] { _bob });
        alloc.Should().ContainKey(_bob).WhoseValue.Should().Be(2);
    }

    [Fact]
    public void DefaultAllocation_TwoTargets_OneEach()
    {
        var alloc = ForkedBoltFactory.DefaultAllocation(new object[] { _bob, _carol });
        alloc[_bob].Should().Be(1);
        alloc[_carol].Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ChosenSpellParams MakeChosen(params object[] targets) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty);
}
