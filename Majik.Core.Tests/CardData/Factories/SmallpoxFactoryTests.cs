using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SmallpoxFactory"/> — Smallpox (Planar Chaos,
/// {B}{B}, Sorcery).
///
/// Oracle text (Scryfall verified):
///   "Each player loses 1 life, discards a card, sacrifices a creature of
///    their choice, then sacrifices a land of their choice."
///
/// Covers:
/// - Identity (name, Sorcery type, {B}{B} cost).
/// - NamedCardFactory dispatch.
/// - Resolve (CR 608.2) iterates EACH player (controller included —
///   CR 109.5 / 800.4 "each player"). For every player, in order:
///     * loses 1 life (CR 119 — life loss, not damage);
///     * discards a card of their choice (CR 701.8);
///     * sacrifices a creature of their choice (CR 701.16);
///     * then sacrifices a land of their choice (CR 701.16).
///   A player with no card / no creature / no land simply skips that step
///   (CR 608.2 — do as much as possible).
/// - The four sub-effects sequence creature-sacrifice BEFORE land-sacrifice.
/// </summary>
[Trait("Color", "B")]
public class SmallpoxFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Smallpox_Identity()
    {
        var c = SmallpoxFactory.Create(_alice);

        c.Name.Should().Be("Smallpox");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.ManaCost.Should().Be("{B}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Smallpox()
    {
        var card = NamedCardFactory.Create("Smallpox", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Smallpox");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — each player loses 1, discards, sacs a creature, then a land.
    // -----------------------------------------------------------------------

    [Fact]
    public void Smallpox_Resolve_HasNoTargets()
    {
        var def = SmallpoxFactory.BuildSpellDefinition(() => new[] { _alice });
        def.TargetRequests.Should().BeEmpty(
            "Smallpox affects each player — no chosen targets (CR 115.1a)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    [Fact]
    public void Smallpox_Resolve_ControllerLosesLife_Discards_SacsCreature_AndLand()
    {
        var alice = new Player("Alice", 20);

        var creature = new Creature("Grizzly Bears", "1G", 2, 2);
        creature.SetOwner(alice);
        creature.SetController(alice);
        alice.Zones.Battlefield.AddCard(creature);
        creature.SetZone(ZoneType.Battlefield);

        var land = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        land.SetOwner(alice);
        land.SetController(alice);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var handCard = new Instant("Shock", "R");
        handCard.SetOwner(alice);
        alice.Zones.Hand.AddCard(handCard);
        handCard.SetZone(ZoneType.Hand);

        ResolveSmallpox(alice, () => new[] { alice });

        alice.LifeTotal.Should().Be(19, "CR 119 — each player loses 1 life");
        alice.Zones.Hand.GetCards().Should().NotContain(handCard,
            "CR 701.8 — each player discards a card");
        alice.Zones.Graveyard.GetCards().Should().Contain(handCard);
        alice.Zones.Battlefield.GetCards().Should().NotContain(creature,
            "CR 701.16 — each player sacrifices a creature of their choice");
        alice.Zones.Graveyard.GetCards().Should().Contain(creature);
        alice.Zones.Battlefield.GetCards().Should().NotContain(land,
            "CR 701.16 — each player then sacrifices a land of their choice");
        alice.Zones.Graveyard.GetCards().Should().Contain(land);
    }

    [Fact]
    public void Smallpox_Resolve_AffectsEachPlayer_IncludingOpponents()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        foreach (var p in new[] { alice, bob })
        {
            var creature = new Creature("Bear", "1G", 2, 2);
            creature.SetOwner(p);
            creature.SetController(p);
            p.Zones.Battlefield.AddCard(creature);
            creature.SetZone(ZoneType.Battlefield);

            var land = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
            land.SetOwner(p);
            land.SetController(p);
            p.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);

            var card = new Instant("Shock", "R");
            card.SetOwner(p);
            p.Zones.Hand.AddCard(card);
            card.SetZone(ZoneType.Hand);
        }

        ResolveSmallpox(alice, () => new[] { alice, bob });

        foreach (var p in new[] { alice, bob })
        {
            p.LifeTotal.Should().Be(19);
            p.Zones.Hand.GetCards().Should().BeEmpty("each player discards their card");
            p.Zones.Battlefield.GetCards().OfType<Creature>().Should().BeEmpty(
                "each player sacrifices their creature");
            p.Zones.Battlefield.GetCards().OfType<Land>().Should().BeEmpty(
                "each player sacrifices their land");
            p.Zones.Graveyard.GetCards().Should().HaveCount(3,
                "discarded card + sacrificed creature + sacrificed land");
        }
    }

    [Fact]
    public void Smallpox_Resolve_MissingPieces_DoAsMuchAsPossible()
    {
        // Alice: life only — empty hand, no creature, no land.
        var alice = new Player("Alice", 20);

        ResolveSmallpox(alice, () => new[] { alice });

        alice.LifeTotal.Should().Be(19, "life loss always applies");
        alice.Zones.Hand.GetCards().Should().BeEmpty();
        alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "CR 608.2 — do as much as possible; nothing to discard or sacrifice");
    }

    [Fact]
    public void Smallpox_Resolve_NonCreatureNonLandPermanents_AreNotSacrificed()
    {
        var alice = new Player("Alice", 20);

        // An artifact is neither a creature nor a land — Smallpox leaves it.
        var artifact = new Artifact("Sol Ring", "1");
        artifact.SetOwner(alice);
        artifact.SetController(alice);
        alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        ResolveSmallpox(alice, () => new[] { alice });

        alice.Zones.Battlefield.GetCards().Should().Contain(artifact,
            "an artifact is neither a creature nor a land");
    }

    private static void ResolveSmallpox(Player caster, Func<IReadOnlyList<Player>> players)
    {
        var def = SmallpoxFactory.BuildSpellDefinition(players);
        var chosen = new ChosenSpellParams(
            null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();
    }
}
