using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PeekFactory"/> (Onslaught / many reprints, {U}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Look at target player's hand.
///    Draw a card."
///
/// A cheap blue cantrip with a hidden-info "look at target player's hand"
/// rider. The look-at half is information-only (no zone change — same posture
/// as <see cref="UrzasBaubleFactory"/>'s look-at-hand); the cantrip draws one
/// card for the caster off the top of their library.
/// </summary>
[Trait("Color", "U")]
public class PeekFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ICard SeedHand(Player p, string name, string cost = "")
    {
        var c = new Card(name, cost);
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static ICard SeedLibraryTop(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.InsertCardAt(0, c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static ChosenSpellParams Chosen(Player target) =>
        new(ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

    [Fact]
    public void Identity_InstantAtU()
    {
        var card = PeekFactory.Create(_alice);
        card.Name.Should().Be("Peek");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{U}");
    }
    [Fact]
    public void DeclaresOneTargetPlayerRequest()
    {
        var def = PeekFactory.BuildSpellDefinition(_alice, resolver: o => o!);
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void Resolve_DrawsACardForTheCaster()
    {
        var top = SeedLibraryTop(_alice, "Island");
        SeedHand(_bob, "Lightning Bolt");

        var def = PeekFactory.BuildSpellDefinition(_alice, resolver: o => o!);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        top.Zone.Should().Be(ZoneType.Hand, "Peek's cantrip draws a card for the caster");
        _alice.Zones.Hand.GetCards().Should().Contain(top);
    }

    [Fact]
    public void Resolve_DoesNotMutateTargetHand()
    {
        SeedLibraryTop(_alice, "Island");
        var bobCard = SeedHand(_bob, "Lightning Bolt");
        var bobCard2 = SeedHand(_bob, "Counterspell");

        var def = PeekFactory.BuildSpellDefinition(_alice, resolver: o => o!);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        // "Look at target player's hand" is hidden-info inspection only —
        // no card leaves the target's hand, no zone change.
        bobCard.Zone.Should().Be(ZoneType.Hand);
        bobCard2.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().HaveCount(2);
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_CasterMayTargetSelf()
    {
        var top = SeedLibraryTop(_alice, "Island");
        var ownCard = SeedHand(_alice, "Brainstorm");

        // CR 115.3 — "target player" can be the caster (Peek's own hand).
        var def = PeekFactory.BuildSpellDefinition(_alice, resolver: o => o!);
        foreach (var e in def.EffectFactory(Chosen(_alice))) e.Execute();

        // Drew the top card; the pre-existing hand card is untouched.
        top.Zone.Should().Be(ZoneType.Hand);
        ownCard.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_EmptyLibrary_FlagsDrawFromEmpty()
    {
        SeedHand(_bob, "Lightning Bolt");

        var def = PeekFactory.BuildSpellDefinition(_alice, resolver: o => o!);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        // CR 704.5b — drawing from an empty library flags the loss SBA.
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    [Fact]
    public void Resolve_IllegalTarget_DoesNothing_NoDraw()
    {
        var top = SeedLibraryTop(_alice, "Island");

        // CR 608.2b — single illegal target: spell does nothing, including
        // the cantrip draw. Resolver returns a non-Player object.
        var def = PeekFactory.BuildSpellDefinition(_alice, resolver: _ => new object());
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        top.Zone.Should().Be(ZoneType.Library);
        _alice.Zones.Hand.GetCards().Should().NotContain(top);
    }
}
