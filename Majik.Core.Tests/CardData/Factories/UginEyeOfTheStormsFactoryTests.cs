using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Ugin, Eye of the Storms (Tarkir: Dragonstorm, {7}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Ugin, starting loyalty 7,
///     mana cost {7}), materialised from the embedded JSON definition.
///   - Two cast triggers (cast-this-spell over Stack; cast-a-colorless-spell
///     over Battlefield) — condition matching + the shared coloured-permanent
///     exile body ("up to one target permanent that's one or more colors").
///   - +2: gain 3 life and draw a card.
///   - 0: add {C}{C}{C} to the controller's mana pool.
///   - −11: exile all colorless nonland cards from library + grant free
///     exile-cast.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "C")]
public class UginEyeOfTheStormsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Ugin_IsLegendaryPlaneswalker_Ugin_7Loyalty_AtCost7()
    {
        var ugin = UginEyeOfTheStormsFactory.Create(_alice);

        ugin.Name.Should().Be("Ugin, Eye of the Storms");
        ugin.ManaCost.Should().Be("{7}");
        ugin.HasType(CardType.Planeswalker).Should().BeTrue();
        ugin.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        ugin.HasSubtype(CardSubtype.Ugin).Should().BeTrue();
        ugin.Loyalty.Should().Be(7);
        ugin.StartingLoyalty.Should().Be(7);
        ugin.Owner.Should().BeSameAs(_alice);
        ugin.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Ugin_HasTwoCastTriggers_AndThreeLoyaltyAbilities()
    {
        var ugin = UginEyeOfTheStormsFactory.Create(_alice);

        ugin.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);

        var loyalty = ugin.Abilities.OfType<LoyaltyAbility>().ToList();
        loyalty.Should().HaveCount(3);
        loyalty.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +2, 0, -11 });
    }

    [Fact]
    public void CastThisSpellTrigger_MatchesOnlyThisCardsCast_OverStack()
    {
        var ugin = UginEyeOfTheStormsFactory.Create(_alice);

        var castTrigger = ugin.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Stack));
        var cond = (EventTriggerCondition<SpellCastEvent>)castTrigger.Condition;

        var selfSpell = new Majik.Core.Spells.Spell(ugin, _alice);
        var other = new Creature("Bear", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        var otherSpell = new Majik.Core.Spells.Spell(other, _alice);

        cond.Matches(new SpellCastEvent(selfSpell), castTrigger).Should().BeTrue();
        cond.Matches(new SpellCastEvent(otherSpell), castTrigger).Should().BeFalse();
    }

    [Fact]
    public void ColorlessCastTrigger_MatchesColorlessSpellByController_OverBattlefield()
    {
        var ugin = UginEyeOfTheStormsFactory.Create(_alice);

        var colorlessTrigger = ugin.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Battlefield));
        var cond = (EventTriggerCondition<SpellCastEvent>)colorlessTrigger.Condition;

        // Colorless artifact cast by Alice → matches.
        var colorlessArtifact = new Artifact("Worker", "{2}");
        colorlessArtifact.SetOwner(_alice);
        cond.Matches(new SpellCastEvent(new Majik.Core.Spells.Spell(colorlessArtifact, _alice)), colorlessTrigger)
            .Should().BeTrue();

        // Coloured spell cast by Alice → does NOT match.
        var redBolt = new Instant("Bolt", "{R}");
        redBolt.SetOwner(_alice);
        cond.Matches(new SpellCastEvent(new Majik.Core.Spells.Spell(redBolt, _alice)), colorlessTrigger)
            .Should().BeFalse();

        // Colorless spell cast by Bob (not Ugin's controller) → no match.
        var bobColorless = new Artifact("Bot", "{1}");
        bobColorless.SetOwner(_bob);
        cond.Matches(new SpellCastEvent(new Majik.Core.Spells.Spell(bobColorless, _bob)), colorlessTrigger)
            .Should().BeFalse();
    }

    [Fact]
    public void CastTrigger_Exile_ExilesOneColouredPermanent_LeavesColourless()
    {
        var redGoblin = new Creature("Goblin", "{R}", 1, 1);
        redGoblin.SetOwner(_bob); redGoblin.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(redGoblin);
        redGoblin.SetZone(ZoneType.Battlefield);

        var colourlessGolem = new Creature("Golem", "{2}", 2, 2);
        colourlessGolem.SetOwner(_bob); colourlessGolem.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(colourlessGolem);
        colourlessGolem.SetZone(ZoneType.Battlefield);

        var ugin = UginEyeOfTheStormsFactory.Create(
            _alice,
            // Resolver offers the colourless first, then the red — the
            // effect must skip the colourless and exile the red (and only
            // one).
            colouredPermanentResolver: () => new[] { colourlessGolem, redGoblin },
            eventBus: null,
            random: null);
        _alice.Zones.Battlefield.AddCard(ugin);
        ugin.SetZone(ZoneType.Battlefield);

        var trigger = ugin.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Stack));
        foreach (var e in trigger.Effects) e.Execute();

        _bob.Zones.Exile.GetCards().Should().Contain(redGoblin);
        _bob.Zones.Exile.GetCards().Should().NotContain(colourlessGolem,
            "a colourless permanent is not 'one or more colors'");
        _bob.Zones.Battlefield.GetCards().Should().Contain(colourlessGolem);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(redGoblin);
    }

    [Fact]
    public void CastTrigger_NoResolver_NoOp()
    {
        var redGoblin = new Creature("Goblin", "{R}", 1, 1);
        redGoblin.SetOwner(_bob); redGoblin.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(redGoblin);
        redGoblin.SetZone(ZoneType.Battlefield);

        var ugin = UginEyeOfTheStormsFactory.Create(_alice);

        var trigger = ugin.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Stack));
        foreach (var e in trigger.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().Contain(redGoblin,
            "no resolver → 'up to one' resolves to zero (silent no-op)");
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Plus2_GainsThreeLifeAndDrawsOne()
    {
        for (var i = 0; i < 3; i++)
        {
            var c = new Card($"Lib{i}", "{1}") { Owner = _alice };
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var ugin = UginEyeOfTheStormsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ugin);
        ugin.SetZone(ZoneType.Battlefield);

        var plus2 = ugin.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +2);
        plus2.Activate();

        ugin.Loyalty.Should().Be(9, "7 + 2 = 9");
        _alice.LifeTotal.Should().Be(23, "20 + 3 = 23");
        _alice.Zones.Hand.GetCards().Should().HaveCount(1, "drew one card");
        _alice.Zones.Library.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void Zero_AddsThreeColorlessMana()
    {
        var ugin = UginEyeOfTheStormsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ugin);
        ugin.SetZone(ZoneType.Battlefield);

        var zero = ugin.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == 0);
        zero.Activate();

        ugin.Loyalty.Should().Be(7, "0: ability leaves loyalty unchanged");
        // {C} maps to the generic bucket today (ManaCost has no dedicated
        // colourless bucket — see ManaCost.Parse note at the {C} branch).
        _alice.ManaPool.Generic.Should().Be(3, "Add {C}{C}{C}");
    }

    [Fact]
    public void Ultimate_ExilesColorlessNonlandCards_AndGrantsFreeCast()
    {
        var colorlessNonland1 = new Card("Eldrazi", "{10}", new[] { CardType.Creature }) { Owner = _alice };
        _alice.Zones.Library.AddCard(colorlessNonland1);
        colorlessNonland1.SetZone(ZoneType.Library);

        var colorlessNonland2 = new Card("Construct", "{4}", new[] { CardType.Artifact }) { Owner = _alice };
        _alice.Zones.Library.AddCard(colorlessNonland2);
        colorlessNonland2.SetZone(ZoneType.Library);

        // Coloured nonland — excluded (not colorless).
        var redSpell = new Card("Bolt", "{R}", new[] { CardType.Instant }) { Owner = _alice };
        _alice.Zones.Library.AddCard(redSpell);
        redSpell.SetZone(ZoneType.Library);

        // Colorless LAND — excluded (nonland filter).
        var wastes = new Land("Wastes");
        wastes.SetOwner(_alice);
        _alice.Zones.Library.AddCard(wastes);
        wastes.SetZone(ZoneType.Library);

        var ugin = UginEyeOfTheStormsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ugin);
        ugin.SetZone(ZoneType.Battlefield);
        ugin.AddLoyalty(4); // 7 + 4 = 11 (enough for −11)

        var ult = ugin.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -11);
        ult.CanActivate().Should().BeTrue();
        ult.Activate();

        ugin.Loyalty.Should().Be(0, "11 - 11 = 0");

        _alice.Zones.Exile.GetCards().Should().Contain(colorlessNonland1);
        _alice.Zones.Exile.GetCards().Should().Contain(colorlessNonland2);
        _alice.Zones.Exile.GetCards().Should().NotContain(redSpell, "coloured card stays in library");
        _alice.Zones.Exile.GetCards().Should().NotContain(wastes, "land stays in library");

        _alice.Zones.Library.GetCards().Should().Contain(redSpell);
        _alice.Zones.Library.GetCards().Should().Contain(wastes);

        // CR 118.9 — free-cast grant stamped at {0} for the controller.
        colorlessNonland1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        colorlessNonland1.RuntimeExileCastCost!.IsZero.Should().BeTrue();
        colorlessNonland2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        colorlessNonland2.RuntimeExileCastCost!.IsZero.Should().BeTrue();
    }
}
