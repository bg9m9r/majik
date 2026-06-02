using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="GhituLavarunnerFactory"/>.
///
/// Card: Ghitu Lavarunner — Creature — Human Wizard {R}, 1/2 (Dominaria).
/// Oracle text (verified against Scryfall 2026-06):
///   "As long as there are two or more instant and/or sorcery cards in your
///    graveyard, this creature gets +1/+0 and has haste."
///
/// Covers:
///   - Identity ({R}, 1/2, Creature — Human Wizard).
///   - No triggered abilities (Lavarunner has none).
///   - Layer 7c conditional +1/+0 self-pump + Layer 6 conditional Haste grant,
///     both gated on "two or more instant and/or sorcery cards in your
///     graveyard" — read live each layer pass.
///   - CountInstantsAndSorceries pure helper / threshold predicate.
/// </summary>
[Trait("Color", "R")]
public class GhituLavarunnerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public GhituLavarunnerFactoryTests()
    {
        _zones = new ZoneService(_bus);
    }

    private static Card MakeInstant(string name, Player owner)
    {
        var c = new Instant(name, "{R}");
        c.SetOwner(owner);
        return c;
    }

    private static Card MakeSorcery(string name, Player owner)
    {
        var c = new Sorcery(name, "{R}");
        c.SetOwner(owner);
        return c;
    }

    private void PutInGraveyard(Card card)
    {
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    private Creature WireLavarunner(Player owner)
    {
        var card = GhituLavarunnerFactory.Create(owner, _effects, _bus);
        card.ActiveEffects = _effects;
        return card;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GhituLavarunner_Identity()
    {
        var c = GhituLavarunnerFactory.Create(_alice);

        c.Name.Should().Be("Ghitu Lavarunner");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GhituLavarunner_HasNoTriggeredAbilities()
    {
        var c = GhituLavarunnerFactory.Create(_alice);

        c.Abilities.OfType<Majik.Core.Abilities.TriggeredAbility>().Should().BeEmpty(
            "Ghitu Lavarunner has no triggered abilities — its whole text is a conditional static");
    }

    // -----------------------------------------------------------------------
    // CountInstantsAndSorceries pure helper
    // -----------------------------------------------------------------------

    [Fact]
    public void CountInstantsAndSorceries_CountsInstantsAndSorceriesOwnedByController()
    {
        var cards = new ICard[]
        {
            MakeInstant("Bolt", _alice),
            MakeSorcery("Divination", _alice),
            new Creature("Bear", "{1}{G}", 2, 2),  // not instant/sorcery — excluded
        };
        ((Card)cards[2]).SetOwner(_alice);

        GhituLavarunnerFactory.CountInstantsAndSorceries(cards, _alice).Should().Be(2);
    }

    [Fact]
    public void CountInstantsAndSorceries_ExcludesCardsOwnedByOtherPlayers()
    {
        var bob = new Player("Bob", 20);
        var cards = new ICard[]
        {
            MakeInstant("Mine", _alice),
            MakeInstant("Bobs", bob),   // "your graveyard" — excluded
        };

        GhituLavarunnerFactory.CountInstantsAndSorceries(cards, _alice).Should().Be(1,
            "only instant/sorcery cards the controller OWNS count (CR 109.5 'you')");
    }

    // -----------------------------------------------------------------------
    // Conditional static — below threshold (0 or 1 cards): no bonus, no haste
    // -----------------------------------------------------------------------

    [Fact]
    public void GhituLavarunner_NoBonus_WhenGraveyardEmpty()
    {
        var c = WireLavarunner(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        c.Power.Should().Be(1, "no instant/sorcery cards in graveyard — base 1/2");
        c.Toughness.Should().Be(2);
        CombatAbilities.HasHaste(c).Should().BeFalse("threshold not met — no haste");
    }

    [Fact]
    public void GhituLavarunner_NoBonus_WhenOnlyOneInstantOrSorcery()
    {
        var c = WireLavarunner(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        PutInGraveyard(MakeInstant("GY-Bolt", _alice));
        c.ActiveEffects!.Clear();

        c.Power.Should().Be(1, "only one instant/sorcery — threshold of two not met");
        c.Toughness.Should().Be(2);
        CombatAbilities.HasHaste(c).Should().BeFalse("threshold of two not met — no haste");
    }

    // -----------------------------------------------------------------------
    // Conditional static — at/above threshold (2+ cards): +1/+0 and haste
    // -----------------------------------------------------------------------

    [Fact]
    public void GhituLavarunner_GetsBonusAndHaste_WhenTwoInstantsOrSorceries()
    {
        var c = WireLavarunner(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        PutInGraveyard(MakeInstant("GY-Bolt", _alice));
        PutInGraveyard(MakeSorcery("GY-Divination", _alice));
        c.ActiveEffects!.Clear();

        c.Power.Should().Be(2, "+1/+0 while two or more instant/sorcery cards in graveyard");
        c.Toughness.Should().Be(2, "toughness is unchanged (+1/+0)");
        CombatAbilities.HasHaste(c).Should().BeTrue(
            "CR 702.10 — Lavarunner has haste while threshold met");
    }

    [Fact]
    public void GhituLavarunner_OnlyGraveyardCounts_NotExileOrCreatures()
    {
        var c = WireLavarunner(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Two qualifying cards in graveyard.
        PutInGraveyard(MakeInstant("GY-Bolt", _alice));
        PutInGraveyard(MakeSorcery("GY-Divination", _alice));

        // An instant in EXILE must NOT count toward the graveyard threshold.
        var exiled = MakeInstant("Exile-Bolt", _alice);
        _alice.Zones.Exile.AddCard(exiled);
        exiled.SetZone(ZoneType.Exile);

        // A creature in the graveyard must NOT count.
        var creatureInGy = new Creature("Dead Bear", "{1}{G}", 2, 2);
        creatureInGy.SetOwner(_alice);
        PutInGraveyard(creatureInGy);

        c.ActiveEffects!.Clear();

        c.Power.Should().Be(2, "exactly two graveyard instants/sorceries — threshold met");
        CombatAbilities.HasHaste(c).Should().BeTrue();
    }

    [Fact]
    public void GhituLavarunner_BonusLiftsWhenThresholdDropsBelowTwo()
    {
        var c = WireLavarunner(_alice);
        _zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);

        var bolt = MakeInstant("GY-Bolt", _alice);
        var div = MakeSorcery("GY-Divination", _alice);
        PutInGraveyard(bolt);
        PutInGraveyard(div);
        c.ActiveEffects!.Clear();

        c.Power.Should().Be(2);
        CombatAbilities.HasHaste(c).Should().BeTrue();

        // Remove one card → drop below the threshold of two.
        _alice.Zones.Graveyard.RemoveCard(div);
        c.ActiveEffects!.Clear();

        c.Power.Should().Be(1, "dropped to one instant/sorcery — bonus lifts (CR 613.7c)");
        CombatAbilities.HasHaste(c).Should().BeFalse("threshold no longer met — haste lifts");
    }
}
