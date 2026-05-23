using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Damping Sphere (Dominaria, {2}).
///
/// Oracle:
///   "If a land is tapped for two or more mana, it produces {C} instead
///    of any other type and amount."
///   "Each spell a player casts costs {1} more to cast for each other
///    spell that player has cast this turn."
///
/// Coverage:
///   * Identity + dispatch through <see cref="NamedCardFactory"/>.
///   * Land-mana cap: Tron-assembled Urza's Tower normally produces {2};
///     with Damping Sphere on the battlefield it produces {C} (one
///     colourless).
///   * Land-mana cap: a {C}-producing basic land is unchanged when
///     Damping Sphere is in play (the cap only fires for ≥2-mana taps).
///   * Per-spell cost rider: 1st cast costs printed; 2nd cast costs +1;
///     3rd cast costs +2.
///   * Per-spell cost rider is per-player (one caster's tally doesn't
///     tax another caster's spells).
///   * Mana cap is symmetric — controller's own Tron is capped too.
/// </summary>
public class DampingSphereTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------------------------------------------------------------------
    // Identity + dispatch
    // ---------------------------------------------------------------------

    [Fact]
    public void Create_HasArtifactShape_TwoGenericCost()
    {
        var sphere = DampingSphereFactory.Create(_alice);

        sphere.Name.Should().Be("Damping Sphere");
        sphere.HasType(CardType.Artifact).Should().BeTrue();
        sphere.ManaCost.Should().Be("{2}");
        sphere.ManaCostValue.Generic.Should().Be(2);
        sphere.ManaCostValue.TotalValue.Should().Be(2);
        sphere.Owner.Should().BeSameAs(_alice);
        sphere.Controller.Should().BeSameAs(_alice);

        sphere.Abilities.OfType<SpellCostIncreaseAbility>()
            .Should().HaveCount(1, "the per-spell cost-increase rider is attached");
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsDampingSphereShape()
    {
        var dispatched = NamedCardFactory.Create("Damping Sphere", _alice);

        dispatched.Should().BeOfType<Artifact>();
        dispatched.Name.Should().Be("Damping Sphere");
        dispatched.Abilities.OfType<SpellCostIncreaseAbility>().Should().HaveCount(1);
    }

    // ---------------------------------------------------------------------
    // Rider 1 — land-mana cap (CR 605, oracle text)
    // ---------------------------------------------------------------------

    [Fact]
    public void TronTower_WithDampingSphereOut_TapsForC_NotTwo()
    {
        // Assemble Tron on Alice.
        var mine = UrzasMineFactory.Create(_alice);
        var tower = UrzasTowerFactory.Create(_alice);
        var plant = UrzasPowerPlantFactory.Create(_alice);
        foreach (var land in new Permanent[] { mine, tower, plant })
        {
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }

        // Bob plops a Damping Sphere — symmetric, caps Alice's lands too.
        var sphere = DampingSphereFactory.Create(_bob);
        _bob.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        // Sanity baseline: WITHOUT the all-players list, Tron tower still
        // produces {2} via the printed Func<ManaCost> ability.
        var baselineAbilities =
            EffectiveManaAbilities.For(tower, layers: null, controller: _alice);
        var baseline = baselineAbilities.Single().Activate();
        baseline.Generic.Should().Be(2, "no Damping Sphere awareness when allPlayers is null");

        // Reset tap state — Tron tower is now tapped from the baseline call.
        tower.Untap();

        // WITH the all-players list, the wrapper caps the {2} output to {C}.
        var cappedAbilities = EffectiveManaAbilities.For(
            tower,
            layers: null,
            controller: _alice,
            allPlayers: new[] { _alice, _bob });
        var capped = cappedAbilities.Single().Activate();

        capped.Generic.Should().Be(1, "Damping Sphere caps any ≥2-mana land tap to {C}");
        capped.TotalValue.Should().Be(1);
    }

    [Fact]
    public void BasicMountain_WithDampingSphereOut_StillTapsForR()
    {
        // {R} (single-mana) basic lands are below the ≥2 threshold — the
        // cap doesn't fire and the printed {R} mana ability is returned
        // unchanged.
        var mountain = new Land(
            "Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        mountain.AddAbility(new ManaAbility(
            mountain, _alice, Majik.Core.ValueObjects.ManaCost.Parse("R")));
        _alice.Zones.Battlefield.AddCard(mountain);
        mountain.SetZone(ZoneType.Battlefield);

        var sphere = DampingSphereFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        var abilities = EffectiveManaAbilities.For(
            mountain,
            layers: null,
            controller: _alice,
            allPlayers: new[] { _alice, _bob });
        var produced = abilities.Single().Activate();

        produced.Red.Should().Be(1, "{R} produces 1 < 2 mana, so the cap doesn't fire");
        produced.TotalValue.Should().Be(1);
    }

    [Fact]
    public void TronTower_WithoutDampingSphere_StillTapsForTwo()
    {
        // Regression — when Damping Sphere is NOT in play (anywhere), the
        // cap path is inert even when allPlayers is supplied.
        var mine = UrzasMineFactory.Create(_alice);
        var tower = UrzasTowerFactory.Create(_alice);
        var plant = UrzasPowerPlantFactory.Create(_alice);
        foreach (var land in new Permanent[] { mine, tower, plant })
        {
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }

        var abilities = EffectiveManaAbilities.For(
            tower,
            layers: null,
            controller: _alice,
            allPlayers: new[] { _alice, _bob });
        var produced = abilities.Single().Activate();

        produced.Generic.Should().Be(2, "Tron assembled — printed {2} stands");
    }

    // ---------------------------------------------------------------------
    // Rider 2 — per-spell cost increase (CR 117.7)
    // ---------------------------------------------------------------------

    [Fact]
    public void SpellCost_ScalesWithCasterSpellsThisTurn()
    {
        var turnState = new TurnState();
        var sphere = DampingSphereFactory.Create(_alice, turnState);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        // A simple 1-mana spell to gauge the cost progression.
        var lightningBolt = new Instant("Lightning Bolt", "{R}");
        lightningBolt.SetOwner(_alice);
        lightningBolt.SetController(_alice);

        // 1st cast — no prior spells this turn → printed cost.
        var first = CostReduction.GetEffectiveCost(
            lightningBolt, _alice, new[] { _alice, _bob });
        first.Generic.Should().Be(0, "1st spell — 0 OTHER spells cast → no rider");
        first.Red.Should().Be(1, "coloured pips are untouched (CR 117.7c)");

        // After recording the 1st cast, the 2nd cast pays +1 generic.
        turnState.RecordSpellCast(_alice, new HashSet<Majik.Core.ValueObjects.ManaColor>());
        var second = CostReduction.GetEffectiveCost(
            lightningBolt, _alice, new[] { _alice, _bob });
        second.Generic.Should().Be(1, "2nd spell — 1 OTHER spell → +{1}");
        second.Red.Should().Be(1);
        second.TotalValue.Should().Be(2);

        // 3rd cast pays +2 generic.
        turnState.RecordSpellCast(_alice, new HashSet<Majik.Core.ValueObjects.ManaColor>());
        var third = CostReduction.GetEffectiveCost(
            lightningBolt, _alice, new[] { _alice, _bob });
        third.Generic.Should().Be(2, "3rd spell — 2 OTHER spells → +{2}");
        third.Red.Should().Be(1);
        third.TotalValue.Should().Be(3);
    }

    [Fact]
    public void SpellCost_IsPerPlayer_OneCastersTallyDoesntAffectOther()
    {
        var turnState = new TurnState();
        var sphere = DampingSphereFactory.Create(_alice, turnState);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        var aliceSpell = new Instant("Alice Spell", "{1}{U}");
        aliceSpell.SetOwner(_alice);
        aliceSpell.SetController(_alice);
        var bobSpell = new Instant("Bob Spell", "{1}{U}");
        bobSpell.SetOwner(_bob);
        bobSpell.SetController(_bob);

        // Alice casts 3 spells, leaving _spellsCastByPlayer[_alice] = 3.
        for (var i = 0; i < 3; i++)
        {
            turnState.RecordSpellCast(_alice, new HashSet<Majik.Core.ValueObjects.ManaColor>());
        }

        // Bob's first cast this turn — no prior Bob spells → no rider.
        var bobFirst = CostReduction.GetEffectiveCost(
            bobSpell, _bob, new[] { _alice, _bob });
        bobFirst.Generic.Should().Be(1, "Bob's first cast — printed {1}, no Damping Sphere tax");

        // Alice's next spell pays +3 (three prior Alice spells).
        var aliceNext = CostReduction.GetEffectiveCost(
            aliceSpell, _alice, new[] { _alice, _bob });
        aliceNext.Generic.Should().Be(1 + 3, "3 prior Alice spells → +{3}");
    }

    [Fact]
    public void SpellCost_NoIncrease_WhenDampingSphereOffBattlefield()
    {
        // Damping Sphere lives in hand, not on the battlefield → no
        // cost-increase rider applies.
        var turnState = new TurnState();
        for (var i = 0; i < 5; i++)
        {
            turnState.RecordSpellCast(_alice, new HashSet<Majik.Core.ValueObjects.ManaColor>());
        }

        var sphere = DampingSphereFactory.Create(_alice, turnState);
        _alice.Zones.Hand.AddCard(sphere); // off-battlefield
        sphere.SetZone(ZoneType.Hand);

        var spell = new Instant("Lightning Bolt", "{R}");
        spell.SetOwner(_alice);
        spell.SetController(_alice);

        var cost = CostReduction.GetEffectiveCost(
            spell, _alice, new[] { _alice, _bob });
        cost.Generic.Should().Be(0, "Damping Sphere isn't on the battlefield → rider inert");
        cost.Red.Should().Be(1);
    }
}
