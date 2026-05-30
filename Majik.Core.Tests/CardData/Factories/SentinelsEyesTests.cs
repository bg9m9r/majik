using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SentinelsEyesFactory"/>.
///
/// Card: Sentinel's Eyes — Enchantment — Aura {W} (Theros Beyond Death).
///   "Enchant creature
///    Enchanted creature gets +1/+1 and has vigilance.
///    Escape—{W}, Exile two other cards from your graveyard."
///
/// Covers:
///   - Identity / dispatch / Aura subtype.
///   - +1/+1 boost via AttachedBoostEffect (Layer 7c).
///   - Granted keyword: Vigilance (Layer 6).
///   - Boost inert while unattached.
///   - "Enchant creature" target predicate (creatures only).
///   - Escape alt-cost shape: {W}, exile two OTHER graveyard cards.
/// </summary>
public class SentinelsEyesTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SentinelsEyes_Identity()
    {
        var c = SentinelsEyesFactory.Create(_alice);

        c.Name.Should().Be("Sentinel's Eyes");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SentinelsEyes()
    {
        var card = NamedCardFactory.Create("Sentinel's Eyes", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Sentinel's Eyes");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Static boost — +1/+1 + vigilance
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_PlusOnePlusOne_AppliesToAttachedCreature()
    {
        var effects = new ContinuousEffectsService();
        var aura = SentinelsEyesFactory.Create(_alice, effects);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        aura.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(3, "2 + 1 = 3");
        chars.Toughness.Should().Be(3, "2 + 1 = 3");
    }

    [Fact]
    public void Static_GrantsVigilance()
    {
        var effects = new ContinuousEffectsService();
        var aura = SentinelsEyesFactory.Create(_alice, effects);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        aura.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Keywords.Should().Contain("Vigilance");
    }

    [Fact]
    public void Static_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var aura = SentinelsEyesFactory.Create(_alice, effects);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear");

        // Don't attach.
        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().NotContain("Vigilance");
    }

    // -----------------------------------------------------------------------
    // Target predicate — "Enchant creature"
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_FiltersOnlyCreatures()
    {
        var aura = SentinelsEyesFactory.Create(_alice);

        var bear = NewCreature("Bear");
        var land = new Land("Plains");

        var battlefield = new Permanent[] { bear, land };
        var def = SentinelsEyesFactory.BuildSpellDefinition(aura, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(bear);
        candidates.Should().NotContain(land, "the printed clause is 'Enchant creature'");
    }

    // -----------------------------------------------------------------------
    // Escape — CR 702.138
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildAlternativeCost_ReturnsEscapeAltCost_WithPrintedShape()
    {
        var cost = SentinelsEyesFactory.BuildAlternativeCost();

        cost.ExileFromGraveyardCount.Should().Be(2,
            "Sentinel's Eyes' printed Escape rider exiles 2 OTHER graveyard cards");
        // {W} = 1 white, 0 generic.
        cost.AlternativeManaCost.White.Should().Be(1);
        cost.AlternativeManaCost.Generic.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreature(string name) => new(name, "{1}{G}", 2, 2);

    private Creature NewCreatureOnBattlefield(string name)
    {
        var bear = NewCreature(name);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        return bear;
    }

    private static void PlaceOnBattlefield(Enchantment aura, Player owner)
    {
        aura.SetOwner(owner);
        aura.SetController(owner);
        owner.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
    }
}
