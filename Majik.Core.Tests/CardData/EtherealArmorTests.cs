using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EtherealArmorFactory"/>.
///
/// Card: Ethereal Armor — Enchantment — Aura {W} (Return to Ravnica).
///   "Enchant creature"
///   "Enchanted creature gets +1/+1 for each enchantment you control and
///    has first strike."
///
/// Covers:
///   - Identity / dispatch (Enchantment — Aura, {W}).
///   - Dynamic +N/+N boost where N = controller's live enchantment count
///     (CR 613 Layer 7c) — including the Armor counting itself.
///   - Granted keyword: First Strike (CR 702.7).
///   - Boost is inert while unattached.
///   - "Enchant creature" cast-time target predicate filters non-creatures.
/// </summary>
public class EtherealArmorTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EtherealArmor_Identity()
    {
        var c = EtherealArmorFactory.Create(_alice);

        c.Name.Should().Be("Ethereal Armor");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_EtherealArmor()
    {
        var card = NamedCardFactory.Create("Ethereal Armor", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Ethereal Armor");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Dynamic +N/+N boost — N = controller's enchantment count
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_Boost_CountsControllersEnchantments_IncludingSelf()
    {
        var effects = new ContinuousEffectsService();
        var armor = EtherealArmorFactory.Create(_alice, effects);
        PlaceOnBattlefield(armor, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        armor.AttachTo(bear);

        // Only the Armor itself is an enchantment under Alice's control → +1/+1.
        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2 + 1, "+1/+1 from one enchantment (the Armor itself)");
        chars.Toughness.Should().Be(2 + 1);

        // Add a second enchantment under Alice's control → +2/+2.
        var pacifism = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });
        pacifism.SetOwner(_alice);
        pacifism.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(pacifism);
        pacifism.SetZone(ZoneType.Battlefield);

        chars = effects.Compute(bear);
        chars.Power.Should().Be(2 + 2, "+2/+2 from two enchantments");
        chars.Toughness.Should().Be(2 + 2);
    }

    [Fact]
    public void Static_GrantsFirstStrike()
    {
        var effects = new ContinuousEffectsService();
        var armor = EtherealArmorFactory.Create(_alice, effects);
        PlaceOnBattlefield(armor, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        armor.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Keywords.Should().Contain("First Strike");
    }

    [Fact]
    public void Static_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var armor = EtherealArmorFactory.Create(_alice, effects);
        PlaceOnBattlefield(armor, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        // Don't attach.

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().NotContain("First Strike");
    }

    // -----------------------------------------------------------------------
    // CountEnchantments helper
    // -----------------------------------------------------------------------

    [Fact]
    public void CountEnchantments_ReadsControllerBattlefield()
    {
        var armor = EtherealArmorFactory.Create(_alice);
        PlaceOnBattlefield(armor, _alice);

        EtherealArmorFactory.CountEnchantments(armor).Should().Be(1,
            "only the Armor itself is on the battlefield");

        var pacifism = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });
        pacifism.SetOwner(_alice);
        pacifism.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(pacifism);
        pacifism.SetZone(ZoneType.Battlefield);

        EtherealArmorFactory.CountEnchantments(armor).Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // "Enchant creature" target predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_FiltersToCreatures()
    {
        var armor = EtherealArmorFactory.Create(_alice);

        var bear = NewCreatureOnBattlefield("Bear");
        var land = new Land("Plains");
        var pacifism = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });

        var battlefield = new Permanent[] { bear, land, pacifism };
        var def = EtherealArmorFactory.BuildSpellDefinition(armor, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(bear);
        candidates.Should().NotContain(land);
        candidates.Should().NotContain(pacifism);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Creature NewCreatureOnBattlefield(string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static void PlaceOnBattlefield(Enchantment armor, Player owner)
    {
        owner.Zones.Battlefield.AddCard(armor);
        armor.SetZone(ZoneType.Battlefield);
    }
}
