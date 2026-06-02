using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="RancorFactory"/>.
///
/// Card: Rancor — Enchantment — Aura {G} (Urza's Legacy).
///   "Enchant creature"
///   "Enchanted creature gets +2/+0 and has trample."
///   "When this Aura is put into a graveyard from the battlefield, return it
///    to its owner's hand."
///
/// Covers:
///   - Identity / dispatch (Enchantment — Aura, {G}).
///   - Static +2/+0 boost (CR 613 Layer 7c) + granted Trample (CR 702.19).
///   - Boost is inert while unattached.
///   - "Enchant creature" cast-time target predicate filters non-creatures.
///   - Dies trigger gates on Battlefield → Graveyard for this card only.
///   - Dies resolution returns the Aura to its OWNER's hand (CR 400.7).
/// </summary>
public class RancorTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Rancor_Identity()
    {
        var c = RancorFactory.Create(_alice);

        c.Name.Should().Be("Rancor");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Rancor()
    {
        var card = NamedCardFactory.Create("Rancor", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Rancor");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Static +2/+0 boost + Trample grant
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_Boost_PumpsPlus2Plus0_AndGrantsTrample()
    {
        var effects = new ContinuousEffectsService();
        var rancor = RancorFactory.Create(_alice, effects, zones: null, triggers: null);
        PlaceOnBattlefield(rancor, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        rancor.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2 + 2, "+2/+0 from Rancor");
        chars.Toughness.Should().Be(2 + 0, "toughness is unchanged");
        chars.Keywords.Should().Contain("Trample");
    }

    [Fact]
    public void Static_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var rancor = RancorFactory.Create(_alice, effects, zones: null, triggers: null);
        PlaceOnBattlefield(rancor, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        // Don't attach.

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().NotContain("Trample");
    }

    // -----------------------------------------------------------------------
    // "Enchant creature" target predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_FiltersToCreatures()
    {
        var rancor = RancorFactory.Create(_alice);

        var bear = NewCreatureOnBattlefield("Bear");
        var land = new Land("Forest");
        var pacifism = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });

        var battlefield = new Permanent[] { bear, land, pacifism };
        var def = RancorFactory.BuildSpellDefinition(rancor, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(bear);
        candidates.Should().NotContain(land);
        candidates.Should().NotContain(pacifism);
    }

    // -----------------------------------------------------------------------
    // Dies trigger — Battlefield → Graveyard for THIS card only
    // -----------------------------------------------------------------------

    [Fact]
    public void Rancor_DiesTrigger_GatesOnSelfFromBattlefieldToGraveyard()
    {
        var rancor = RancorFactory.Create(_alice);
        var other = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura }) { Owner = _alice };

        // Active-zones guard: Battlefield + Graveyard. Place on battlefield so
        // the predicate's zone-guard passes (Mosswood / Wurmcoil posture).
        PlaceOnBattlefield(rancor, _alice);

        var trigger = rancor.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new CardMovedEvent(rancor, ZoneType.Battlefield, ZoneType.Graveyard))
            .Should().BeTrue("self battlefield → graveyard is the 'put into a graveyard from the battlefield' event");
        trigger.IsTriggered(new CardMovedEvent(other, ZoneType.Battlefield, ZoneType.Graveyard))
            .Should().BeFalse("another aura dying does not fire this trigger");
        trigger.IsTriggered(new CardMovedEvent(rancor, ZoneType.Battlefield, ZoneType.Exile))
            .Should().BeFalse("battlefield → exile is not 'put into a graveyard'");
        trigger.IsTriggered(new CardMovedEvent(rancor, ZoneType.Hand, ZoneType.Graveyard))
            .Should().BeFalse("hand → graveyard (discard) is not 'from the battlefield'");
    }

    [Fact]
    public void Rancor_DiesResolution_ReturnsToOwnersHand()
    {
        var rancor = RancorFactory.Create(_alice);

        // Stage the post-death state: card lives in owner's graveyard.
        _alice.Zones.Graveyard.AddCard(rancor);
        rancor.SetZone(ZoneType.Graveyard);

        var trigger = rancor.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(rancor,
            "dies trigger routes the Aura back to its owner's hand");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(rancor,
            "Aura no longer sits in the graveyard");
        rancor.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Rancor_DiesResolution_ReturnsToTRUE_OwnersHand_NotControllerHand()
    {
        // CR 400.7 — "owner". If Bob gains control of the enchanted creature
        // (and thus Rancor) and it then dies, Rancor returns to Alice's hand
        // (the true owner), not Bob's.
        var rancor = RancorFactory.Create(_alice);

        rancor.SetController(_bob);
        _alice.Zones.Graveyard.AddCard(rancor);
        rancor.SetZone(ZoneType.Graveyard);

        var trigger = rancor.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(rancor,
            "return-to-OWNER's-hand routes to Alice even though Bob controlled it");
        _bob.Zones.Hand.GetCards().Should().NotContain(rancor);
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

    private static void PlaceOnBattlefield(Enchantment aura, Player owner)
    {
        owner.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
    }
}
