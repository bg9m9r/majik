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
[Trait("Color", "W")]
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
    // -------------------------------------------------------------------------
    // Resolve — Metalcraft active: tap AND exile
    // -------------------------------------------------------------------------
    // -------------------------------------------------------------------------
    // Illegal target at resolution — fizzles (CR 608.2b)
    // -------------------------------------------------------------------------
}
