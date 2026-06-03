using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="OldGrowthTrollFactory"/>.
///
/// Card: Old-Growth Troll — Creature — Troll Warrior {G}{G}{G} 4/4 (Kaldheim).
///   "Trample"
///   "When Old-Growth Troll dies, if it was a creature, return it to the
///    battlefield. It's an Aura enchantment with enchant Forest you control
///    and 'Enchanted Forest has \"{T}: Add {G}{G}\" and \"{1}, {T},
///    Sacrifice this land: Create a tapped 4/4 green Troll Warrior creature
///    token with trample.\"'"
///
/// Covers the NEW engine primitive — return-to-battlefield-as-an-Aura on dies
/// (CR 614.12-ish bestow-on-death): the dies trigger returns the dead creature
/// to the battlefield as an Enchantment — Aura attached to a chosen Forest the
/// controller controls, and grants that Forest two activated abilities.
/// </summary>
public class OldGrowthTrollTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void OldGrowthTroll_Identity()
    {
        var c = OldGrowthTrollFactory.Create(_alice);

        c.Name.Should().Be("Old-Growth Troll");
        c.ManaCost.Should().Be("{G}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Troll).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Trample");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_OldGrowthTroll()
    {
        var card = NamedCardFactory.Create("Old-Growth Troll", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Old-Growth Troll");
    }

    // -----------------------------------------------------------------------
    // Dies trigger gating — Battlefield → Graveyard for THIS card only
    // -----------------------------------------------------------------------

    [Fact]
    public void DiesTrigger_GatesOnSelfFromBattlefieldToGraveyard()
    {
        var troll = OldGrowthTrollFactory.Create(_alice);
        PlaceOnBattlefield(troll, _alice);

        var trigger = troll.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new Majik.Core.Events.CardMovedEvent(
                troll, ZoneType.Battlefield, ZoneType.Graveyard))
            .Should().BeTrue("self battlefield → graveyard is 'dies'");
        trigger.IsTriggered(new Majik.Core.Events.CardMovedEvent(
                troll, ZoneType.Battlefield, ZoneType.Exile))
            .Should().BeFalse("battlefield → exile is not 'dies'");
    }

    // -----------------------------------------------------------------------
    // Return-as-Aura on death — the new primitive
    // -----------------------------------------------------------------------

    [Fact]
    public void DiesResolution_ReturnsAsAura_AttachedToControllersForest_GrantingAbilities()
    {
        var effects = new ContinuousEffectsService();
        var zones = new ZoneService();
        var troll = OldGrowthTrollFactory.Create(_alice, effects, zones, triggers: null);

        // A Forest the controller controls — the future host.
        var forest = NewForest(_alice);

        // Stage the post-death state: the creature lies in the owner's graveyard.
        _alice.Zones.Graveyard.AddCard(troll);
        troll.SetZone(ZoneType.Graveyard);

        var trigger = troll.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        // The returned permanent is on the battlefield as an Enchantment — Aura.
        var aura = _alice.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Single(p => p.Name == "Old-Growth Troll" && p.HasType(CardType.Enchantment));

        aura.HasType(CardType.Enchantment).Should().BeTrue();
        aura.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        aura.Zone.Should().Be(ZoneType.Battlefield);

        // It enters attached to the chosen Forest (CR 303.4f).
        aura.AttachedTo.Should().BeSameAs(forest);
        forest.Attachments.Should().Contain(aura);

        // The Forest is granted the two activated abilities.
        forest.Abilities.OfType<ManaAbility>().Should().NotBeEmpty(
            "the Forest gains '{T}: Add {G}{G}'");
        forest.Abilities.OfType<ActivatedAbility>()
            .Should().NotBeEmpty("the Forest gains the sacrifice-for-token ability");
    }

    [Fact]
    public void DiesResolution_NoForest_NoReturn()
    {
        var effects = new ContinuousEffectsService();
        var zones = new ZoneService();
        var troll = OldGrowthTrollFactory.Create(_alice, effects, zones, triggers: null);

        // No Forest controlled — the Aura has nothing to enchant, so the
        // return doesn't happen (CR 303.4 — an Aura with no legal object to
        // enchant can't enter the battlefield).
        _alice.Zones.Graveyard.AddCard(troll);
        troll.SetZone(ZoneType.Graveyard);

        var trigger = troll.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(troll);
    }

    [Fact]
    public void GrantedForestManaAbility_AddsTwoGreen()
    {
        var effects = new ContinuousEffectsService();
        var zones = new ZoneService();
        var troll = OldGrowthTrollFactory.Create(_alice, effects, zones, triggers: null);
        var forest = NewForest(_alice);

        _alice.Zones.Graveyard.AddCard(troll);
        troll.SetZone(ZoneType.Graveyard);
        var trigger = troll.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        var mana = forest.Abilities.OfType<ManaAbility>().First();
        var produced = mana.Activate();
        produced.Green.Should().Be(2, "{T}: Add {G}{G}");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Land NewForest(Player controller)
    {
        var forest = new Land("Forest", supertypes: null,
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(controller);
        forest.SetController(controller);
        controller.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);
        return forest;
    }

    private static void PlaceOnBattlefield(Creature card, Player owner)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}
