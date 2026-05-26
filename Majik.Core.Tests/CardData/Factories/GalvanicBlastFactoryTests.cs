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
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GalvanicBlastFactory"/>
/// (Scars of Mirrodin, {R}).
///
/// Instant. Oracle text:
///   "Galvanic Blast deals 2 damage to any target.
///    Metalcraft — Galvanic Blast deals 4 damage to that target instead
///    if you control three or more artifacts."
///
/// Covers:
///   - Identity ({R} Instant, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Spell definition shape: 1..1 "any target", no modes, no X.
///   - Resolve body deals 2 damage when Metalcraft is INACTIVE
///     (0, 1, 2 artifacts controlled).
///   - Resolve body deals 4 damage when Metalcraft is ACTIVE
///     (3, 4 artifacts controlled).
///   - Opponent's artifacts do NOT contribute (CR 109.5).
///   - Galvanic Blast on the stack does NOT count toward Metalcraft
///     (only battlefield artifacts).
///   - Damage routes through <see cref="Primitives.Fx.DealDamageAny"/>
///     for both player and creature targets.
/// </summary>
public class GalvanicBlastFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutArtifactOnBattlefield(Player owner, string name)
    {
        var a = new Artifact(name, "{0}");
        a.SetOwner(owner);
        a.SetController(owner);
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
    }

    private static ChosenSpellParams ChosenAt(object target) =>
        new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { target },
            },
            Mana: ManaPayment.Empty);

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void GalvanicBlast_Identity_InstantAtR()
    {
        var blast = GalvanicBlastFactory.Create(_alice);

        blast.Name.Should().Be("Galvanic Blast");
        blast.HasType(CardType.Instant).Should().BeTrue();
        blast.ManaCost.ToString().Should().Be("{R}");
        blast.Owner.Should().BeSameAs(_alice);
        blast.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GalvanicBlast_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Galvanic Blast", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Galvanic Blast");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GalvanicBlast_SpellDefinition_HasSingleAnyTargetRequest()
    {
        var def = GalvanicBlastFactory.BuildSpellDefinition(_alice, x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Metalcraft predicate
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(7, true)]
    public void MetalcraftActive_ToggleAtThreeArtifacts(int artifacts, bool expected)
    {
        for (var i = 0; i < artifacts; i++)
            PutArtifactOnBattlefield(_alice, $"Mox #{i}");

        GalvanicBlastFactory.MetalcraftActive(_alice).Should().Be(expected);
    }

    [Fact]
    public void MetalcraftActive_OpponentArtifacts_DoNotContribute()
    {
        // Bob (opponent) controls 5 artifacts; Alice controls 0.
        for (var i = 0; i < 5; i++)
            PutArtifactOnBattlefield(_bob, $"Bob's Mox #{i}");

        GalvanicBlastFactory.MetalcraftActive(_alice).Should().BeFalse(
            "Metalcraft is gated on artifacts YOU control (CR 109.5)");
    }

    // -------------------------------------------------------------------------
    // Resolve — base 2 damage when Metalcraft is inactive
    // -------------------------------------------------------------------------

    [Fact]
    public void GalvanicBlast_Resolve_DealsTwoDamage_NoArtifacts()
    {
        var def = GalvanicBlastFactory.BuildSpellDefinition(_alice, x => x);

        var effects = def.EffectFactory(ChosenAt(_bob));
        foreach (var fx in effects) fx.Execute();

        _bob.LifeTotal.Should().Be(18,
            "no artifacts → Metalcraft inactive → base 2 damage");
    }

    [Fact]
    public void GalvanicBlast_Resolve_DealsTwoDamage_AtTwoArtifacts()
    {
        // Two artifacts: still below the threshold.
        PutArtifactOnBattlefield(_alice, "Mox Opal");
        PutArtifactOnBattlefield(_alice, "Ornithopter");

        var def = GalvanicBlastFactory.BuildSpellDefinition(_alice, x => x);

        var effects = def.EffectFactory(ChosenAt(_bob));
        foreach (var fx in effects) fx.Execute();

        _bob.LifeTotal.Should().Be(18,
            "two artifacts < 3 → Metalcraft inactive → base 2 damage");
    }

    // -------------------------------------------------------------------------
    // Resolve — upgraded 4 damage when Metalcraft is active
    // -------------------------------------------------------------------------

    [Fact]
    public void GalvanicBlast_Resolve_DealsFourDamage_AtThreeArtifacts()
    {
        for (var i = 0; i < 3; i++)
            PutArtifactOnBattlefield(_alice, $"Artifact #{i}");

        var def = GalvanicBlastFactory.BuildSpellDefinition(_alice, x => x);

        var effects = def.EffectFactory(ChosenAt(_bob));
        foreach (var fx in effects) fx.Execute();

        _bob.LifeTotal.Should().Be(16,
            "Metalcraft active (3 artifacts) → 4 damage");
    }

    [Fact]
    public void GalvanicBlast_Resolve_DealsFourDamage_AtFourArtifacts()
    {
        for (var i = 0; i < 4; i++)
            PutArtifactOnBattlefield(_alice, $"Artifact #{i}");

        var def = GalvanicBlastFactory.BuildSpellDefinition(_alice, x => x);

        var effects = def.EffectFactory(ChosenAt(_bob));
        foreach (var fx in effects) fx.Execute();

        _bob.LifeTotal.Should().Be(16,
            "Metalcraft active (4 artifacts) → 4 damage");
    }

    [Fact]
    public void GalvanicBlast_Resolve_OpponentArtifacts_DoNotUpgrade()
    {
        // Bob's 5 artifacts don't gate Alice's Metalcraft (CR 109.5).
        for (var i = 0; i < 5; i++)
            PutArtifactOnBattlefield(_bob, $"Bob's Mox #{i}");

        var def = GalvanicBlastFactory.BuildSpellDefinition(_alice, x => x);

        var effects = def.EffectFactory(ChosenAt(_bob));
        foreach (var fx in effects) fx.Execute();

        _bob.LifeTotal.Should().Be(18,
            "Metalcraft reads Alice's artifacts only → base 2 damage");
    }

    // -------------------------------------------------------------------------
    // Resolve — creature target via Fx.DealDamageAny
    // -------------------------------------------------------------------------

    [Fact]
    public void GalvanicBlast_Resolve_DealsTwoDamage_ToCreatureTarget_NoMetalcraft()
    {
        // Tough creature so it survives the 2 damage marker.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 0, 5,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Bear });
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = GalvanicBlastFactory.BuildSpellDefinition(_alice, x => x);

        var effects = def.EffectFactory(ChosenAt(bear));
        foreach (var fx in effects) fx.Execute();

        bear.Damage.Should().Be(2,
            "no Metalcraft → 2 damage marked on creature");
    }

    [Fact]
    public void GalvanicBlast_Resolve_DealsFourDamage_ToCreatureTarget_WithMetalcraft()
    {
        for (var i = 0; i < 3; i++)
            PutArtifactOnBattlefield(_alice, $"Artifact #{i}");

        var bear = new Creature("Grizzly Bears", "{1}{G}", 0, 5,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Bear });
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = GalvanicBlastFactory.BuildSpellDefinition(_alice, x => x);

        var effects = def.EffectFactory(ChosenAt(bear));
        foreach (var fx in effects) fx.Execute();

        bear.Damage.Should().Be(4,
            "Metalcraft active → 4 damage marked on creature");
    }

    // -------------------------------------------------------------------------
    // Stack-side Galvanic Blast does NOT count toward Metalcraft
    // -------------------------------------------------------------------------

    [Fact]
    public void GalvanicBlast_OnStack_DoesNotCountTowardMetalcraft()
    {
        // The spell itself is not on the battlefield while resolving;
        // confirm only battlefield artifacts contribute. Put two
        // artifacts on the battlefield and a Galvanic Blast "on the
        // stack" — Metalcraft should still be inactive (2 < 3).
        PutArtifactOnBattlefield(_alice, "Artifact #1");
        PutArtifactOnBattlefield(_alice, "Artifact #2");

        var blast = GalvanicBlastFactory.Create(_alice);
        blast.SetZone(ZoneType.Stack);
        // (Galvanic Blast is an Instant — not an artifact — so even if
        // somehow on the battlefield it wouldn't count; this guard
        // documents the battlefield-only convention.)

        GalvanicBlastFactory.MetalcraftActive(_alice).Should().BeFalse(
            "stack zone does not contribute to Metalcraft count");
    }
}
