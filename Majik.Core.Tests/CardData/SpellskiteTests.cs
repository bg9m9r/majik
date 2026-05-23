using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="SpellskiteFactory"/> — Artifact Creature — Horror
/// {2} 0/4 with two parallel <see cref="ActivatedAbility"/> instances
/// modeling the printed {U/P} pip:
///   "{U}: Change the target of target spell or ability with a single
///         target to Spellskite."
///   "Pay 2 life: same effect."
///
/// Covers:
/// - Card identity (Artifact + Creature, Horror, {2}, 0/4).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Pay-{U} variant redirects a single-target spell's chosen target
///   (e.g. Lightning Bolt) to Spellskite.
/// - Pay-2-life variant performs the same redirect.
/// - Multi-target spell (ChosenTargets.Count > 1) is ineligible — no-op
///   at resolution (CR 608.2b).
/// </summary>
public class SpellskiteTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Spellskite_IsArtifactCreature_Horror_0_4_For2Mana()
    {
        var skite = SpellskiteFactory.Create(_alice);

        skite.Name.Should().Be("Spellskite");
        skite.ManaCost.Should().Be("{2}");
        skite.HasType(CardType.Creature).Should().BeTrue();
        skite.HasType(CardType.Artifact).Should().BeTrue();
        skite.Subtypes.Should().Contain(CardSubtype.Horror);
        skite.BasePower.Should().Be(0);
        skite.BaseToughness.Should().Be(4);
        skite.Owner.Should().BeSameAs(_alice);
        skite.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Spellskite()
    {
        var card = NamedCardFactory.Create("Spellskite", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Spellskite");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void Spellskite_HasTwoActivatedAbilities_OneManaOneLife()
    {
        var skite = SpellskiteFactory.Create(_alice);
        var abilities = skite.Abilities.OfType<ActivatedAbility>().ToList();

        abilities.Should().HaveCount(2);

        // Both target a single spell/ability.
        abilities.Should().OnlyContain(a => a.TargetRequests.Count == 1
                                            && a.TargetRequests[0].MinTargets == 1
                                            && a.TargetRequests[0].MaxTargets == 1);

        // One has {U} mana cost.
        abilities.Should().ContainSingle(a => a.Costs.OfType<ManaCostCost>()
            .Any(c => c.Description.Contains("U")));

        // One has pay-2-life additional cost.
        abilities.Should().ContainSingle(a => a.Costs.OfType<AdditionalCost>()
            .Any(c => c.CostType == AdditionalCostType.PayLife
                      && c.Description.Contains("2")));
    }

    // -----------------------------------------------------------------------
    // Redirect: pay {U}
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_PayU_Redirects_LightningBolt_ToSpellskite()
    {
        // Alice controls Spellskite. Bob casts Lightning Bolt targeting
        // Alice's other creature. Alice activates Spellskite's {U} ability
        // and redirects the Bolt onto Spellskite.
        var skite = SpellskiteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(skite);
        skite.SetZone(ZoneType.Battlefield);

        var originalTarget = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        originalTarget.SetOwner(_alice);
        originalTarget.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(originalTarget);
        originalTarget.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetZone(ZoneType.Stack);
        var boltSpell = new Spell(
            bolt, _bob,
            effects: new[] { new Effect("dmg", () => originalTarget.TakeDamage(3)) });
        boltSpell.ChosenTargets.Add(originalTarget);

        var manaAbility = skite.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());

        manaAbility.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { boltSpell },
        });
        manaAbility.Resolve();

        boltSpell.ChosenTargets.Should().HaveCount(1);
        boltSpell.ChosenTargets[0].Should().BeSameAs(skite,
            "Spellskite's redirect should rewrite the Bolt's chosen target to Spellskite");
    }

    // -----------------------------------------------------------------------
    // Redirect: pay 2 life
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Pay2Life_Redirects_LightningBolt_ToSpellskite()
    {
        var skite = SpellskiteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(skite);
        skite.SetZone(ZoneType.Battlefield);

        var originalTarget = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        originalTarget.SetOwner(_alice);
        originalTarget.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(originalTarget);
        originalTarget.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetZone(ZoneType.Stack);
        var boltSpell = new Spell(
            bolt, _bob,
            effects: new[] { new Effect("dmg", () => originalTarget.TakeDamage(3)) });
        boltSpell.ChosenTargets.Add(originalTarget);

        var lifeAbility = skite.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.PayLife));

        lifeAbility.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { boltSpell },
        });
        lifeAbility.Resolve();

        // Same redirect semantics — the 2-life path is observationally
        // equivalent to the {U} path on the redirect effect itself.
        boltSpell.ChosenTargets.Should().HaveCount(1);
        boltSpell.ChosenTargets[0].Should().BeSameAs(skite);
    }

    // -----------------------------------------------------------------------
    // Multi-target spell: ineligible
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_MultiTargetSpell_IsIneligible_NoRedirect()
    {
        // CR 608.2b — Spellskite's effect requires the targeted spell to
        // have exactly one target. A two-target spell (e.g. Electrolyze
        // splitting 2 damage across two creatures) is an illegal target;
        // the ability does nothing at resolution.
        var skite = SpellskiteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(skite);
        skite.SetZone(ZoneType.Battlefield);

        var creatureA = new Creature("Bear A", "{1}{G}", 1, 1);
        creatureA.SetOwner(_alice);
        creatureA.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(creatureA);
        creatureA.SetZone(ZoneType.Battlefield);

        var creatureB = new Creature("Bear B", "{1}{G}", 1, 1);
        creatureB.SetOwner(_alice);
        creatureB.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(creatureB);
        creatureB.SetZone(ZoneType.Battlefield);

        var electrolyze = new Instant("Electrolyze", "{1}{U}{R}");
        electrolyze.SetOwner(_bob);
        electrolyze.SetZone(ZoneType.Stack);
        var spell = new Spell(electrolyze, _bob, effects: Array.Empty<IEffect>());
        spell.ChosenTargets.Add(creatureA);
        spell.ChosenTargets.Add(creatureB);

        var manaAbility = skite.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());

        manaAbility.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { spell },
        });
        manaAbility.Resolve();

        // Both original targets untouched; the multi-target spell is not
        // a legal target for Spellskite's redirect.
        spell.ChosenTargets.Should().HaveCount(2);
        spell.ChosenTargets[0].Should().BeSameAs(creatureA);
        spell.ChosenTargets[1].Should().BeSameAs(creatureB);
    }
}
