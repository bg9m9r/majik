using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MosswoodDreadknightFactory"/> (Wilds of
/// Eldraine, {B/G}{B/G}).
///
/// Covers:
/// - Identity (name, type, subtypes, P/T, mana cost, owner/controller).
/// - NamedCardFactory dispatch + Creature shape.
/// - Trample keyword marker present.
/// - Dies trigger gates on Battlefield → Graveyard for this card only.
/// - Dies-resolution returns the card to its owner's hand (raw zone path).
/// - Adventure spec: Dread Whispers, Sorcery, {B/G}.
/// - Adventure resolve: top → hand, second → graveyard (deterministic).
/// </summary>
public class MosswoodDreadknightTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MosswoodDreadknight_Identity()
    {
        var c = MosswoodDreadknightFactory.Create(_alice);

        c.Name.Should().Be("Mosswood Dreadknight");
        c.ManaCost.Should().Be("{B/G}{B/G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.GetPower().Should().Be(3);
        c.GetToughness().Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MosswoodDreadknight_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Mosswood Dreadknight", _alice);

        c.Should().BeOfType<Creature>();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample",
                "Trample keyword marker is wired");
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "dies trigger is attached");
    }

    // -----------------------------------------------------------------------
    // Adventure spec — Dread Whispers
    // -----------------------------------------------------------------------

    [Fact]
    public void MosswoodDreadknight_HasDreadWhispersAdventureSpec()
    {
        var c = MosswoodDreadknightFactory.Create(_alice);

        c.AdventureSpec.Should().NotBeNull("Dread Whispers Adventure half is attached");
        c.AdventureSpec!.Name.Should().Be("Dread Whispers");
        c.AdventureSpec.AdventureType.Should().Be(CardType.Sorcery);
        c.AdventureSpec.IsSorcery.Should().BeTrue();
        c.AdventureSpec.ManaCost.HybridPips.Should().HaveCount(1,
            "{B/G} is a single hybrid pip");
        c.AdventureSpec.ManaCost.TotalValue.Should().Be(1,
            "Dread Whispers costs 1 mana to cast");
    }

    [Fact]
    public void DreadWhispers_Resolve_PutsTopIntoHand_SecondIntoGraveyard()
    {
        var c = MosswoodDreadknightFactory.Create(_alice);

        // Seed three library cards so we can verify only the top two are
        // touched.
        var top = new Creature("Top", "1G", 1, 1) { Owner = _alice };
        var second = new Creature("Second", "1G", 1, 1) { Owner = _alice };
        var third = new Creature("Third", "1G", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        _alice.Zones.Library.AddCard(second);
        _alice.Zones.Library.AddCard(third);
        top.SetZone(ZoneType.Library);
        second.SetZone(ZoneType.Library);
        third.SetZone(ZoneType.Library);

        var def = MosswoodDreadknightFactory.BuildAdventureSpell(_alice, raw => raw);
        var effects = def.EffectFactory(new Majik.Core.Game.ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: default!));

        foreach (var eff in effects) eff.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "top library card routes to hand (v1 deterministic)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(second,
            "second library card routes to graveyard (v1 deterministic)");
        _alice.Zones.Library.GetCards().Should().Contain(third,
            "third card remains on top of the library");
    }

    [Fact]
    public void DreadWhispers_Resolve_LibraryWithOneCard_PutsItIntoHand_NoThrow()
    {
        // CR 608.2b — do as much as possible. With only one card in the
        // library, Dread Whispers puts the top into the hand and the
        // second move silently no-ops.
        var c = MosswoodDreadknightFactory.Create(_alice);

        var only = new Creature("Only", "1G", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(only);
        only.SetZone(ZoneType.Library);

        var def = MosswoodDreadknightFactory.BuildAdventureSpell(_alice, raw => raw);
        var effects = def.EffectFactory(new Majik.Core.Game.ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: default!));

        Action act = () => { foreach (var eff in effects) eff.Execute(); };
        act.Should().NotThrow();

        _alice.Zones.Hand.GetCards().Should().Contain(only,
            "the only library card routes to hand");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "nothing to route to graveyard");
    }

    // -----------------------------------------------------------------------
    // Dies trigger — Battlefield → Graveyard for THIS card only
    // -----------------------------------------------------------------------

    [Fact]
    public void MosswoodDreadknight_DiesTrigger_GatesOnSelfFromBattlefieldToGraveyard()
    {
        var c = MosswoodDreadknightFactory.Create(_alice);
        var other = new Creature("Bear", "1G", 2, 2) { Owner = _alice };

        // Active-zones guard: Battlefield + Graveyard. Place on
        // battlefield so the predicate's zone-guard passes (mirrors
        // MatterReshaper / Wurmcoil dies-trigger test setup).
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new CardMovedEvent(c, ZoneType.Battlefield, ZoneType.Graveyard))
            .Should().BeTrue("self battlefield → graveyard is the dies event");
        trigger.IsTriggered(new CardMovedEvent(other, ZoneType.Battlefield, ZoneType.Graveyard))
            .Should().BeFalse("another creature dying does not fire this trigger");
        trigger.IsTriggered(new CardMovedEvent(c, ZoneType.Battlefield, ZoneType.Exile))
            .Should().BeFalse("battlefield → exile is not 'dies'");
        trigger.IsTriggered(new CardMovedEvent(c, ZoneType.Hand, ZoneType.Graveyard))
            .Should().BeFalse("hand → graveyard (discard) is not 'dies'");
    }

    [Fact]
    public void MosswoodDreadknight_DiesResolution_ReturnsToOwnersHand()
    {
        var c = MosswoodDreadknightFactory.Create(_alice);

        // Stage the post-death state: card lives in owner's graveyard.
        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(c,
            "dies trigger routes the card back to its owner's hand");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(c,
            "card no longer sits in the graveyard");
        c.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void MosswoodDreadknight_DiesResolution_ReturnsToTRUE_OwnersHand_NotControllerHand()
    {
        // CR 400.7 — "owner". When Bob steals Mosswood Dreadknight (e.g.
        // via Threaten) and it dies, the dies-trigger return-to-hand
        // routes to Alice's hand (the true owner), not Bob's.
        var c = MosswoodDreadknightFactory.Create(_alice);

        // Stage: card was Bob-controlled when it died; now sits in
        // Alice's graveyard per CR 404.1 ("when a card moves to the
        // graveyard, it goes to its owner's graveyard").
        c.SetController(_bob);
        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(c,
            "return-to-OWNER's-hand routes to Alice even though Bob controlled it");
        _bob.Zones.Hand.GetCards().Should().NotContain(c);
    }
}
