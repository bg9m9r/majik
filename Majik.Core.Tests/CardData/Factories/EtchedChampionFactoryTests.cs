using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EtchedChampionFactory"/>
/// (Mirrodin Besieged, {3}).
///
/// Artifact Creature — Soldier 2/2. Oracle text:
///   "Metalcraft — As long as you control three or more artifacts,
///    Etched Champion has protection from all colors."
///
/// Covers:
///   - Identity (dual Artifact + Creature, Soldier subtype, {3}, 2/2,
///     owner/controller).
///   - NamedCardFactory dispatch.
///   - Single ProtectionAbility with quality "all colors" and an
///     IsActive gate (conditional protection).
///   - Metalcraft gate: 0 / 2 / 3 / 4 artifacts controlled — protection
///     inactive at 0 / 2, active at 3 / 4.
///   - Opponent's artifacts do NOT count toward Metalcraft.
///   - HasProtectionFromColor returns the gated read for every colour.
/// </summary>
public class EtchedChampionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void EtchedChampion_Identity()
    {
        var c = EtchedChampionFactory.Create(_alice);

        c.Name.Should().Be("Etched Champion");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Artifact Creature — CR 301.1 / 302.1");
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EtchedChampion_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Etched Champion", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Etched Champion");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.Abilities.OfType<ProtectionAbility>().Should().ContainSingle(
            p => p.Quality == "all colors",
            "the conditional Metalcraft rider is attached");
    }

    // -------------------------------------------------------------------------
    // Single ProtectionAbility with the gate
    // -------------------------------------------------------------------------

    [Fact]
    public void EtchedChampion_HasSingleConditionalProtectionAbility()
    {
        var c = EtchedChampionFactory.Create(_alice);

        var prot = c.Abilities.OfType<ProtectionAbility>().Should().ContainSingle().Subject;
        prot.Quality.Should().Be("all colors");
        prot.IsActive.Should().NotBeNull(
            "Metalcraft is conditional — gate closure is wired");
    }

    // -------------------------------------------------------------------------
    // Metalcraft gate — inactive (< 3 artifacts)
    // -------------------------------------------------------------------------

    [Fact]
    public void Metalcraft_Inactive_WhenNoArtifactsOnBattlefield()
    {
        // Etched Champion not yet on the battlefield, controller has no
        // artifacts — count is 0.
        var c = EtchedChampionFactory.Create(_alice);

        EtchedChampionFactory.MetalcraftActive(c).Should().BeFalse();
        ProtectionFromAllFiveColors(c).Should().AllBeEquivalentTo(false,
            "protection is gated off; HasProtectionFromColor is false for every colour");
    }

    [Fact]
    public void Metalcraft_Inactive_WithTwoArtifactsTotal_IncludingSelf()
    {
        // Etched Champion + one other artifact = 2 artifacts. Threshold
        // requires 3.
        var c = EtchedChampionFactory.Create(_alice);
        PutOnBattlefield(_alice, c);
        PutOnBattlefield(_alice, new Artifact("Other Artifact", "{1}"));

        EtchedChampionFactory.MetalcraftActive(c).Should().BeFalse();
        ProtectionFromAllFiveColors(c).Should().AllBeEquivalentTo(false);
    }

    // -------------------------------------------------------------------------
    // Metalcraft gate — active (>= 3 artifacts)
    // -------------------------------------------------------------------------

    [Fact]
    public void Metalcraft_Active_WithThreeArtifacts_IncludingSelf()
    {
        var c = EtchedChampionFactory.Create(_alice);
        PutOnBattlefield(_alice, c);
        PutOnBattlefield(_alice, new Artifact("Artifact A", "{1}"));
        PutOnBattlefield(_alice, new Artifact("Artifact B", "{1}"));

        EtchedChampionFactory.MetalcraftActive(c).Should().BeTrue();
        ProtectionFromAllFiveColors(c).Should().AllBeEquivalentTo(true,
            "Metalcraft active → protection from W/U/B/R/G");
    }

    [Fact]
    public void Metalcraft_Active_WithFourArtifacts()
    {
        var c = EtchedChampionFactory.Create(_alice);
        PutOnBattlefield(_alice, c);
        for (var i = 0; i < 3; i++)
        {
            PutOnBattlefield(_alice, new Artifact($"Artifact {i}", "{1}"));
        }

        EtchedChampionFactory.MetalcraftActive(c).Should().BeTrue();
        ProtectionFromAllFiveColors(c).Should().AllBeEquivalentTo(true);
    }

    // -------------------------------------------------------------------------
    // Metalcraft gate — opponent's artifacts don't count
    // -------------------------------------------------------------------------

    [Fact]
    public void Metalcraft_OpponentsArtifactsDoNotCount()
    {
        var c = EtchedChampionFactory.Create(_alice);
        PutOnBattlefield(_alice, c);

        // Alice has only Etched Champion (1 artifact). Bob has 3
        // artifacts. From Alice's perspective: only her own count,
        // which is 1 → Metalcraft inactive.
        for (var i = 0; i < 3; i++)
        {
            PutOnBattlefield(_bob, new Artifact($"Bob Artifact {i}", "{1}"));
        }

        EtchedChampionFactory.MetalcraftActive(c).Should().BeFalse(
            "Bob's artifacts don't contribute to Alice's Metalcraft");
        ProtectionFromAllFiveColors(c).Should().AllBeEquivalentTo(false);
    }

    // -------------------------------------------------------------------------
    // Live re-evaluation — protection turns on/off as artifacts move
    // -------------------------------------------------------------------------

    [Fact]
    public void Metalcraft_LiveReEvaluation_TogglesProtection()
    {
        var c = EtchedChampionFactory.Create(_alice);
        PutOnBattlefield(_alice, c);
        var a = new Artifact("Toggle Artifact A", "{1}");
        PutOnBattlefield(_alice, a);
        var b = new Artifact("Toggle Artifact B", "{1}");
        PutOnBattlefield(_alice, b);

        // 3 artifacts total — active.
        Protection.HasProtectionFromColor(c, ManaColor.Red).Should().BeTrue();

        // Sacrifice 'a' — drop to 2 artifacts → inactive.
        _alice.Zones.Battlefield.RemoveCard(a);
        a.SetZone(ZoneType.Graveyard);

        Protection.HasProtectionFromColor(c, ManaColor.Red).Should().BeFalse(
            "Metalcraft is re-evaluated live; with 2 artifacts the gate is off");

        // Replay 'a' — back to 3 artifacts → active again.
        PutOnBattlefield(_alice, a);

        Protection.HasProtectionFromColor(c, ManaColor.Red).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a list of five bools — one per WUBRG colour — answering
    /// <see cref="Protection.HasProtectionFromColor"/> for that colour.
    /// </summary>
    private static List<bool> ProtectionFromAllFiveColors(Creature card)
    {
        var colors = new[]
        {
            ManaColor.White, ManaColor.Blue, ManaColor.Black,
            ManaColor.Red, ManaColor.Green,
        };
        return colors.Select(c => Protection.HasProtectionFromColor(card, c)).ToList();
    }
}
