using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tests.Helpers;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Blasphemous Edict (Modern Horizons 3, {3}{B}{B}, Sorcery).
///
/// Oracle text (Scryfall verified):
///   "You may pay {B} rather than pay this spell's mana cost if there are
///    thirteen or more creatures on the battlefield.
///    Each player sacrifices thirteen creatures of their choice."
///
/// Covers:
///   - Identity (name, Sorcery type, {3}{B}{B} cost, black).
///   - Resolve (CR 608.2 / 701.16) iterates EACH player (controller included —
///     CR 109.5 / 800.4) and sacrifices up to thirteen creatures of their
///     choice. Fewer than thirteen → sacrifice all (CR 608.2).
///   - No targets (CR 115.1a — "each player").
///   - The {B} alternative cost (CR 118.9) is available iff there are 13+
///     creatures across every battlefield (CR 109.4) and pays {B}.
/// </summary>
[Trait("Color", "B")]
public class BlasphemousEdictFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BlasphemousEdict_Identity()
    {
        var c = BlasphemousEdictFactory.Create(_alice);

        c.Name.Should().Be("Blasphemous Edict");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.ManaCost.Should().Be("{3}{B}{B}");
        CardColors.GetColors(c).Should().Contain(ManaColor.Black);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — each player sacrifices up to 13 creatures of their choice
    // -----------------------------------------------------------------------

    [Fact]
    public void BlasphemousEdict_Resolve_HasNoTargets()
    {
        var def = BlasphemousEdictFactory.BuildSpellDefinition();
        def.TargetRequests.Should().BeEmpty(
            "Blasphemous Edict affects each player — no chosen targets (CR 115.1a)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    [Fact]
    public void BlasphemousEdict_Resolve_EachPlayerSacrificesAllUpToThirteen()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Alice controls 14 creatures (more than 13) + a non-creature.
        var aliceCreatures = AddCreatures(alice, 14);
        var aliceArtifact = new Artifact("Sol Ring", "1");
        aliceArtifact.SetOwner(alice);
        aliceArtifact.SetController(alice);
        alice.Zones.Battlefield.AddCard(aliceArtifact);
        aliceArtifact.SetZone(ZoneType.Battlefield);

        // Bob controls 3 creatures (fewer than 13).
        var bobCreatures = AddCreatures(bob, 3);

        ResolveEdict(alice, alice, bob);

        // Alice sacrifices exactly 13 of her 14 creatures (CR 701.16).
        alice.Zones.Battlefield.GetCards().OfType<Creature>().Should().HaveCount(1,
            "each player sacrifices thirteen creatures — Alice had 14, one remains");
        alice.Zones.Graveyard.GetCards().OfType<Creature>().Should().HaveCount(13);

        // The non-creature artifact is untouched.
        alice.Zones.Battlefield.GetCards().Should().Contain(aliceArtifact,
            "Blasphemous Edict sacrifices creatures only");

        // Bob sacrifices all 3 (CR 608.2 — do as much as possible).
        bob.Zones.Battlefield.GetCards().OfType<Creature>().Should().BeEmpty(
            "Bob had fewer than thirteen creatures — all are sacrificed");
        bob.Zones.Graveyard.GetCards().OfType<Creature>().Should().HaveCount(3);
    }

    [Fact]
    public void BlasphemousEdict_Resolve_NoCreatures_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        ResolveEdict(alice, alice);

        alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "CR 608.2 — nothing to sacrifice");
    }

    // -----------------------------------------------------------------------
    // {B} alternative cost (CR 118.9) gated on 13+ creatures (CR 109.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void BlasphemousEdict_AltCost_PaysSingleBlack()
    {
        var alt = BlasphemousEdictFactory.BuildAlternativeCost();

        alt.AlternativeManaCost.Black.Should().Be(1, "the alternative cost is {B}");
        alt.AlternativeManaCost.Generic.Should().Be(0);
        alt.RequiredCreatures.Should().Be(13);
    }

    [Fact]
    public void BlasphemousEdict_AltCost_UnavailableBelowThirteenCreatures()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        AddCreatures(alice, 7);
        AddCreatures(bob, 5); // 12 total — below the threshold.

        var card = BlasphemousEdictFactory.Create(alice);
        alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var alt = BlasphemousEdictFactory.BuildAlternativeCost(new[] { alice, bob });

        alt.CanCastFor(card, alice).Should().BeFalse(
            "fewer than thirteen creatures on the battlefield (CR 109.4)");
    }

    [Fact]
    public void BlasphemousEdict_AltCost_AvailableAtThirteenCreatures()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        AddCreatures(alice, 7);
        AddCreatures(bob, 6); // 13 total — meets the threshold.

        var card = BlasphemousEdictFactory.Create(alice);
        alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var alt = BlasphemousEdictFactory.BuildAlternativeCost(new[] { alice, bob });

        alt.CanCastFor(card, alice).Should().BeTrue(
            "thirteen or more creatures on the battlefield (CR 118.9 / CR 109.4)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static List<Creature> AddCreatures(Player owner, int count)
    {
        var made = new List<Creature>();
        for (int i = 0; i < count; i++)
        {
            var c = new Creature($"Bear {owner.Name} {i}", "1G", 2, 2);
            c.SetOwner(owner);
            c.SetController(owner);
            owner.Zones.Battlefield.AddCard(c);
            c.SetZone(ZoneType.Battlefield);
            made.Add(c);
        }
        return made;
    }

    private static void ResolveEdict(Player caster, params Player[] players)
    {
        var def = BlasphemousEdictFactory.BuildSpellDefinition();
        var chosen = new ChosenSpellParams(
            null, null, System.Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(chosen);

        // Resolve through a live GameContext so the each-player body reads
        // ctx.Game.AllPlayers (the production path).
        ContextResolve.ResolveEffects(effects, caster, players);
    }
}
