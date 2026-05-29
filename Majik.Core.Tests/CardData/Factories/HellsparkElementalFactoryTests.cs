using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HellsparkElementalFactory"/>.
///
/// Hellspark Elemental — Creature — Elemental {1}{R} (Eventide):
///   "Trample, haste
///    At the beginning of the end step, sacrifice this creature.
///    Unearth {1}{R}"
///
/// Covers:
///   - Card identity (name, type, 3/1, mana cost, Elemental, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Trample + Haste keyword markers present (CombatAbilities.HasHaste true).
///   - Unearth ability shape: ActivatedAbility, {1}{R} ManaCostCost, sorcery
///     speed, no tap cost.
///   - End-step sacrifice trigger fires (battlefield → graveyard) on any
///     end step.
///   - Unearth resolution: graveyard → battlefield, gains haste, summoning
///     sickness cleared, CardMovedEvent fires.
///   - Unearth end-step rider EXILES (not graveyard) the returned creature.
///   - Unearth no-ops when the card is not in the graveyard.
/// </summary>
public class HellsparkElementalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public HellsparkElementalFactoryTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_CreatureRed_ThreeOne_Elemental()
    {
        var card = HellsparkElementalFactory.Create(_alice);

        card.Name.Should().Be("Hellspark Elemental");
        card.Should().BeOfType<Creature>();
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.GetPower().Should().Be(3);
        card.GetToughness().Should().Be(1);
        card.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsCreatureWithKeywordsAndUnearth()
    {
        var card = NamedCardFactory.Create("Hellspark Elemental", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Hellspark Elemental");
        card.ManaCost.Should().Be("{1}{R}");
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample")
            .And.Contain(k => k.Keyword == "Haste");
        card.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "the Unearth activated ability");
    }

    // -----------------------------------------------------------------------
    // Keywords
    // -----------------------------------------------------------------------

    [Fact]
    public void HasTrampleAndHaste_KeywordMarkers()
    {
        var card = HellsparkElementalFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain(new[] { "Trample", "Haste" });
        CombatAbilities.HasHaste(card).Should().BeTrue(
            "printed Haste lets Hellspark attack the turn it enters (CR 702.10)");
    }

    // -----------------------------------------------------------------------
    // Unearth ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void UnearthAbility_IsSorcerySpeed_WithOneRedOneGenericManaCost()
    {
        var card = HellsparkElementalFactory.Create(_alice);
        var unearth = card.Abilities.OfType<ActivatedAbility>().Single();

        unearth.IsSorcerySpeed.Should().BeTrue(
            "Unearth only as a sorcery (CR 702.84a)");
        unearth.Costs.Should().ContainSingle();
        var mana = unearth.Costs.OfType<ManaCostCost>().Single();
        mana.Cost.Red.Should().Be(1, "Unearth {1}{R} — one red");
        mana.Cost.Generic.Should().Be(1, "Unearth {1}{R} — one generic");
        unearth.Source.Should().BeSameAs(card);
        unearth.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // End-step sacrifice trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void EndStep_SacrificesItself_BattlefieldToGraveyard()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var card = HellsparkElementalFactory.Create(_alice, _zones, triggers);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        card.Zone.Should().Be(ZoneType.Battlefield);

        // Begin an end step — the sacrifice trigger fires.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        card.Zone.Should().Be(ZoneType.Graveyard,
            "CR 603.2 / CR 701.16 — at the beginning of the end step, sacrifice this creature");
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(card);
    }

    // -----------------------------------------------------------------------
    // Unearth resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Unearth_ReturnsFromGraveyard_GainsHaste_FiresMovedEvent()
    {
        var card = HellsparkElementalFactory.Create(_alice, _zones, triggers: null);
        card.ActiveEffects = new ContinuousEffectsService();
        card.HasSummoningSickness = true;
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);

        var movedEvents = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var unearth = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in unearth.Effects) e.Execute();

        card.Zone.Should().Be(ZoneType.Battlefield,
            "Unearth returns the card from graveyard to the battlefield (CR 702.84a)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(card);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(card);
        CombatAbilities.HasHaste(card).Should().BeTrue("Unearth grants haste");
        card.HasSummoningSickness.Should().BeFalse(
            "haste clears summoning sickness (CR 702.10b)");
        movedEvents.Should().Contain(
            e => ReferenceEquals(e.Card, card)
                 && e.FromZone == ZoneType.Graveyard
                 && e.ToZone == ZoneType.Battlefield);
    }

    [Fact]
    public void Unearth_EndStep_ExilesTheReturnedCreature_NotGraveyard()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var card = HellsparkElementalFactory.Create(_alice, _zones, triggers);
        card.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);

        var unearth = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in unearth.Effects) e.Execute();

        card.Zone.Should().Be(ZoneType.Battlefield,
            "Unearth returned the card to the battlefield");

        // Begin the next end step — both the printed end-step sacrifice and
        // the unearth delayed exile fire. Unearth's exile rider is the
        // governing outcome: the card lands in EXILE (CR 702.84c), not the
        // graveyard.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        card.Zone.Should().Be(ZoneType.Exile,
            "CR 702.84c — an unearthed creature is exiled at the beginning of the next end step");
        _alice.Zones.Exile.GetCards().Should().Contain(card);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(card);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(card);
    }

    [Fact]
    public void Unearth_NoOp_WhenCardNotInGraveyard()
    {
        var card = HellsparkElementalFactory.Create(_alice, _zones, triggers: null);
        // Card sits on the battlefield, not the graveyard.
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var unearth = card.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => { foreach (var e in unearth.Effects) e.Execute(); };

        act.Should().NotThrow("Unearth resolves to a clean no-op outside the graveyard");
        card.Zone.Should().Be(ZoneType.Battlefield,
            "no graveyard source → nothing returns; the card is untouched");
    }
}
