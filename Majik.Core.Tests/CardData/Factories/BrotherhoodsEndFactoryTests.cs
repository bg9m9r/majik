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
/// Tests for Brotherhood's End (The Brothers' War, {1}{R}{R}, Sorcery).
///
/// Oracle text (verified against Scryfall + the embedded seed):
///   "Choose one —
///     • Brotherhood's End deals 3 damage to each creature and each planeswalker.
///     • Destroy all artifacts with mana value 3 or less."
///
/// Modal "Choose one —" sweeper (CR 700.2d). Mode 0 is an untargeted
/// Pyroclasm-style board sweep widened to hit planeswalkers (loyalty removal —
/// CR 119.3 / 306.7); mode 1 is mana-value-filtered mass artifact destruction
/// (CR 202.3 / 701.7). Neither mode takes a target, so the
/// <see cref="SpellDefinition.EffectFactory"/> is exercised directly with
/// crafted <see cref="ChosenSpellParams"/> — same pattern as
/// <see cref="Majik.Core.Tests.CardData.KolaghansCommandTests"/>.
/// </summary>
public class BrotherhoodsEndFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasSorceryShape_Red_AtCost1RR()
    {
        var card = BrotherhoodsEndFactory.Create(_alice);

        card.Name.Should().Be("Brotherhood's End");
        card.ManaCost.Should().Be("{1}{R}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsBrotherhoodsEndShape()
    {
        var dispatched = NamedCardFactory.Create("Brotherhood's End", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Brotherhood's End");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_TwoModes_NoTargetRequests()
    {
        var def = BrotherhoodsEndFactory.BuildDefinition(new[] { _alice, _bob });

        def.Modes.Should().HaveCount(2);
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty(
            because: "both modes are untargeted board sweeps (CR 700.2d)");
    }

    // -----------------------------------------------------------------------
    // Mode 0 — 3 damage to each creature and each planeswalker
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_Deals3DamageToEachCreature_AcrossAllBattlefields()
    {
        var aliceBear = NewControlledPermanent<Creature>(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobGiant = NewControlledPermanent<Creature>(_bob, "Hill Giant", "{3}{R}", 3, 3);

        ResolveMode(BrotherhoodsEndFactory.ModeDamageSweep);

        aliceBear.Damage.Should().Be(3,
            because: "mode 0 deals 3 damage to each creature (CR 109.5)");
        bobGiant.Damage.Should().Be(3,
            because: "the sweep reaches every battlefield regardless of controller");
    }

    [Fact]
    public void Mode0_Removes3LoyaltyFromEachPlaneswalker()
    {
        var pw = new Planeswalker("Test Walker", "{2}{R}", 5);
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        ResolveMode(BrotherhoodsEndFactory.ModeDamageSweep);

        pw.Loyalty.Should().Be(2,
            because: "3 damage to a planeswalker removes 3 loyalty (CR 119.3 / 306.7)");
    }

    [Fact]
    public void Mode0_DoesNotDestroyArtifacts()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        ResolveMode(BrotherhoodsEndFactory.ModeDamageSweep);

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 0 only damages creatures and planeswalkers");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — destroy all artifacts with mana value 3 or less
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_DestroysArtifacts_WithManaValue3OrLess()
    {
        var solRing = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");        // MV 1
        var mindStone = NewControlledPermanent<Artifact>(_alice, "Mind Stone", "{2}");   // MV 2
        var threeCost = NewControlledPermanent<Artifact>(_bob, "Coalition Relic", "{3}"); // MV 3

        ResolveMode(BrotherhoodsEndFactory.ModeDestroyArtifacts);

        solRing.Zone.Should().Be(ZoneType.Graveyard, "MV 1 ≤ 3 (CR 701.7)");
        mindStone.Zone.Should().Be(ZoneType.Graveyard,
            "the sweep is untargeted — it hits the controller's own artifacts too");
        threeCost.Zone.Should().Be(ZoneType.Graveyard, "MV 3 ≤ 3 (boundary case)");
    }

    [Fact]
    public void Mode1_SparesArtifacts_WithManaValue4OrMore()
    {
        var batterskull = NewControlledPermanent<Artifact>(_bob, "Batterskull", "{5}"); // MV 5

        ResolveMode(BrotherhoodsEndFactory.ModeDestroyArtifacts);

        batterskull.Zone.Should().Be(ZoneType.Battlefield,
            because: "mana value 5 > 3 — spared (CR 202.3)");
    }

    [Fact]
    public void Mode1_DoesNotDamageCreaturesOrPlaneswalkers()
    {
        var bear = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        ResolveMode(BrotherhoodsEndFactory.ModeDestroyArtifacts);

        bear.Damage.Should().Be(0, because: "mode 1 only destroys artifacts");
        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Mode selection
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultMode_IsDamageSweep_WhenNoSelectorSupplied()
    {
        var bear = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        var def = BrotherhoodsEndFactory.BuildDefinition(new[] { _alice, _bob });
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        bear.Damage.Should().Be(3,
            because: "no explicit mode → defaults to the damage sweep (mode 0)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void ResolveMode(int mode)
    {
        var def = BrotherhoodsEndFactory.BuildDefinition(new[] { _alice, _bob });
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
