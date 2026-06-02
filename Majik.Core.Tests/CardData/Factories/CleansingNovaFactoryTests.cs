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
/// Tests for Cleansing Nova (Core Set 2019, {3}{W}{W}, Sorcery).
///
/// Oracle text (verified against Scryfall):
///   "Choose one —
///     • Destroy all creatures.
///     • Destroy all artifacts and enchantments."
///
/// Modal "Choose one —" sweeper (CR 700.2d). Mode 0 destroys every creature
/// (CR 701.7); mode 1 destroys every artifact (CR 301) and enchantment
/// (CR 303). Neither mode takes a target, so the
/// <see cref="SpellDefinition.EffectFactory"/> is exercised directly with
/// crafted <see cref="ChosenSpellParams"/> — same pattern as
/// <see cref="BrotherhoodsEndFactoryTests"/>.
/// </summary>
[Trait("Color", "W")]
public class CleansingNovaFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasSorceryShape_White_AtCost3WW()
    {
        var card = CleansingNovaFactory.Create(_alice);

        card.Name.Should().Be("Cleansing Nova");
        card.ManaCost.Should().Be("{3}{W}{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.ManaCostValue.TotalValue.Should().Be(5);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildDefinition_TwoModes_NoTargetRequests()
    {
        var def = CleansingNovaFactory.BuildDefinition(new[] { _alice, _bob });

        def.Modes.Should().HaveCount(2);
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty(
            because: "both modes are untargeted board sweeps (CR 700.2d)");
    }

    // -----------------------------------------------------------------------
    // Mode 0 — destroy all creatures
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_DestroysEveryCreature_AcrossAllBattlefields()
    {
        var aliceBear = NewControlledPermanent<Creature>(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobGiant = NewControlledPermanent<Creature>(_bob, "Hill Giant", "{3}{R}", 3, 3);

        ResolveMode(CleansingNovaFactory.ModeDestroyCreatures);

        aliceBear.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 0 destroys all creatures (CR 701.7)");
        bobGiant.Zone.Should().Be(ZoneType.Graveyard,
            because: "the sweep reaches every battlefield regardless of controller (CR 109.5)");
    }

    [Fact]
    public void Mode0_DoesNotDestroyArtifactsOrEnchantments()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");
        var enchantment = NewControlledPermanent<Enchantment>(_bob, "Pacifism", "{1}{W}");

        ResolveMode(CleansingNovaFactory.ModeDestroyCreatures);

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 0 only destroys creatures");
        enchantment.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 0 only destroys creatures");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — destroy all artifacts and enchantments
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_DestroysEveryArtifactAndEnchantment_AcrossAllBattlefields()
    {
        var solRing = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");
        var batterskull = NewControlledPermanent<Artifact>(_alice, "Batterskull", "{5}");
        var pacifism = NewControlledPermanent<Enchantment>(_bob, "Pacifism", "{1}{W}");

        ResolveMode(CleansingNovaFactory.ModeDestroyArtifactsAndEnchantments);

        solRing.Zone.Should().Be(ZoneType.Graveyard, "all artifacts destroyed (CR 301 / 701.7)");
        batterskull.Zone.Should().Be(ZoneType.Graveyard,
            "the sweep is untargeted and has no mana-value filter — every artifact dies");
        pacifism.Zone.Should().Be(ZoneType.Graveyard, "all enchantments destroyed (CR 303 / 701.7)");
    }

    [Fact]
    public void Mode1_DoesNotDestroyCreatures()
    {
        var bear = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        ResolveMode(CleansingNovaFactory.ModeDestroyArtifactsAndEnchantments);

        bear.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 1 only destroys artifacts and enchantments");
    }

    // -----------------------------------------------------------------------
    // Mode selection
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultMode_IsDestroyCreatures_WhenNoSelectorSupplied()
    {
        var bear = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        var def = CleansingNovaFactory.BuildDefinition(new[] { _alice, _bob });
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            because: "no explicit mode → defaults to destroy all creatures (mode 0)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void ResolveMode(int mode)
    {
        var def = CleansingNovaFactory.BuildDefinition(new[] { _alice, _bob });
        var chosen = new ChosenSpellParams(
            ModeIndex: mode,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[] { mode });

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();
    }

    private T NewControlledPermanent<T>(Player owner, string name, string cost,
        int power = 0, int toughness = 0)
        where T : ICard
    {
        T card;
        if (typeof(T) == typeof(Creature))
        {
            card = (T)(ICard)new Creature(name, cost, power, toughness);
        }
        else if (typeof(T) == typeof(Artifact))
        {
            card = (T)(ICard)new Artifact(name, cost);
        }
        else if (typeof(T) == typeof(Enchantment))
        {
            card = (T)(ICard)new Enchantment(name, cost);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported type {typeof(T)}");
        }

        ((Card)(ICard)card).SetOwner(owner);
        ((Card)(ICard)card).SetController(owner);
        ((Card)(ICard)card).SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }
}
