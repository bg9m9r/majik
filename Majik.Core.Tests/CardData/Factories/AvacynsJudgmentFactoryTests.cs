using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="AvacynsJudgmentFactory"/>.
///
/// Card: Avacyn's Judgment — Sorcery {1}{R} (Eldritch Moon). Madness {X}{R}.
///   "Avacyn's Judgment deals 2 damage divided as you choose among any number
///    of targets. If this spell's madness cost was paid, it deals X damage
///    divided as you choose among those permanents and/or players instead."
///
/// Madness itself is intrinsic (MadnessCatalog + the discard funnel) and is
/// NOT exercised here — only the divided-damage spell body:
///   - Identity (name / {1}{R} / Sorcery).
///   - Normal cast (no X) → total 2 divided among targets.
///   - Madness cast (X supplied) → total X divided "instead".
///   - Division across &gt;2 targets, single-target dump, and creature targets.
///   - Illegal-at-resolution targets dropped (CR 608.2b).
/// </summary>
[Trait("Color", "R")]
public class AvacynsJudgmentFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void AvacynsJudgment_Identity()
    {
        var aj = AvacynsJudgmentFactory.Create(_alice);

        aj.Name.Should().Be("Avacyn's Judgment");
        aj.ManaCost.Should().Be("{1}{R}");
        aj.HasType(CardType.Sorcery).Should().BeTrue();
        aj.Owner.Should().BeSameAs(_alice);
        aj.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Normal cast (no madness) → total 2
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_NormalCast_SingleTarget_Deals2()
    {
        var def = AvacynsJudgmentFactory.BuildSpellDefinition(o => o!);
        var bobL = _bob.LifeTotal;

        // X null → normal cast → total = 2.
        var effects = def.EffectFactory(MakeChosen(x: null, _bob));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobL - 2);
    }

    [Fact]
    public void Resolve_NormalCast_TwoTargets_DefaultSplit_1Each()
    {
        var def = AvacynsJudgmentFactory.BuildSpellDefinition(o => o!);
        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(x: null, _bob, _carol));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobL - 1);
        _carol.LifeTotal.Should().Be(carolL - 1);
    }

    // -----------------------------------------------------------------------
    // Madness cast → total X "instead"
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_MadnessCast_X5_SingleTarget_Deals5()
    {
        // Madness {X}{R} paid with X=5 → total = X = 5 (not 2).
        var def = AvacynsJudgmentFactory.BuildSpellDefinition(o => o!);
        var bobL = _bob.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(x: 5, _bob));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobL - 5);
    }

    [Fact]
    public void Resolve_MadnessCast_X4_ThreeTargets_DividedTotalsX()
    {
        // X=4 across three targets, default even spread = 2/1/1 (totals X=4).
        var def = AvacynsJudgmentFactory.BuildSpellDefinition(o => o!);
        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;
        var aliceL = _alice.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(x: 4, _bob, _carol, _alice));
        foreach (var e in effects) e.Execute();

        var totalDealt =
            (bobL - _bob.LifeTotal)
            + (carolL - _carol.LifeTotal)
            + (aliceL - _alice.LifeTotal);
        totalDealt.Should().Be(4, "X damage is divided among the chosen targets");
    }

    [Fact]
    public void Resolve_MadnessCast_CallerSkewsAllToOne()
    {
        // X=3, caller dumps all 3 on the second target.
        var def = AvacynsJudgmentFactory.BuildSpellDefinition(
            resolver: o => o!,
            distribute: (legal, total) => new Dictionary<object, int>
            {
                [legal[0]] = 0,
                [legal[1]] = total,
            });

        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(x: 3, _bob, _carol));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobL, "0 allocated to Bob");
        _carol.LifeTotal.Should().Be(carolL - 3, "all 3 to Carol");
    }

    // -----------------------------------------------------------------------
    // Target types
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DamageToCreature_MarksDamage()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 3, 3);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var def = AvacynsJudgmentFactory.BuildSpellDefinition(o => o!);
        var effects = def.EffectFactory(MakeChosen(x: null, bear));
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(2, "normal cast deals 2 to a single creature target");
    }

    [Fact]
    public void Resolve_AllTargetsIllegal_NoDamage()
    {
        var def = AvacynsJudgmentFactory.BuildSpellDefinition(
            resolver: _ => "not-a-valid-target");
        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(x: null, _bob, _carol));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobL);
        _carol.LifeTotal.Should().Be(carolL);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ChosenSpellParams MakeChosen(int? x, params object[] targets) =>
        new(
            ModeIndex: null,
            X: x,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty);
}
