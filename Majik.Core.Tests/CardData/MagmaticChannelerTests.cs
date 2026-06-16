using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="MagmaticChannelerFactory"/> (Modern Horizons 3, {1}{R}).
///
/// Oracle text:
///   "As long as there are four or more instant and/or sorcery cards in your
///    graveyard, this creature gets +3/+1.
///    {T}, Discard a card: Exile the top two cards of your library, then
///    choose one of them. You may play that card this turn."
///
/// Covers:
///   - Identity (Human Wizard 1/3, {1}{R}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatcher entry.
///   - Activated ability shape: Tap + DiscardACardCost, no mana cost.
///   - <see cref="MagmaticChannelerFactory.IsPumpActive"/> boundary at
///     0 / 3 / 4 / 5 instant+sorcery cards.
///   - Dynamic +3/+1 static turns on/off with the graveyard count.
///   - Resolve: exile top 2, choose one, stamp a play-this-turn grant on the
///     chosen card; the other exiled card carries no grant.
///   - Resolve: short / empty library posture.
/// </summary>
public class MagmaticChannelerTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void MagmaticChanneler_Identity_HumanWizard_1_3_At_1R()
    {
        var card = MagmaticChannelerFactory.Create(_alice);

        card.Name.Should().Be("Magmatic Channeler");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MagmaticChanneler()
    {
        var card = NamedCardFactory.Create("Magmatic Channeler", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Magmatic Channeler");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(3);

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void MagmaticChanneler_HasOneActivatedAbility_With_Tap_And_DiscardCosts_NoMana()
    {
        var card = MagmaticChannelerFactory.Create(_alice);
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "the {T}, Discard a card ability has no mana component");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the {T} symbol is the tap cost");
        ability.Costs.OfType<DiscardACardCost>()
            .Should().ContainSingle("\"Discard a card\" is the second activation cost");
        ability.RebindSafe.Should().BeTrue(
            "the dig reads ResolutionContext.Source and is re-source-safe for Agatha");
    }

    // ── Dynamic +3/+1 static ────────────────────────────────────────────────

    [Fact]
    public void IsPumpActive_FalseAt0()
        => MagmaticChannelerFactory.IsPumpActive(_alice).Should().BeFalse();

    [Fact]
    public void IsPumpActive_FalseAt3()
    {
        SeedGraveyardWithSpells(_alice, instants: 2, sorceries: 1);
        MagmaticChannelerFactory.IsPumpActive(_alice).Should().BeFalse();
    }

    [Fact]
    public void IsPumpActive_TrueAtThreshold4()
    {
        SeedGraveyardWithSpells(_alice, instants: 2, sorceries: 2);
        MagmaticChannelerFactory.IsPumpActive(_alice).Should().BeTrue();
    }

    [Fact]
    public void IsPumpActive_TrueAt5_Over_4()
    {
        SeedGraveyardWithSpells(_alice, instants: 3, sorceries: 2);
        MagmaticChannelerFactory.IsPumpActive(_alice).Should().BeTrue();
    }

    [Fact]
    public void IsPumpActive_IgnoresNonInstantSorceryCards()
    {
        for (var i = 0; i < 5; i++)
        {
            AddToGraveyard(_alice, new Creature($"Bear{i}", "{1}{G}", 2, 2));
        }
        MagmaticChannelerFactory.IsPumpActive(_alice).Should().BeFalse(
            "creatures don't count toward the +3/+1 threshold");
    }

    [Fact]
    public void DynamicStatic_BelowThreshold_PrintedStats()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);
        var card = MagmaticChannelerFactory.Create(_alice, bus, effects);
        SeatOnBattlefield(card, bus);

        SeedGraveyardWithSpells(_alice, instants: 3, sorceries: 0);
        MagmaticChannelerFactory.IsPumpActive(_alice).Should().BeFalse();
        card.Power.Should().Be(1, "below 4 I/S the static is inactive");
        card.Toughness.Should().Be(3);
    }

    [Fact]
    public void DynamicStatic_AtThreshold_GivesPlus3Plus1()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);
        var card = MagmaticChannelerFactory.Create(_alice, bus, effects);
        SeatOnBattlefield(card, bus);

        SeedGraveyardWithSpells(_alice, instants: 2, sorceries: 2);
        MagmaticChannelerFactory.IsPumpActive(_alice).Should().BeTrue();
        card.Power.Should().Be(4, "4+ I/S grants +3/+1");
        card.Toughness.Should().Be(4);
    }

    // ── {T}, Discard a card: exile top 2, may play one ──────────────────────

    [Fact]
    public void Resolve_ExilesTopTwo_StampsPlayGrantOnChosen()
    {
        var bus = new EventBus();
        var card = MagmaticChannelerFactory.Create(_alice, bus, effects: null);
        SeatOnBattlefield(card, bus);

        var first = new Instant("Bolt", "{R}") { Owner = _alice };
        var second = new Sorcery("Recall", "{U}") { Owner = _alice };
        var deep = new Creature("Deep", "{1}{G}", 2, 2) { Owner = _alice };
        AddToLibrary(_alice, first, second, deep);

        ExecuteActivation(card);

        // Top two are exiled; the deep card stays in the library.
        _alice.Zones.Exile.GetCards().Should().Contain(new ICard[] { first, second });
        _alice.Zones.Library.GetCards().Should().ContainSingle().Which.Should().BeSameAs(deep);

        // Deterministic fallback picks the first exiled card for the
        // play-this-turn grant; the other exiled card carries no grant.
        first.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "the chosen card may be played this turn (CR 118.9)");
        second.RuntimeExileCastAllowedCaster.Should().BeNull(
            "only the chosen card gets the play-this-turn grant");
    }

    [Fact]
    public void Resolve_PlayGrant_ClearsAtCleanup()
    {
        var bus = new EventBus();
        var card = MagmaticChannelerFactory.Create(_alice, bus, effects: null);
        SeatOnBattlefield(card, bus);

        var first = new Instant("Bolt", "{R}") { Owner = _alice };
        var second = new Instant("Bolt2", "{R}") { Owner = _alice };
        AddToLibrary(_alice, first, second);

        ExecuteActivation(card);
        first.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);

        // A Cleanup step clears the "this turn" grant (CR 514.2).
        bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _alice));
        first.RuntimeExileCastAllowedCaster.Should().BeNull(
            "the play-this-turn grant clears at the next Cleanup");
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoOp()
    {
        var card = MagmaticChannelerFactory.Create(_alice);
        SeatOnBattlefield(card, eventBus: null);

        ExecuteActivation(card);

        _alice.Zones.Exile.GetCards().Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ShortLibrary_ExilesWhatRemains()
    {
        var card = MagmaticChannelerFactory.Create(_alice);
        SeatOnBattlefield(card, eventBus: null);

        var only = new Instant("Bolt", "{R}") { Owner = _alice };
        AddToLibrary(_alice, only);

        ExecuteActivation(card);

        _alice.Zones.Exile.GetCards().Should().ContainSingle().Which.Should().BeSameAs(only);
        only.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "the single exiled card is the chosen one");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void AddToGraveyard(Player p, ICard card)
    {
        if (card is Card concrete)
        {
            concrete.SetOwner(p);
            concrete.SetZone(ZoneType.Graveyard);
        }
        p.Zones.Graveyard.AddCard(card);
    }

    private static void SeedGraveyardWithSpells(Player p, int instants, int sorceries)
    {
        for (var i = 0; i < instants; i++)
        {
            AddToGraveyard(p, new Instant($"Inst{System.Guid.NewGuid():N}", "{R}"));
        }
        for (var i = 0; i < sorceries; i++)
        {
            AddToGraveyard(p, new Sorcery($"Sorc{System.Guid.NewGuid():N}", "{R}"));
        }
    }

    private static void AddToLibrary(Player p, params ICard[] cards)
    {
        foreach (var c in cards)
        {
            if (c is Card concrete)
            {
                concrete.SetOwner(p);
                concrete.SetZone(ZoneType.Library);
            }
            p.Zones.Library.AddCard(c);
        }
    }

    private static void SeatOnBattlefield(Creature card, EventBus? eventBus)
    {
        card.SetZone(ZoneType.Battlefield);
        card.Owner!.Zones.Battlefield.AddCard(card);
        eventBus?.Publish(new CardMovedEvent(card, ZoneType.Library, ZoneType.Battlefield));
    }

    private static void ExecuteActivation(Creature card)
    {
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();
    }
}
