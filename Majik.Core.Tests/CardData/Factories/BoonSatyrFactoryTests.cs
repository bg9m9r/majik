using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Boon Satyr (Theros, {1}{G}{G}) and the bestow keyword primitive
/// (<see cref="BestowKeyword"/>, CR 702.103).
///
/// Card: Enchantment Creature — Satyr 4/2.
///   "Flash
///    Bestow {3}{G}{G} (If you cast this card for its bestow cost, it's an
///    Aura spell with enchant creature. It becomes a creature again if it's
///    not attached.)
///    Enchanted creature gets +4/+2."
///
/// Covers:
///   - Identity / dispatch (Enchantment Creature — Satyr, {1}{G}{G}, 4/2, green).
///   - Flash keyword marker (CR 702.8).
///   - Bestow boost: enchanted creature gets +4/+2 while attached (CR 613).
///   - Boost inert while unattached.
///   - CR 702.103e: NOT a creature while attached as an Aura.
///   - CR 702.103f: becomes a creature again when it stops being attached.
///   - Bestow-cast spell definition filters legal targets to creatures.
///   - Bestow cost parses to {3}{G}{G}.
/// </summary>
[Trait("Color", "G")]
public class BoonSatyrFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BoonSatyr_Identity()
    {
        var c = BoonSatyrFactory.Create(_alice);

        c.Name.Should().Be("Boon Satyr");
        c.ManaCost.Should().Be("{1}{G}{G}");
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(2);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Enchantment).Should().BeTrue("Enchantment Creature carries both types (CR 205.2a)");
        c.HasSubtype(CardSubtype.Satyr).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BoonSatyr()
    {
        var card = NamedCardFactory.Create("Boon Satyr", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Boon Satyr");
        card.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void BoonSatyr_HasFlash()
    {
        var c = BoonSatyrFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flash");
    }

    // -----------------------------------------------------------------------
    // Bestow boost — "Enchanted creature gets +4/+2" (CR 613)
    // -----------------------------------------------------------------------

    [Fact]
    public void Bestow_Boost_PumpsPlus4Plus2_WhileAttached()
    {
        var effects = new ContinuousEffectsService();
        var satyr = BoonSatyrFactory.Create(_alice, effects);
        PlaceOnBattlefield(satyr, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        satyr.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2 + 4, "+4/+2 bestow boost from Boon Satyr");
        chars.Toughness.Should().Be(2 + 2);
    }

    [Fact]
    public void Bestow_Boost_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var satyr = BoonSatyrFactory.Create(_alice, effects);
        PlaceOnBattlefield(satyr, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        // Do not attach.

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2, "boost is inert until Boon Satyr is bestowed onto the creature");
        chars.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // CR 702.103e / 702.103f — dual-type state transition
    // -----------------------------------------------------------------------

    [Fact]
    public void Bestow_NotACreature_WhileAttachedAsAura()
    {
        // CR 702.103e — while attached as an Aura, the bestow card is NOT a
        // creature; it is only an Aura.
        var effects = new ContinuousEffectsService();
        var satyr = BoonSatyrFactory.Create(_alice, effects);
        PlaceOnBattlefield(satyr, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        satyr.AttachTo(bear);

        var chars = effects.Compute(satyr);
        chars.Types.Should().NotContain(CardType.Creature,
            "CR 702.103e — bestowed permanent is not a creature while attached");
        chars.Types.Should().Contain(CardType.Enchantment,
            "it is still an Enchantment (an Aura)");
    }

    [Fact]
    public void Bestow_BecomesCreatureAgain_WhenUnattached()
    {
        // CR 702.103f — if a permanent with bestow stops being attached to a
        // permanent, it becomes a creature.
        var effects = new ContinuousEffectsService();
        var satyr = BoonSatyrFactory.Create(_alice, effects);
        PlaceOnBattlefield(satyr, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        satyr.AttachTo(bear);

        // Sanity: bestowed → not a creature.
        effects.Compute(satyr).Types.Should().NotContain(CardType.Creature);

        // The enchanted creature leaves / the aura detaches.
        satyr.Unattach();

        var chars = effects.Compute(satyr);
        chars.Types.Should().Contain(CardType.Creature,
            "CR 702.103f — once unattached it becomes a creature again");
    }

    [Fact]
    public void Bestow_IsACreature_WhenCastNormally()
    {
        // Cast normally (never attached) → it is a creature the whole time.
        var effects = new ContinuousEffectsService();
        var satyr = BoonSatyrFactory.Create(_alice, effects);
        PlaceOnBattlefield(satyr, _alice);

        var chars = effects.Compute(satyr);
        chars.Types.Should().Contain(CardType.Creature,
            "an unattached bestow permanent is a normal creature");
    }

    // -----------------------------------------------------------------------
    // Bestow-cast spell shape — "enchant creature" (CR 702.103b)
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildBestowSpellDefinition_FiltersToCreatures()
    {
        var satyr = BoonSatyrFactory.Create(_alice);

        var bear = NewCreatureOnBattlefield("Bear");
        var land = new Land("Forest");
        var pacifism = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });

        var battlefield = new Permanent[] { bear, land, pacifism };
        var def = BoonSatyrFactory.BuildBestowSpellDefinition(satyr, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(bear);
        candidates.Should().NotContain(land);
        candidates.Should().NotContain(pacifism);
    }

    [Fact]
    public void BestowSpell_AttachesToChosenTarget_OnResolution()
    {
        var effects = new ContinuousEffectsService();
        var satyr = BoonSatyrFactory.Create(_alice, effects);
        var bear = NewCreatureOnBattlefield("Bear");

        var def = BoonSatyrFactory.BuildBestowSpellDefinition(
            satyr, new Permanent[] { bear });

        var chosen = new Majik.Core.Game.ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { bear } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        satyr.AttachedTo.Should().BeSameAs(bear, "CR 303.4f — the aura enters attached to its target");
    }

    [Fact]
    public void BestowCost_Is_3GG()
    {
        BoonSatyrFactory.ParseBestowCost().Should().Be(ManaCost.Parse("{3}{G}{G}"));
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

    private static void PlaceOnBattlefield(Creature card, Player owner)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}
