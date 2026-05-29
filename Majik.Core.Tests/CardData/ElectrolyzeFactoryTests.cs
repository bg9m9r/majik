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
/// Tests for <see cref="ElectrolyzeFactory"/>.
///
/// Card: Electrolyze — Instant {1}{U}{R} (Guildpact / Modern Masters).
///   "Electrolyze deals 2 damage divided as you choose among one or two
///    targets.
///    Draw a card."
///
/// Built on the same divided-damage clause as
/// <see cref="ForkedBoltFactory"/> (CR 119.4 — divide exactly 2 across the
/// chosen targets) plus a "Draw a card" rider mirroring
/// <see cref="IzzetCharmFactory"/>'s top-of-library draw
/// (<see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> per CR 704.5b).
///
/// Covers:
///   - Identity ({1}{U}{R} Instant) + NamedCardFactory dispatch.
///   - One target → all 2 damage on that target + caster draws a card.
///   - Two targets, default split → 1 damage to each (CR 119.4).
///   - Two targets, caller-supplied skew (2+0).
///   - Draw rider always fires (even when all damage targets fizzle).
///   - Empty library on draw flags SBA-loss intent (CR 704.5b).
/// </summary>
public class ElectrolyzeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Electrolyze_Identity()
    {
        var card = ElectrolyzeFactory.Create(_alice);

        card.Name.Should().Be("Electrolyze");
        card.ManaCost.Should().Be("{1}{U}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Electrolyze()
    {
        var card = NamedCardFactory.Create("Electrolyze", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Electrolyze");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{U}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve: damage distribution + draw
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_SingleTarget_TakesAll2Damage_AndCasterDrawsACard()
    {
        SeedLibrary(_alice, 1);
        var def = ElectrolyzeFactory.BuildSpellDefinition(_alice, o => o!);
        var bobL = _bob.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobL - 2, "all 2 damage on the single target");
        _alice.Zones.Hand.GetCards().Should().HaveCount(1, "the caster draws a card");
        _alice.Zones.Library.GetCards().Should().HaveCount(0, "the drawn card left the library");
    }

    [Fact]
    public void Resolve_TwoTargets_DefaultSplit_1Each_AndDraws()
    {
        SeedLibrary(_alice, 1);
        var def = ElectrolyzeFactory.BuildSpellDefinition(_alice, o => o!);
        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(_bob, _carol));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobL - 1);
        _carol.LifeTotal.Should().Be(carolL - 1);
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void Resolve_TwoTargets_CallerSkewsAllOnSecond_2OnCarol_0OnBob()
    {
        SeedLibrary(_alice, 1);
        var def = ElectrolyzeFactory.BuildSpellDefinition(
            _alice,
            resolver: o => o!,
            distribute: legal => new Dictionary<object, int>
            {
                [legal[0]] = 0,
                [legal[1]] = 2,
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
        SeedLibrary(_alice, 1);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 3, 3);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var def = ElectrolyzeFactory.BuildSpellDefinition(_alice, o => o!);
        var effects = def.EffectFactory(MakeChosen(bear));
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(2);
    }

    [Fact]
    public void Resolve_AllTargetsIllegal_NoDamage_ButCasterStillDraws()
    {
        // CR 608.2b — illegal targets are filtered. Even with no legal
        // damage target, the unconditional "Draw a card" rider still fires.
        SeedLibrary(_alice, 1);
        var def = ElectrolyzeFactory.BuildSpellDefinition(
            _alice,
            resolver: _ => "not-a-valid-target");
        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(_bob, _carol));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobL);
        _carol.LifeTotal.Should().Be(carolL);
        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "the draw rider is unconditional even when all targets are illegal");
    }

    [Fact]
    public void Resolve_EmptyLibrary_FlagsTriedToDrawFromEmpty()
    {
        // CR 704.5b — drawing from an empty library marks the player for the
        // SBA loss check (resolved elsewhere). The draw rider should not throw.
        var def = ElectrolyzeFactory.BuildSpellDefinition(_alice, o => o!);

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing with an empty library flags the player per CR 704.5b");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Instant($"Lightning Bolt {i}", "{R}") { Owner = p };
            p.Zones.Library.AddCard(c);
        }
    }

    private static ChosenSpellParams MakeChosen(params object[] targets) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty);
}
