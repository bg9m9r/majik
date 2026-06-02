using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ShelteredByGhostsFactory"/>.
///
/// Card: Sheltered by Ghosts — Enchantment — Aura {1}{W} (Duskmourn).
///   "Enchant creature you control"
///   "When this Aura enters, exile target nonland permanent an opponent
///    controls until this Aura leaves the battlefield."
///   "Enchanted creature gets +1/+0 and has lifelink and ward {2}."
///
/// Covers:
///   - Identity / dispatch (Enchantment — Aura, {1}{W}, white).
///   - ETB exile trigger present (Banishing Light shape) — exiles an
///     opponent's nonland permanent; rejects lands + controller-side.
///   - LTB returns the exiled card under its owner's control.
///   - Static +1/+0 boost (CR 613 Layer 7c) + granted Lifelink + Ward {2}.
///   - Boost is inert while unattached.
///   - "Enchant creature you control" cast-time predicate filters to the
///     aura controller's creatures only.
/// </summary>
[Trait("Color", "W")]
public class ShelteredByGhostsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ShelteredByGhosts_Identity()
    {
        var c = ShelteredByGhostsFactory.Create(_alice);

        c.Name.Should().Be("Sheltered by Ghosts");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ShelteredByGhosts()
    {
        var card = NamedCardFactory.Create("Sheltered by Ghosts", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Sheltered by Ghosts");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB exile trigger — Banishing Light shape (CR 701.21)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_ExilesOpponentNonlandPermanent()
    {
        var aura = ShelteredByGhostsFactory.Create(_alice);
        PlaceOnBattlefield(aura, _alice);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = aura.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles the targeted nonland permanent (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void Etb_RejectsLandTarget()
    {
        var aura = ShelteredByGhostsFactory.Create(_alice);
        PlaceOnBattlefield(aura, _alice);

        var bobsLand = new Land("Forest");
        bobsLand.SetOwner(_bob);
        bobsLand.SetController(_bob);
        bobsLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsLand);

        var etb = aura.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsLand },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobsLand.Zone.Should().Be(ZoneType.Battlefield,
            "lands are skipped by the printed 'nonland' filter (CR 608.2b)");
    }

    [Fact]
    public void Etb_RejectsControllerOwnPermanent()
    {
        var aura = ShelteredByGhostsFactory.Create(_alice);
        PlaceOnBattlefield(aura, _alice);

        var aliceCreature = new Creature("Bird", "{1}{W}", 1, 2);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var etb = aura.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { aliceCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            "ETB ignores controller-side permanents (oracle: 'an opponent controls')");
    }

    [Fact]
    public void Ltb_ReturnsExiledCardUnderOwnersControl()
    {
        var aura = ShelteredByGhostsFactory.Create(_alice);
        PlaceOnBattlefield(aura, _alice);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = aura.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();
        bobsCreature.Zone.Should().Be(ZoneType.Exile);

        var ltb = aura.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Battlefield,
            "LTB returns the exiled card to the battlefield");
        bobsCreature.Controller.Should().BeSameAs(_bob,
            "returned card is under its owner's control (CR 110.2)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Exile.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void Ltb_NoOpWhenNothingExiled()
    {
        var aura = ShelteredByGhostsFactory.Create(_alice);
        PlaceOnBattlefield(aura, _alice);

        var ltb = aura.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Static +1/+0, lifelink, ward {2}
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_Boost_PumpsPlus1Plus0_GrantsLifelinkAndWard2()
    {
        var effects = new ContinuousEffectsService();
        var aura = ShelteredByGhostsFactory.Create(
            _alice, effects, eventBus: null, triggers: null);
        PlaceOnBattlefield(aura, _alice);

        var bear = new Creature("Bear", "{1}{W}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _alice.Zones.Battlefield.AddCard(bear);
        aura.AttachTo(bear);

        // The Ward grant is a GrantAbilityEffect: the first Compute pass
        // attaches the parameterised KeywordAbility marker onto the bearer
        // (Sync), and the keyword set stabilises on the FOLLOWING Compute —
        // the same priming posture as LavaspurBootsTests. Lifelink (carried
        // on the AttachedBoostEffect) and the +1/+0 pump are live immediately.
        effects.Compute(bear);
        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2 + 1, "+1/+0 from Sheltered by Ghosts");
        chars.Toughness.Should().Be(2 + 0, "toughness is unchanged");
        chars.Keywords.Should().Contain("Lifelink");
        chars.Keywords.Should().Contain("Ward");

        // Ward carries the printed {2} amount (CR 702.21).
        bear.Abilities.OfType<KeywordAbility>()
            .Single(k => k.Keyword == "Ward")
            .Arg.Should().Be(ShelteredByGhostsFactory.WardAmount);
    }

    [Fact]
    public void Static_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var aura = ShelteredByGhostsFactory.Create(
            _alice, effects, eventBus: null, triggers: null);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        // Don't attach.

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().NotContain("Lifelink");
        chars.Keywords.Should().NotContain("Ward");
    }

    // -----------------------------------------------------------------------
    // "Enchant creature you control" target predicate (CR 702.5b)
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_FiltersToControllerSideCreaturesOnly()
    {
        var aura = ShelteredByGhostsFactory.Create(_alice);

        var aliceBear = NewCreatureOnBattlefield("Bear");

        var bobsBear = new Creature("Goyf", "{1}{G}", 2, 2);
        bobsBear.SetOwner(_bob);
        bobsBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobsBear);
        bobsBear.SetZone(ZoneType.Battlefield);

        var land = new Land("Forest");
        land.SetOwner(_alice);
        land.SetController(_alice);

        var battlefield = new Permanent[] { aliceBear, bobsBear, land };
        var def = ShelteredByGhostsFactory.BuildSpellDefinition(aura, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(aliceBear, "creature the aura controller controls");
        candidates.Should().NotContain(bobsBear, "'you control' excludes opponent creatures");
        candidates.Should().NotContain(land, "not a creature");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Creature NewCreatureOnBattlefield(string name)
    {
        var c = new Creature(name, "{1}{W}", 2, 2);
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
