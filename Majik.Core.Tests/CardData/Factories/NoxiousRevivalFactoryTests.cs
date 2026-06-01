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
/// Tests for Noxious Revival (New Phyrexia, {G/P}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "({G/P} can be paid with either {G} or 2 life.)
///    Put target card from a graveyard on top of its owner's library."
///
/// Card shape comes from the embedded JSON (<c>noxious-revival.json</c>) via
/// <see cref="CardDefinitionLoader"/> + <see cref="CardDefinitionFactory"/>;
/// the resolve-time "put target card on top of its owner's library" body is
/// built by the factory (mirrors <see cref="AncientGrudgeFactory"/> for the
/// data-only shape + resolver-bound effect, and
/// <see cref="MysticalTutorFactory"/> for the index-0 top-of-library
/// placement).
///
/// Covers:
///   - Card shape + dispatch ({G/P} Phyrexian-green, Instant).
///   - SpellDefinition shape: one 1..1 "target card in a graveyard" request.
///   - Resolves a card from the controller's graveyard onto the top of their
///     library (CR 700.4).
///   - Resolves a card from an OPPONENT's graveyard onto the top of THAT
///     opponent's library (CR 401.1 — owner's library, not controller's).
///   - No-op if the target left the graveyard before resolution (CR 608.2b).
/// </summary>
public class NoxiousRevivalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity + dispatch ───────────────────────────────────────────────────

    [Fact]
    public void NoxiousRevival_HasInstantShape_PhyrexianGreen_AtCostGP()
    {
        var card = NoxiousRevivalFactory.Create(_alice);

        card.Name.Should().Be("Noxious Revival");
        card.ManaCost.Should().Be("{G/P}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCostValue.PhyrexianPips.Should().ContainSingle()
            .Which.Should().Be(ManaColor.Green,
                because: "{G/P} is a single Phyrexian-green pip (CR 107.4f)");
        card.ManaCostValue.TotalValue.Should().Be(1,
            because: "a single Phyrexian pip contributes 1 to mana value");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NameAndCost_AreScryfallExact()
    {
        NoxiousRevivalFactory.CardName.Should().Be("Noxious Revival");
        NoxiousRevivalFactory.PrintedManaCost.Should().Be("{G/P}");
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsNoxiousRevivalShape()
    {
        var dispatched = NamedCardFactory.Create("Noxious Revival", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Noxious Revival");
        dispatched.ManaCost.Should().Be("{G/P}");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    // ── SpellDefinition shape ─────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_ExposesSingleGraveyardTargetRequest()
    {
        var def = NoxiousRevivalFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("graveyard");
    }

    // ── Resolve ───────────────────────────────────────────────────────────────

    [Fact]
    public void OwnGraveyardCard_GoesToTopOfOwnLibrary()
    {
        // Alice's library already has a card so we can assert the target
        // lands ON TOP (index 0), ahead of the existing card.
        var existingTop = NewLibraryCard(_alice, "Forest");
        var bolt = NewGraveyardInstant(_alice, "Lightning Bolt", "{R}");

        ResolveAgainst(bolt);

        bolt.Zone.Should().Be(ZoneType.Library);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bolt,
            because: "the card left the graveyard (CR 700.4)");

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(2);
        lib[0].Should().BeSameAs(bolt, because: "top of library = index 0");
        lib[1].Should().BeSameAs(existingTop);
    }

    [Fact]
    public void OpponentsGraveyardCard_GoesToTopOfThatOpponentsLibrary()
    {
        // CR 401.1 — the destination is the card's OWNER's library, even
        // though Alice controls the spell. Bob's card returns to Bob's deck.
        var bobsCreature = NewGraveyardCreature(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        ResolveAgainst(bobsCreature);

        bobsCreature.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bobsCreature);
        _bob.Zones.Library.GetCards().First().Should().BeSameAs(bobsCreature,
            because: "the card goes on top of ITS OWNER's library (CR 401.1)");
        // Alice's library is untouched — wrong owner.
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void TargetNoLongerInGraveyard_DoesNothing()
    {
        var bolt = NewGraveyardInstant(_alice, "Lightning Bolt", "{R}");

        // Target leaves the graveyard (e.g. exiled) before resolution.
        _alice.Zones.Graveyard.RemoveCard(bolt);
        bolt.SetZone(ZoneType.Exile);

        ResolveAgainst(bolt);

        bolt.Zone.Should().Be(ZoneType.Exile,
            because: "CR 608.2b — target not in a graveyard at resolution → no-op");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ResolveAgainst(ICard target)
    {
        var def = NoxiousRevivalFactory.BuildDefinition(o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private Instant NewGraveyardInstant(Player owner, string name, string cost)
    {
        var card = new Instant(name, cost);
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }

    private Creature NewGraveyardCreature(Player owner, string name, string cost, int p, int t)
    {
        var card = new Creature(name, cost, p, t);
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }

    private Land NewLibraryCard(Player owner, string name)
    {
        var card = new Land(name,
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Library);
        owner.Zones.Library.AddCard(card);
        return card;
    }
}
