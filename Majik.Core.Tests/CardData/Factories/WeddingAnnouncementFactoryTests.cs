using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Wedding Announcement // Wedding Festivity (Crimson Vow, {2}{W}) —
/// a transforming double-faced Enchantment.
///
/// Front oracle (verified against Scryfall 2026-06-02):
///   "At the beginning of your end step, put an invitation counter on this
///    enchantment. If you attacked with two or more creatures this turn, draw
///    a card. Otherwise, create a 1/1 white Human creature token. Then if this
///    enchantment has three or more invitation counters on it, transform it."
/// Back oracle (Wedding Festivity): "Creatures you control get +1/+1."
///
/// Covers:
///   - Card shape: name, Enchantment type, mana cost {2}{W}, MdfcState faces.
///   - End-step trigger adds an invitation counter.
///   - Did NOT attack with 2+ creatures → create a 1/1 white Human token.
///   - Attacked with 2+ creatures → draw a card (no token).
///   - Third counter transforms the enchantment to Wedding Festivity.
///   - Back face anthem: +1/+1 to the controller's creatures once transformed.
///   - Anthem is inert while front-face up.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "W")]
public class WeddingAnnouncementFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void WeddingAnnouncement_IsEnchantment_AtCost2W_WithFaces()
    {
        var c = WeddingAnnouncementFactory.Create(_alice);

        c.Name.Should().Be("Wedding Announcement // Wedding Festivity");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.MdfcState.Should().NotBeNull();
        c.MdfcState!.FrontFaceName.Should().Be("Wedding Announcement");
        c.MdfcState.BackFaceName.Should().Be("Wedding Festivity");
        c.MdfcState.IsBackFace.Should().BeFalse("starts on the front face");
    }

    [Fact]
    public void EndStep_AddsInvitationCounter_AndCreatesTokenWhenNotAttacked()
    {
        var (card, fire) = BuildOnBattlefield(continuousEffects: null, eventBus: null);

        fire();

        card.Counters.Count(WeddingAnnouncementFactory.InvitationCounter).Should().Be(1,
            "the end-step trigger puts an invitation counter on this enchantment");

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Where(c => c.IsToken).ToList();
        tokens.Should().HaveCount(1, "no 2+ attackers this turn → create a 1/1 white Human token");
        var token = tokens[0];
        token.GetPower().Should().Be(1);
        token.GetToughness().Should().Be(1);
        token.HasSubtype(CardSubtype.Human).Should().BeTrue();

        card.MdfcState!.IsBackFace.Should().BeFalse("one counter is below the transform threshold");
    }

    [Fact]
    public void EndStep_DrawsACard_WhenAttackedWithTwoOrMoreCreatures()
    {
        var bus = new EventBus();
        var (card, fire) = BuildOnBattlefield(continuousEffects: null, eventBus: bus);

        // Seed two library cards so the draw has something to take.
        SeedLibrary(_alice, 2);
        var handBefore = _alice.Zones.Hand.GetCards().Count();

        // Declare two of Alice's creatures as attackers this turn.
        var atk1 = MakeCreature("Attacker A", _alice, 2, 2);
        var atk2 = MakeCreature("Attacker B", _alice, 2, 2);
        bus.Publish(new CreatureAttacksEvent(atk1, _bob));
        bus.Publish(new CreatureAttacksEvent(atk2, _bob));

        fire();

        _alice.Zones.Hand.GetCards().Should().HaveCount(handBefore + 1,
            "attacked with two or more creatures → draw a card");
        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken).Should().BeEmpty("the draw branch does not also make a token");
    }

    [Fact]
    public void ThirdEndStep_TransformsToWeddingFestivity()
    {
        var (card, fire) = BuildOnBattlefield(continuousEffects: null, eventBus: null);

        fire();
        fire();
        card.MdfcState!.IsBackFace.Should().BeFalse("two counters is still below threshold");

        fire();
        card.Counters.Count(WeddingAnnouncementFactory.InvitationCounter).Should().Be(3);
        card.MdfcState.IsBackFace.Should().BeTrue(
            "three or more invitation counters transforms it to Wedding Festivity");
    }

    [Fact]
    public void BackFace_GrantsAnthemPlusOnePlusOne_ToControllersCreatures()
    {
        var svc = new ContinuousEffectsService();
        var (card, fire) = BuildOnBattlefield(continuousEffects: svc, eventBus: null);
        card.ActiveEffects = svc;

        var bear = MakeCreature("Bear", _alice, 2, 2);
        bear.ActiveEffects = svc;

        // Front-face up: anthem is inert.
        bear.GetPower().Should().Be(2, "Wedding Announcement (front face) has no anthem");

        // Three end steps → transform to Wedding Festivity.
        fire(); fire(); fire();
        card.MdfcState!.IsBackFace.Should().BeTrue();

        bear.GetPower().Should().Be(3, "Wedding Festivity: creatures you control get +1/+1");
        bear.GetToughness().Should().Be(3);
    }

    [Fact]
    public void BackFace_Anthem_DoesNotBuffOpponents()
    {
        var svc = new ContinuousEffectsService();
        var (card, fire) = BuildOnBattlefield(continuousEffects: svc, eventBus: null);
        card.ActiveEffects = svc;

        var bobBear = MakeCreature("Bob's Bear", _bob, 2, 2);
        bobBear.ActiveEffects = svc;

        fire(); fire(); fire();
        card.MdfcState!.IsBackFace.Should().BeTrue();

        bobBear.GetPower().Should().Be(2, "'Creatures you control' excludes opponents");
        bobBear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WeddingAnnouncement()
    {
        var card = Majik.Core.CardData.NamedCardFactory.Create(
            "Wedding Announcement // Wedding Festivity", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Wedding Announcement // Wedding Festivity");
        card.HasType(CardType.Enchantment).Should().BeTrue();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Build Wedding Announcement on Alice's battlefield with a manual
    /// trigger fire (no TriggerManager) so tests can run the end-step effect
    /// directly.</summary>
    private (Enchantment card, System.Action fire) BuildOnBattlefield(
        ContinuousEffectsService? continuousEffects, EventBus? eventBus)
    {
        var card = WeddingAnnouncementFactory.Create(
            _alice, continuousEffects, zones: null, eventBus: eventBus, triggers: null);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        void Fire() => trigger.Resolve();
        return (card, Fire);
    }

    private static Creature MakeCreature(string name, Player owner, int p, int t)
    {
        var c = new Creature(name, "{W}", p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static void SeedLibrary(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = new Creature($"Lib{i}", "{G}", 1, 1);
            c.SetOwner(player);
            c.SetZone(ZoneType.Library);
            player.Zones.Library.AddCard(c);
        }
    }
}
