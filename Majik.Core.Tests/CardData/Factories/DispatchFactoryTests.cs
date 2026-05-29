using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.ValueObjects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DispatchFactory"/> (New Phyrexia, {W}).
///
/// Instant. Oracle text:
///   "Tap target creature.
///    Metalcraft — If you control three or more artifacts, exile that
///    creature."
///
/// Covers:
///   - Identity ({W} Instant, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Spell definition shape: 1..1 "target creature", no modes, no X.
///   - Resolve body taps the target creature unconditionally.
///   - Metalcraft INACTIVE (0/1/2 artifacts) → creature is tapped but
///     stays on the battlefield (not exiled).
///   - Metalcraft ACTIVE (3/4 artifacts) → creature is tapped AND exiled.
///   - Opponent's artifacts do NOT contribute (CR 109.5).
///   - "you" reads the spell's controller, not the target's controller.
/// </summary>
public class DispatchFactoryTests
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

    private Creature PutCreatureOnBattlefield(Player owner, string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Bear });
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
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
    public void Dispatch_Identity_InstantAtW()
    {
        var d = DispatchFactory.Create(_alice);

        d.Name.Should().Be("Dispatch");
        d.HasType(CardType.Instant).Should().BeTrue();
        d.ManaCost.ToString().Should().Be("{W}");
        d.Owner.Should().BeSameAs(_alice);
        d.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Dispatch_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Dispatch", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Dispatch");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Dispatch_SpellDefinition_HasSingleTargetCreatureRequest()
    {
        var def = DispatchFactory.BuildSpellDefinition(_alice, x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature");
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

        DispatchFactory.MetalcraftActive(_alice).Should().Be(expected);
    }

    [Fact]
    public void MetalcraftActive_OpponentArtifacts_DoNotContribute()
    {
        for (var i = 0; i < 5; i++)
            PutArtifactOnBattlefield(_bob, $"Bob's Mox #{i}");

        DispatchFactory.MetalcraftActive(_alice).Should().BeFalse(
            "Metalcraft is gated on artifacts YOU control (CR 109.5)");
    }

    // -------------------------------------------------------------------------
    // Resolve — Metalcraft inactive: tap only, no exile
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispatch_Resolve_TapsOnly_NoArtifacts()
    {
        var creature = PutCreatureOnBattlefield(_bob, "Grizzly Bears");

        var def = DispatchFactory.BuildSpellDefinition(_alice, x => x);
        var effects = def.EffectFactory(ChosenAt(creature));
        foreach (var fx in effects) fx.Execute();

        creature.IsTapped.Should().BeTrue("Tap target creature is unconditional");
        creature.Zone.Should().Be(ZoneType.Battlefield,
            "Metalcraft inactive (0 artifacts) → creature is not exiled");
        _bob.Zones.Battlefield.GetCards().Should().Contain(creature);
    }

    [Fact]
    public void Dispatch_Resolve_TapsOnly_AtTwoArtifacts()
    {
        PutArtifactOnBattlefield(_alice, "Mox Opal");
        PutArtifactOnBattlefield(_alice, "Ornithopter");
        var creature = PutCreatureOnBattlefield(_bob, "Grizzly Bears");

        var def = DispatchFactory.BuildSpellDefinition(_alice, x => x);
        var effects = def.EffectFactory(ChosenAt(creature));
        foreach (var fx in effects) fx.Execute();

        creature.IsTapped.Should().BeTrue();
        creature.Zone.Should().Be(ZoneType.Battlefield,
            "two artifacts < 3 → Metalcraft inactive → no exile");
    }

    // -------------------------------------------------------------------------
    // Resolve — Metalcraft active: tap AND exile
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispatch_Resolve_TapsAndExiles_AtThreeArtifacts()
    {
        for (var i = 0; i < 3; i++)
            PutArtifactOnBattlefield(_alice, $"Artifact #{i}");
        var creature = PutCreatureOnBattlefield(_bob, "Grizzly Bears");

        var def = DispatchFactory.BuildSpellDefinition(_alice, x => x);
        var effects = def.EffectFactory(ChosenAt(creature));
        foreach (var fx in effects) fx.Execute();

        creature.IsTapped.Should().BeTrue("the creature is tapped before being exiled");
        creature.Zone.Should().Be(ZoneType.Exile,
            "Metalcraft active (3 artifacts) → exile that creature (CR 701.20)");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(creature);
        _bob.Zones.Exile.GetCards().Should().Contain(creature);
    }

    [Fact]
    public void Dispatch_Resolve_TapsAndExiles_AtFourArtifacts()
    {
        for (var i = 0; i < 4; i++)
            PutArtifactOnBattlefield(_alice, $"Artifact #{i}");
        var creature = PutCreatureOnBattlefield(_bob, "Grizzly Bears");

        var def = DispatchFactory.BuildSpellDefinition(_alice, x => x);
        var effects = def.EffectFactory(ChosenAt(creature));
        foreach (var fx in effects) fx.Execute();

        creature.IsTapped.Should().BeTrue();
        creature.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Dispatch_Resolve_OpponentArtifacts_DoNotEnableExile()
    {
        // Bob's artifacts don't gate Alice's Metalcraft (CR 109.5).
        for (var i = 0; i < 5; i++)
            PutArtifactOnBattlefield(_bob, $"Bob's Mox #{i}");
        var creature = PutCreatureOnBattlefield(_bob, "Grizzly Bears");

        var def = DispatchFactory.BuildSpellDefinition(_alice, x => x);
        var effects = def.EffectFactory(ChosenAt(creature));
        foreach (var fx in effects) fx.Execute();

        creature.IsTapped.Should().BeTrue();
        creature.Zone.Should().Be(ZoneType.Battlefield,
            "Metalcraft reads Alice's artifacts only → tap, no exile");
    }

    // -------------------------------------------------------------------------
    // Illegal target at resolution — fizzles (CR 608.2b)
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispatch_Resolve_IllegalTarget_NoOp()
    {
        for (var i = 0; i < 3; i++)
            PutArtifactOnBattlefield(_alice, $"Artifact #{i}");
        var creature = PutCreatureOnBattlefield(_bob, "Grizzly Bears");

        // Creature left the battlefield in response — no longer a legal target.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);

        var def = DispatchFactory.BuildSpellDefinition(_alice, x => x);
        var effects = def.EffectFactory(ChosenAt(creature));
        foreach (var fx in effects) fx.Execute();

        creature.IsTapped.Should().BeFalse("no legal target → nothing happens");
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }
}
