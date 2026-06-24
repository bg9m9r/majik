using FluentAssertions;
using Majik.Core.Abilities;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Split Up (Mirage, {1}{W}{W}, Sorcery).
///
/// Oracle text (verified against Scryfall):
///   "Choose one —
///     • Destroy all tapped creatures.
///     • Destroy all untapped creatures."
///
/// Modal "Choose one —" sweeper (CR 700.2d). Each mode destroys every creature
/// (CR 701.7) matching a tapped-state predicate evaluated on resolution
/// (CR 608.2). Neither mode takes a target, so the
/// <see cref="SpellDefinition.EffectFactory"/> is exercised directly with
/// crafted <see cref="ChosenSpellParams"/> — same pattern as
/// <see cref="CleansingNovaFactoryTests"/>.
/// </summary>
[Trait("Color", "W")]
public class SplitUpFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity (non-vanilla cost asserted once)
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasSorceryShape_White_At1WW()
    {
        var card = SplitUpFactory.Create(_alice);

        card.Name.Should().Be("Split Up");
        card.ManaCost.Should().Be("{1}{W}{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.ManaCostValue.TotalValue.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildDefinition_TwoModes_NoTargetRequests()
    {
        var def = SplitUpFactory.BuildDefinition(new[] { _alice, _bob });

        def.Modes.Should().HaveCount(2);
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty(
            because: "both modes are untargeted board sweeps (CR 700.2d)");
    }

    // -----------------------------------------------------------------------
    // Mode 0 — destroy all tapped creatures
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_DestroysOnlyTappedCreatures_AcrossAllBattlefields()
    {
        var aliceTapped = NewCreature(_alice, "Grizzly Bears", "{1}{G}", tap: true);
        var bobTapped = NewCreature(_bob, "Hill Giant", "{3}{R}", tap: true);
        var aliceUntapped = NewCreature(_alice, "Savannah Lions", "{W}", tap: false);

        ResolveMode(SplitUpFactory.ModeDestroyTapped);

        aliceTapped.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 0 destroys all tapped creatures (CR 701.7)");
        bobTapped.Zone.Should().Be(ZoneType.Graveyard,
            because: "the sweep reaches every battlefield regardless of controller (CR 109.5)");
        aliceUntapped.Zone.Should().Be(ZoneType.Battlefield,
            because: "an untapped creature is spared by mode 0");
    }

    [Fact]
    public void Mode0_DoesNotDestroyNonCreatures()
    {
        var tappedArtifact = NewNonCreaturePermanent<Artifact>(_bob, "Sol Ring", "{1}", tap: true);
        var tappedLand = NewNonCreaturePermanent<Land>(_bob, "Plains", "", tap: true);

        ResolveMode(SplitUpFactory.ModeDestroyTapped);

        tappedArtifact.Zone.Should().Be(ZoneType.Battlefield,
            because: "Split Up only destroys creatures, not tapped artifacts");
        tappedLand.Zone.Should().Be(ZoneType.Battlefield,
            because: "Split Up only destroys creatures, not tapped lands");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — destroy all untapped creatures
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_DestroysOnlyUntappedCreatures_AcrossAllBattlefields()
    {
        var aliceUntapped = NewCreature(_alice, "Savannah Lions", "{W}", tap: false);
        var bobUntapped = NewCreature(_bob, "Grizzly Bears", "{1}{G}", tap: false);
        var aliceTapped = NewCreature(_alice, "Hill Giant", "{3}{R}", tap: true);

        ResolveMode(SplitUpFactory.ModeDestroyUntapped);

        aliceUntapped.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 1 destroys all untapped creatures (CR 701.7)");
        bobUntapped.Zone.Should().Be(ZoneType.Graveyard,
            because: "the sweep reaches every battlefield regardless of controller (CR 109.5)");
        aliceTapped.Zone.Should().Be(ZoneType.Battlefield,
            because: "a tapped creature is spared by mode 1");
    }

    // -----------------------------------------------------------------------
    // Mode selection
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultMode_IsDestroyTapped_WhenNoSelectorSupplied()
    {
        var tapped = NewCreature(_bob, "Grizzly Bears", "{1}{G}", tap: true);
        var untapped = NewCreature(_bob, "Savannah Lions", "{W}", tap: false);

        var def = SplitUpFactory.BuildDefinition(new[] { _alice, _bob });
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        tapped.Zone.Should().Be(ZoneType.Graveyard,
            because: "no explicit mode → defaults to destroy all tapped creatures (mode 0)");
        untapped.Zone.Should().Be(ZoneType.Battlefield,
            because: "the untapped creature survives the default tapped sweep");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void ResolveMode(int mode)
    {
        var def = SplitUpFactory.BuildDefinition(new[] { _alice, _bob });
        var chosen = new ChosenSpellParams(
            ModeIndex: mode,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[] { mode });

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();
    }

    private static Creature NewCreature(Player owner, string name, string cost, bool tap)
    {
        var c = new Creature(name, cost, power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        if (tap) c.Tap();
        return c;
    }

    private static T NewNonCreaturePermanent<T>(Player owner, string name, string cost, bool tap)
        where T : Permanent
    {
        Permanent p = typeof(T) == typeof(Artifact)
            ? new Artifact(name, cost)
            : typeof(T) == typeof(Land)
                ? new Land(name)
                : throw new InvalidOperationException($"Unsupported type {typeof(T)}");

        p.SetOwner(owner);
        p.SetController(owner);
        p.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(p);
        if (tap) p.Tap();
        return (T)p;
    }
}
