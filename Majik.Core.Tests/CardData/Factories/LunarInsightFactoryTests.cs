using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Lunar Insight (Duskmourn: House of Horror, {2}{U}, Sorcery).
///
/// Oracle text (verified against Scryfall):
///   "Draw a card for each different mana value among nonland permanents you
///    control."
///
/// Coverage:
/// - Card identity (Sorcery, blue, {2}{U}, CMC 3, owner/controller) loaded from
///   the embedded JSON def via the factory.
/// - SpellDefinition shape — no modes, no X, no target requests (CR 601 — the
///   spell has no targets; it counts a set at resolution).
/// - Distinct-mana-value count (CR 107.18 / CR 202.3): duplicate mana values
///   count once; lands are excluded (CR 305); a 0-cost nonland permanent
///   contributes the value 0.
/// - Resolve: caster draws exactly one card per distinct mana value (CR 121.1).
/// - Zero nonland permanents → draw 0 (CR — "for each" of an empty set).
/// </summary>
[Trait("Color", "U")]
public class LunarInsightFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity + dispatch ────────────────────────────────────────────────

    [Fact]
    public void LunarInsight_HasSorceryShape_Blue_AtCost2U()
    {
        var card = LunarInsightFactory.Create(_alice);

        card.Name.Should().Be("Lunar Insight");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCostValue.TotalValue.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // ── SpellDefinition — structural shape ─────────────────────────────────

    [Fact]
    public void LunarInsight_SpellDefinition_HasNoTargets_NoModes_NoX()
    {
        var def = LunarInsightFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    // ── Distinct-mana-value count ──────────────────────────────────────────

    [Fact]
    public void CountDistinctManaValues_CountsEachValueOnce_ExcludesLands()
    {
        // MV 1, 1, 2 across nonland permanents → 2 distinct values.
        NewBattlefieldCreature("One A", "{U}");        // MV 1
        NewBattlefieldArtifact("One B", "{U}");        // MV 1 (duplicate value)
        NewBattlefieldEnchantment("Two", "{1}{U}");    // MV 2
        NewBattlefieldLand("A Land");                  // excluded (CR 305)

        LunarInsightFactory.CountDistinctManaValues(_alice).Should().Be(2);
    }

    [Fact]
    public void CountDistinctManaValues_ZeroCostNonland_ContributesValueZero()
    {
        NewBattlefieldArtifact("Zero", "{0}");         // MV 0
        NewBattlefieldCreature("Three", "{2}{U}");     // MV 3

        LunarInsightFactory.CountDistinctManaValues(_alice).Should().Be(2);
    }

    [Fact]
    public void CountDistinctManaValues_NoNonlandPermanents_IsZero()
    {
        NewBattlefieldLand("Only Land");

        LunarInsightFactory.CountDistinctManaValues(_alice).Should().Be(0);
    }

    // ── Resolve ────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DrawsOnePerDistinctManaValue()
    {
        // 3 distinct mana values: 1, 2, 4.
        NewBattlefieldCreature("MV1 A", "{U}");
        NewBattlefieldCreature("MV1 B", "{U}");        // duplicate value → counted once
        NewBattlefieldArtifact("MV2", "{1}{U}");
        NewBattlefieldEnchantment("MV4", "{3}{U}");
        NewBattlefieldLand("Land");                    // excluded

        var l1 = NewLibraryCard("L1");
        var l2 = NewLibraryCard("L2");
        var l3 = NewLibraryCard("L3");
        var l4 = NewLibraryCard("L4");

        var effect = LunarInsightFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        // CR 121.1 — exactly 3 cards drawn (3 distinct mana values).
        _alice.Zones.Hand.GetCards().Should().Equal(new ICard[] { l1, l2, l3 });
        _alice.Zones.Library.GetCards().Should().Equal(new ICard[] { l4 });
    }

    [Fact]
    public void Resolve_NoNonlandPermanents_DrawsZero()
    {
        NewBattlefieldLand("Land");
        NewLibraryCard("L1");

        var effect = LunarInsightFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().HaveCount(1);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private Creature NewBattlefieldCreature(string name, string manaCost)
    {
        var c = new Creature(name, manaCost, 2, 2) { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        return c;
    }

    private Artifact NewBattlefieldArtifact(string name, string manaCost)
    {
        var a = new Artifact(name, manaCost) { Owner = _alice, Controller = _alice };
        a.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(a);
        return a;
    }

    private Enchantment NewBattlefieldEnchantment(string name, string manaCost)
    {
        var e = new Enchantment(name, manaCost) { Owner = _alice, Controller = _alice };
        e.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(e);
        return e;
    }

    private Land NewBattlefieldLand(string name)
    {
        var l = new Land(name) { Owner = _alice, Controller = _alice };
        l.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(l);
        return l;
    }

    private ICard NewLibraryCard(string name)
    {
        var c = new Sorcery(name, "{0}") { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c);
        return c;
    }
}
