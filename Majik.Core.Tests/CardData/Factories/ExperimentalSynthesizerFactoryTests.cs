using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Experimental Synthesizer (Aetherdrift / Modern Horizons 3,
/// {R}, Artifact). Oracle text (verified against Scryfall):
///   "When this artifact enters or leaves the battlefield, exile the top
///    card of your library. Until end of turn, you may play that card.
///    {2}{R}, Sacrifice this artifact: Create a 2/2 white Samurai creature
///    token with vigilance. Activate only as a sorcery."
///
/// Covers:
///   - Card identity (name, Artifact type, {R} mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - The enters-or-leaves trigger fires on ETB and on LTB, exiling the top
///     card of the controller's library + stamping the may-play grant.
///   - The trigger does NOT fire for an unrelated card moving zones.
///   - The activated ability: {2}{R} + Sacrifice this, sorcery-speed, creates
///     a 2/2 white Samurai token with vigilance.
/// </summary>
[Trait("Color", "R")]
public class ExperimentalSynthesizerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Card NewCardInLibrary(Player owner, string name)
    {
        ICard c = new Card(name, "R");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return (Card)c;
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_Artifact_AtCostR()
    {
        var card = ExperimentalSynthesizerFactory.Create(_alice);

        card.Name.Should().Be("Experimental Synthesizer");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Enters-or-leaves trigger (CR 603.6a / 603.10b)
    // -----------------------------------------------------------------------

    [Fact]
    public void HasEntersOrLeavesTrigger()
    {
        var card = ExperimentalSynthesizerFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void EntersBattlefield_ExilesTop_AndGrantsPlay()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = ExperimentalSynthesizerFactory.Create(_alice, bus, triggers);
        var top = NewCardInLibrary(_alice, "Shock");

        // The artifact is on the battlefield; its enters-or-leaves trigger is
        // active there (CR 603.6a). Simulate the entering move.
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(card, ZoneType.Stack, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1,
            "the enters-or-leaves trigger fires when the artifact enters");

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        top.Zone.Should().Be(ZoneType.Exile, "the top card is exiled");
        _alice.Zones.Exile.GetCards().Should().Contain(top);
        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "the controller may play the exiled card until end of turn");
    }

    [Fact]
    public void LeavesBattlefield_AlsoTriggers()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = ExperimentalSynthesizerFactory.Create(_alice, bus, triggers);
        NewCardInLibrary(_alice, "C1");

        // Simulate the artifact leaving the battlefield (e.g. sacrificed). The
        // trigger stays active in the graveyard (CR 603.6d), so the card's
        // post-move zone (Graveyard) keeps it registered.
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Graveyard));

        triggers.PendingCount.Should().Be(1,
            "the enters-or-leaves trigger fires when the artifact leaves");
    }

    [Fact]
    public void UnrelatedCardMove_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = ExperimentalSynthesizerFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        var other = NewCardInLibrary(_alice, "Other");

        bus.Publish(new CardMovedEvent(other, ZoneType.Library, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0,
            "only this artifact entering or leaving triggers the ability");
    }

    // -----------------------------------------------------------------------
    // Activated ability: {2}{R}, Sacrifice this: create a 2/2 white Samurai
    // with vigilance. Activate only as a sorcery.
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_SacForSamurai_IsSorcerySpeed()
    {
        var card = ExperimentalSynthesizerFactory.Create(_alice);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        ability.IsSorcerySpeed.Should().BeTrue("Activate only as a sorcery (CR 307.5)");
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        ability.Costs.OfType<SacrificeSelfCost>().Should().ContainSingle();
    }

    [Fact]
    public void ActivatedAbility_Resolve_CreatesWhiteSamuraiWithVigilance()
    {
        var card = ExperimentalSynthesizerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.IsToken);

        token.Should().NotBeNull();
        token!.Name.Should().Be("Samurai");
        token.GetPower().Should().Be(2);
        token.GetToughness().Should().Be(2);
        token.HasSubtype(CardSubtype.Samurai).Should().BeTrue();
        CardColors.GetColors(token).Should().Contain(ManaColor.White);
        token.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Vigilance");
    }
}
