using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GoblinGuideFactory"/> (Zendikar, {R}).
///
/// Creature — Goblin Scout 2/2. Oracle text:
///   "Haste. Whenever Goblin Guide attacks, defending player reveals the
///    top card of their library. If it's a land card, that player puts it
///    into their hand."
///
/// Covers:
///   - Identity (Creature — Goblin Scout, {R}, 2/2, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Printed Haste keyword marker (CR 702.10).
///   - Attack trigger matches only Goblin Guide as attacker.
///   - Attack trigger publishes CardRevealedEvent for defender's top card.
///   - Land reveal → moves card Library → Hand (with and without zone svc).
///   - Non-land reveal → card stays in library, hand unchanged.
///   - Empty defender library → no-op (no reveal, no crash).
///   - Planeswalker defender → no-op (no reveal, no move).
/// </summary>
public class GoblinGuideFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinGuide_Identity()
    {
        var guide = GoblinGuideFactory.Create(_alice);

        guide.Name.Should().Be("Goblin Guide");
        guide.ManaCost.Should().Be("{R}");
        guide.HasType(CardType.Creature).Should().BeTrue();
        guide.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        guide.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        guide.BasePower.Should().Be(2);
        guide.BaseToughness.Should().Be(2);
        guide.Owner.Should().BeSameAs(_alice);
        guide.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GoblinGuide_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Goblin Guide", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Goblin Guide");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
    }

    [Fact]
    public void GoblinGuide_HasPrintedHaste()
    {
        var guide = GoblinGuideFactory.Create(_alice);
        guide.Zone = ZoneType.Battlefield;

        guide.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Haste",
                "CR 702.10 — printed Haste.");
        CombatAbilities.HasHaste(guide).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Attack trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinGuide_AttackTrigger_Matches_OnSelfAttack()
    {
        var guide = GoblinGuideFactory.Create(_alice);
        var trig = GetAttackTrigger(guide);

        var ev = new CreatureAttacksEvent(guide, _bob);
        trig.Condition.Matches(ev, trig).Should().BeTrue();
    }

    [Fact]
    public void GoblinGuide_AttackTrigger_DoesNotMatch_OnOtherAttacker()
    {
        var guide = GoblinGuideFactory.Create(_alice);
        var trig = GetAttackTrigger(guide);

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);
        var ev = new CreatureAttacksEvent(other, _bob);

        trig.Condition.Matches(ev, trig).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Resolution — land on top
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinGuide_AttackTriggerEffect_LandOnTop_MovesLandToHand_AndPublishesReveal()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var guide = GoblinGuideFactory.Create(_alice, zones, bus, triggers: null);

        // Bob's library: land on top, plus a follower below.
        var land = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(land);

        var follower = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        follower.SetOwner(_bob);
        follower.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(follower);

        var revealed = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(revealed.Add);

        var trig = GetAttackTrigger(guide);
        trig.Condition.Matches(new CreatureAttacksEvent(guide, _bob), trig)
            .Should().BeTrue();

        foreach (var effect in trig.Effects) effect.Execute();

        // Reveal event published once, for the land, from the library.
        revealed.Should().HaveCount(1);
        revealed[0].Card.Should().BeSameAs(land);
        revealed[0].Player.Should().BeSameAs(_bob);
        revealed[0].From.Should().Be(ZoneType.Library);
        revealed[0].Reason.Should().Be("goblin-guide");

        // Land moved to hand; follower still on top of library.
        land.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(land);
        _bob.Zones.Library.GetCards().Should().NotContain(land);
        _bob.Zones.Library.GetCards().First().Should().BeSameAs(follower);
    }

    [Fact]
    public void GoblinGuide_AttackTriggerEffect_LandOnTop_NoZoneService_StillMovesViaRawZones()
    {
        // No event bus, no zone service — raw move path; no CardRevealedEvent
        // expected but the land still ends up in hand.
        var guide = GoblinGuideFactory.Create(_alice);

        var land = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(land);

        var trig = GetAttackTrigger(guide);
        trig.Condition.Matches(new CreatureAttacksEvent(guide, _bob), trig)
            .Should().BeTrue();

        foreach (var effect in trig.Effects) effect.Execute();

        land.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(land);
        _bob.Zones.Library.GetCards().Should().NotContain(land);
    }

    // -----------------------------------------------------------------------
    // Resolution — non-land on top
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinGuide_AttackTriggerEffect_NonLandOnTop_StaysInLibrary()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var guide = GoblinGuideFactory.Create(_alice, zones, bus, triggers: null);

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(bears);

        var revealed = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(revealed.Add);

        var trig = GetAttackTrigger(guide);
        trig.Condition.Matches(new CreatureAttacksEvent(guide, _bob), trig)
            .Should().BeTrue();

        foreach (var effect in trig.Effects) effect.Execute();

        // Revealed, but stays on top of library — hand unchanged.
        revealed.Should().HaveCount(1);
        revealed[0].Card.Should().BeSameAs(bears);

        bears.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Library.GetCards().Should().Contain(bears);
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinGuide_AttackTriggerEffect_EmptyLibrary_NoOpNoReveal()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var guide = GoblinGuideFactory.Create(_alice, zones, bus, triggers: null);

        var revealed = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(revealed.Add);

        var trig = GetAttackTrigger(guide);
        trig.Condition.Matches(new CreatureAttacksEvent(guide, _bob), trig)
            .Should().BeTrue();

        // Bob's library is empty.
        foreach (var effect in trig.Effects) effect.Execute();

        revealed.Should().BeEmpty();
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void GoblinGuide_AttackTriggerEffect_PlaneswalkerDefender_NoOp()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var guide = GoblinGuideFactory.Create(_alice, zones, bus, triggers: null);

        // Bob has a Planeswalker. Build it as a Planeswalker permanent.
        var pw = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", 3);
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        // Seed Bob's library so a Player-defender would have something to
        // reveal — but the defender here is the planeswalker, not the
        // player.
        var land = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(land);

        var revealed = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(revealed.Add);

        var trig = GetAttackTrigger(guide);
        // CR 506.2 — attack into a planeswalker; defender object is the PW,
        // not the player. The capture closure stores `as Player` → null,
        // so the effect is a no-op.
        trig.Condition.Matches(new CreatureAttacksEvent(guide, pw), trig)
            .Should().BeTrue();

        foreach (var effect in trig.Effects) effect.Execute();

        revealed.Should().BeEmpty();
        land.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static TriggeredAbility GetAttackTrigger(ICard card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);
}
