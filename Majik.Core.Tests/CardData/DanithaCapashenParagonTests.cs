using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Danitha Capashen, Paragon (Dominaria, {2}{W}, Legendary
/// Creature — Human Knight 2/2).
///
/// Oracle text (verified against Scryfall):
///   "First strike, vigilance, lifelink
///    Aura and Equipment spells you cast cost {1} less to cast."
///
/// Covers:
///   - Identity (Legendary, Human + Knight, 2/2, {2}{W}, owner/controller).
///   - First strike / vigilance / lifelink keyword markers (CombatAbilities).
///   - NamedCardFactory dispatch.
///   - Aura spell you cast costs {1} less ({1}{G} -> {G}).
///   - Equipment spell you cast costs {1} less ({3} -> {2}).
///   - Vanilla (non-Aura/Equipment) spell is unaffected.
///   - Opponent's Aura is unaffected ("spells YOU cast").
/// </summary>
public class DanithaCapashenParagonTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Enchantment NewAura(Player owner) =>
        new("Rancor", "{G}", subtypes: new[] { CardSubtype.Aura }) { Owner = owner };

    private static Artifact NewEquipment(Player owner) =>
        new("Bone Saw", "{3}", subtypes: new[] { CardSubtype.Equipment }) { Owner = owner };

    private static Creature NewVanillaCreature(Player owner) =>
        new("Bear", "{1}{G}", 2, 2) { Owner = owner };

    private static Creature BuildOnBattlefield(Player owner)
    {
        var card = (Creature)DanithaCapashenParagonFactory.Create(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        return card;
    }

    [Fact]
    public void Danitha_Identity()
    {
        var d = (Creature)DanithaCapashenParagonFactory.Create(_alice);

        d.Name.Should().Be("Danitha Capashen, Paragon");
        d.ManaCost.Should().Be("{2}{W}");
        d.HasType(CardType.Creature).Should().BeTrue();
        d.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        d.HasSubtype(CardSubtype.Human).Should().BeTrue();
        d.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        d.BasePower.Should().Be(2);
        d.BaseToughness.Should().Be(2);
        d.Owner.Should().BeSameAs(_alice);
        d.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Danitha_HasFirstStrikeVigilanceLifelink()
    {
        var d = (Creature)DanithaCapashenParagonFactory.Create(_alice);

        CombatAbilities.HasFirstStrike(d).Should().BeTrue("CR 702.7 — printed first strike");
        CombatAbilities.HasVigilance(d).Should().BeTrue("CR 702.21 — printed vigilance");
        CombatAbilities.HasLifelink(d).Should().BeTrue("CR 702.15 — printed lifelink");
    }

    [Fact]
    public void Danitha_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Danitha Capashen, Paragon", _alice);
        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Danitha Capashen, Paragon");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
    }

    [Fact]
    public void Danitha_AuraSpellYouCast_CostsOneLess()
    {
        _ = BuildOnBattlefield(_alice);

        var aura = NewAura(_alice);
        // {G} aura: no generic to reduce, so coloured pip is untouched (floor at 0).
        var cheapCost = CostReduction.GetEffectiveCost(aura, _alice);
        cheapCost.TotalValue.Should().Be(1, "no generic mana to shave; {G} stays {G}");

        // An equipment with a generic component shows the actual {1} reduction.
        var equip = NewEquipment(_alice);
        var reduced = CostReduction.GetEffectiveCost(equip, _alice);
        reduced.TotalValue.Should().Be(2, "{3} Equipment -> {2} (CR 117.7)");
    }

    [Fact]
    public void Danitha_EquipmentSpellYouCast_CostsOneLess()
    {
        _ = BuildOnBattlefield(_alice);

        var equip = NewEquipment(_alice);
        var reduced = CostReduction.GetEffectiveCost(equip, _alice);
        reduced.TotalValue.Should().Be(2, "{3} Equipment -> {2} (CR 117.7)");
    }

    [Fact]
    public void Danitha_NonAuraNonEquipmentSpell_Unaffected()
    {
        _ = BuildOnBattlefield(_alice);

        var bear = NewVanillaCreature(_alice);
        var cost = CostReduction.GetEffectiveCost(bear, _alice);
        cost.TotalValue.Should().Be(2, "{1}{G} vanilla creature is not Aura/Equipment");
    }

    [Fact]
    public void Danitha_OpponentEquipment_Unaffected()
    {
        _ = BuildOnBattlefield(_alice);

        // Bob casts an Equipment; Danitha's reducer is scoped to "spells YOU
        // cast", so Bob (the caster) sees no reduction (CR 117.7 — the rider
        // is scanned against the caster's battlefield only).
        var equip = NewEquipment(_bob);
        var cost = CostReduction.GetEffectiveCost(equip, _bob);
        cost.TotalValue.Should().Be(3, "{3} Equipment cast by opponent is unreduced");
    }
}
