using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EnduringVitalityFactory"/>.
///
/// Enduring Vitality (Duskmourn: House of Horror, {1}{G}{G}). Enchantment
/// Creature — Elk Glimmer 3/3. Oracle text (verified against Scryfall):
///   "Vigilance
///    Creatures you control have "{T}: Add one mana of any color."
///    When Enduring Vitality dies, if it was a creature, return it to the
///    battlefield under its owner's control. It's an enchantment. (It's not a
///    creature.)"
///
/// Covers:
/// - Identity ({1}{G}{G} Enchantment Creature — Elk Glimmer, 3/3, mono-G).
/// - Vigilance keyword marker (CR 702.21).
/// - Group mana-grant: creatures the controller controls (including Enduring
///   Vitality itself) gain "{T}: Add one mana of any color" (CR 613.1f),
///   not the opponent's creatures, and the grant is revoked when Enduring
///   Vitality leaves play (CR 611.2c).
/// - Dies → return-to-battlefield + Layer-4 type-strip (CR 603.6c / 701.20 /
///   613.1d): after the return the card is an enchantment but no longer a
///   creature; a subsequent death does not re-return it.
/// </summary>
[Trait("Color", "G")]
public class EnduringVitalityFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;

    public EnduringVitalityFactoryTests()
    {
        _effects = new ContinuousEffectsService(_bus);
        _zones = new ZoneService(_bus);
    }

    private System.Collections.Generic.IEnumerable<Player> AllPlayers() => new[] { _alice, _bob };

    /// <summary>Move a freshly-built card to the battlefield via the real zone flow.</summary>
    private void PutOnBattlefield(ICard card, Player owner)
    {
        owner.Zones.Library.AddCard(card);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);
    }

    private static bool ProducesColor(IManaAbility a, char wubrg)
    {
        var m = a.ManaGenerated;
        return wubrg switch
        {
            'W' => m.White == 1,
            'U' => m.Blue == 1,
            'B' => m.Black == 1,
            'R' => m.Red == 1,
            'G' => m.Green == 1,
            _ => false,
        };
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void EnduringVitality_Identity()
    {
        var c = EnduringVitalityFactory.Create(_alice);

        c.Name.Should().Be("Enduring Vitality");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elk).Should().BeTrue();
        c.HasSubtype(CardSubtype.Glimmer).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.ManaCost.Should().Be("{1}{G}{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EnduringVitality_IsMonoGreen()
    {
        var c = EnduringVitalityFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green);
        colors.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Vigilance
    // -----------------------------------------------------------------------

    [Fact]
    public void EnduringVitality_HasVigilance()
    {
        var c = EnduringVitalityFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Vigilance", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("CR 702.21 — Vigilance");
    }

    // -----------------------------------------------------------------------
    // "Creatures you control have '{T}: Add one mana of any color.'"
    // -----------------------------------------------------------------------

    private Creature AliceBear()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.ChangeOwner(_alice);
        bear.ChangeController(_alice);
        return bear;
    }

    [Fact]
    public void GroupGrant_CreatureYouControl_TapsForAnyColor()
    {
        var bear = AliceBear();
        PutOnBattlefield(bear, _alice);

        var vitality = EnduringVitalityFactory.Create(
            _alice, _effects, _bus, zoneService: null, allPlayersProvider: AllPlayers);
        PutOnBattlefield(vitality, _alice);

        var abilities = bear.Abilities.OfType<IManaAbility>().ToList();
        abilities.Should().HaveCount(5, "CR 605.1a — 'any color' = five single-colour mana abilities");
        foreach (var color in "WUBRG")
            abilities.Should().Contain(a => ProducesColor(a, color),
                $"the bear should tap for {color} (CR 613.1f)");
    }

    [Fact]
    public void GroupGrant_AppliesToSelf()
    {
        var vitality = EnduringVitalityFactory.Create(
            _alice, _effects, _bus, zoneService: null, allPlayersProvider: AllPlayers);
        PutOnBattlefield(vitality, _alice);

        vitality.Abilities.OfType<IManaAbility>().Should().HaveCount(5,
            "Enduring Vitality is itself a creature you control, so it gains the granted any-colour mana ability");
    }

    [Fact]
    public void GroupGrant_DoesNotApplyToOpponentsCreatures()
    {
        var bobBear = new Creature("Bob's Bear", "{1}{R}", 2, 2);
        bobBear.ChangeOwner(_bob);
        bobBear.ChangeController(_bob);
        PutOnBattlefield(bobBear, _bob);

        var vitality = EnduringVitalityFactory.Create(
            _alice, _effects, _bus, zoneService: null, allPlayersProvider: AllPlayers);
        PutOnBattlefield(vitality, _alice);
        _effects.Compute((Permanent)bobBear);

        bobBear.Abilities.OfType<IManaAbility>().Should().BeEmpty(
            "the grant scope is 'creatures YOU control' (CR 109.5)");
    }

    [Fact]
    public void GroupGrant_RevokedWhenVitalityLeavesPlay()
    {
        var bear = AliceBear();
        PutOnBattlefield(bear, _alice);

        var vitality = EnduringVitalityFactory.Create(
            _alice, _effects, _bus, zoneService: null, allPlayersProvider: AllPlayers);
        PutOnBattlefield(vitality, _alice);
        bear.Abilities.OfType<IManaAbility>().Should().HaveCount(5);

        // Enduring Vitality leaves the battlefield — the grant ends (CR 611.2c).
        _zones.MoveCard(vitality, ZoneType.Battlefield, ZoneType.Exile, _alice);

        bear.Abilities.OfType<IManaAbility>().Should().BeEmpty(
            "once the source leaves play the granted ability is lost (CR 613.6e)");
    }

    // -----------------------------------------------------------------------
    // Dies → return as a (non-creature) enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void DiesTrigger_ReturnsToBattlefield_UnderOwnersControl()
    {
        var vitality = EnduringVitalityFactory.Create(
            _alice, _effects, _bus, zoneService: null, allPlayersProvider: AllPlayers);

        vitality.SetOwner(_alice);
        vitality.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(vitality);
        vitality.SetZone(ZoneType.Graveyard);

        var trig = vitality.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);
        foreach (var effect in trig.Effects) effect.Execute();

        vitality.Zone.Should().Be(ZoneType.Battlefield, "it returns to the battlefield (CR 701.20)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(vitality);
        vitality.Controller.Should().BeSameAs(_alice, "under its owner's control");
    }

    [Fact]
    public void AfterReturn_ItsAnEnchantmentNotACreature()
    {
        var vitality = EnduringVitalityFactory.Create(
            _alice, _effects, _bus, zoneService: null, allPlayersProvider: AllPlayers);

        vitality.SetOwner(_alice);
        vitality.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(vitality);
        vitality.SetZone(ZoneType.Graveyard);

        var diesTrigger = vitality.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);
        foreach (var effect in diesTrigger.Effects) effect.Execute();

        // CR 613.1d — after the return its layered characteristics lose the
        // Creature type but keep the printed Enchantment type.
        var chars = _effects.Compute((Permanent)vitality);
        chars.Types.Should().NotContain(CardType.Creature,
            "after returning, it's an enchantment, not a creature (CR 613.1d)");
        chars.Types.Should().Contain(CardType.Enchantment,
            "the printed Enchantment type is preserved (the strip is creature-only)");
    }

    [Fact]
    public void DiesTrigger_OnlyReturnsOnce_SecondDeathDoesNotReturn()
    {
        var vitality = EnduringVitalityFactory.Create(
            _alice, _effects, _bus, zoneService: null, allPlayersProvider: AllPlayers);

        vitality.SetOwner(_alice);
        vitality.SetController(_alice);

        var diesTrigger = vitality.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

        // First death → return.
        _alice.Zones.Graveyard.AddCard(vitality);
        vitality.SetZone(ZoneType.Graveyard);
        foreach (var effect in diesTrigger.Effects) effect.Execute();
        vitality.Zone.Should().Be(ZoneType.Battlefield);

        // Second death (now a non-creature enchantment) → intervening-if
        // "if it was a creature" fails; it stays in the graveyard.
        _alice.Zones.Battlefield.RemoveCard(vitality);
        _alice.Zones.Graveyard.AddCard(vitality);
        vitality.SetZone(ZoneType.Graveyard);
        foreach (var effect in diesTrigger.Effects) effect.Execute();

        vitality.Zone.Should().Be(ZoneType.Graveyard,
            "once it has returned as a non-creature enchantment, dying again does not re-return it");
    }
}
