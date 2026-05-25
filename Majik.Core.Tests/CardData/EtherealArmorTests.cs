using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EtherealArmorFactory"/>.
///
/// Card: Ethereal Armor — Enchantment — Aura {W} (Return to Ravnica).
///   "Enchant creature"
///   "Enchanted creature gets +1/+1 for each enchantment you control
///    and has first strike."
///
/// Covers:
///   - Identity / dispatch.
///   - Aura subtype.
///   - Dynamic +N/+N boost via AttachedBoostEffect (Layer 7c) where N =
///     enchantments you control (includes Ethereal Armor itself).
///   - First Strike granted to the enchanted creature.
///   - Boost is inert when the armor is unattached.
///   - Build-spell-definition emits a creature-only target predicate.
/// </summary>
public class EtherealArmorTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void EtherealArmor_Identity()
    {
        var c = EtherealArmorFactory.Create(_alice);

        c.Name.Should().Be("Ethereal Armor");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
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
    // Dynamic +N/+N — N = enchantments you control
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_PlusOneBoost_PerEnchantment_CountsArmorItself()
    {
        var effects = new ContinuousEffectsService();
        var armor = EtherealArmorFactory.Create(_alice, effects);
        PlaceOnBattlefield(armor, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        armor.AttachTo(bear);

        var chars = effects.Compute(bear);
        // Armor itself is an enchantment on Alice's battlefield → N = 1.
        chars.Power.Should().Be(3, "2 + 1 (Ethereal Armor itself) = 3");
        chars.Toughness.Should().Be(3, "2 + 1 (Ethereal Armor itself) = 3");
    }

    [Fact]
    public void Static_PlusNBoost_ScalesWithEnchantmentCount()
    {
        var effects = new ContinuousEffectsService();
        var armor = EtherealArmorFactory.Create(_alice, effects);
        PlaceOnBattlefield(armor, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        armor.AttachTo(bear);

        // Drop two more vanilla enchantments on Alice's side.
        var pacifism = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });
        pacifism.SetOwner(_alice);
        pacifism.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(pacifism);
        pacifism.SetZone(ZoneType.Battlefield);

        var honor = new Enchantment("Honor of the Pure", "{1}{W}",
            supertypes: null, subtypes: null);
        honor.SetOwner(_alice);
        honor.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(honor);
        honor.SetZone(ZoneType.Battlefield);

        var chars = effects.Compute(bear);
        // 3 enchantments (armor + pacifism + honor) → +3/+3 → 5/5.
        chars.Power.Should().Be(5, "2 + 3 enchantments = 5");
        chars.Toughness.Should().Be(5, "2 + 3 enchantments = 5");
    }

    [Fact]
    public void Static_GrantsFirstStrike()
    {
        var effects = new ContinuousEffectsService();
        var armor = EtherealArmorFactory.Create(_alice, effects);
        PlaceOnBattlefield(armor, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

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

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        // Don't attach.
        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().NotContain("First Strike");
    }

    [Fact]
    public void CountEnchantments_ReturnsZero_WhenOrphaned()
    {
        // Isolated card with no owner/controller wiring.
        var floating = new Enchantment("Floating", "{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });
        EtherealArmorFactory.CountEnchantments(floating).Should().Be(0,
            "no controller/owner → count gates to 0");
    }

    // -----------------------------------------------------------------------
    // Spell definition — target predicate filters to creatures
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_FiltersCreaturesOnly()
    {
        var armor = EtherealArmorFactory.Create(_alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var land = new Land("Plains");
        land.SetOwner(_alice);
        land.SetController(_alice);

        var battlefield = new Permanent[] { bear, land };
        var def = EtherealArmorFactory.BuildSpellDefinition(armor, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(bear);
        candidates.Should().NotContain(land);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void PlaceOnBattlefield(Enchantment aura, Player owner)
    {
        aura.SetOwner(owner);
        aura.SetController(owner);
        owner.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
    }
}
