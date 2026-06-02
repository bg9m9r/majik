using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="LilianasTriumphFactory"/>.
///
/// Card: Liliana's Triumph — Instant {1}{B} (War of the Spark).
///   "Each opponent sacrifices a creature of their choice. If you control a
///    Liliana planeswalker, each opponent also discards a card."
///
/// CR 701.16 — "sacrifice" bypasses Indestructible / regeneration and moves
/// the permanent from the battlefield to its owner's graveyard.
/// CR 701.8a — "discard"; the discarding player chooses which card.
/// CR 608.2 — the "if you control a Liliana planeswalker" condition is checked
/// as the spell resolves (it is part of the resolution, not an intervening-if
/// trigger). "Liliana planeswalker" = a permanent with the Liliana planeswalker
/// subtype (CR 205.3j).
///
/// Mirrors <see cref="SheoldredsEdictFactoryTests"/> (each-opponent edict /
/// agent-driven sacrifice pick) + <see cref="MindRotFactoryTests"/>
/// (agent-driven discard-of-choice).
/// </summary>
[Trait("Color", "B")]
public class LilianasTriumphFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);
    private readonly Player _carol = new("Carol", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LilianasTriumph_Identity()
    {
        var card = LilianasTriumphFactory.Create(_alice);

        card.Name.Should().Be("Liliana's Triumph");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpellDefinition_NoModes_NoMandatoryTargets()
    {
        var def = LilianasTriumphFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        // No modal choice and no targets — the card is "each opponent" /
        // "of their choice"; nothing gates the cast on a chosen target.
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Sacrifice half — each opponent sacrifices a creature
    // -----------------------------------------------------------------------

    [Fact]
    public void EachOpponentSacrificesCreature_ControllerUnaffected()
    {
        var bobBear   = SeedCreature(_bob, "Runeclaw Bear");
        var aliceBear = SeedCreature(_alice, "Grizzly Bears");

        var def = LilianasTriumphFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def);

        bobBear.Zone.Should().Be(ZoneType.Graveyard, "Bob is an opponent");
        aliceBear.Zone.Should().Be(ZoneType.Battlefield, "Alice cast it — not an opponent");
    }

    [Fact]
    public void EachOpponentSacrificesCreature_HitsEveryOpponent()
    {
        var bobBear   = SeedCreature(_bob, "Runeclaw Bear");
        var carolBear = SeedCreature(_carol, "Grizzly Bears");

        var def = LilianasTriumphFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def);

        bobBear.Zone.Should().Be(ZoneType.Graveyard);
        carolBear.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void EachOpponentSacrificesCreature_AgentDrivenPick()
    {
        var bear = SeedCreature(_bob, "Runeclaw Bear");
        var goyf = SeedCreature(_bob, "Tarmogoyf");

        var agent = new ScriptedAgent();
        agent.QueueFromBattlefield(candidates => candidates.First(c => c.Name == "Tarmogoyf"));

        var def = LilianasTriumphFactory.BuildDefinition(
            _alice, AllPlayers(), agent: agent);

        Run(def);

        goyf.Zone.Should().Be(ZoneType.Graveyard, "the affected player's agent chose Tarmogoyf");
        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void EachOpponentSacrificesCreature_NoCreature_NoOp()
    {
        var def = LilianasTriumphFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        var act = () => Run(def);
        act.Should().NotThrow();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Discard half — only when the caster controls a Liliana planeswalker
    // -----------------------------------------------------------------------

    [Fact]
    public void NoLilianaPlaneswalker_OpponentDoesNotDiscard()
    {
        SeedCreature(_bob, "Runeclaw Bear");
        var card = SeedHandCard(_bob, "Lightning Bolt");

        // Alice controls no planeswalker.
        var def = LilianasTriumphFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def);

        card.Zone.Should().Be(ZoneType.Hand, "the discard rider requires a Liliana planeswalker");
        _bob.Zones.Hand.GetCards().Should().Contain(card);
    }

    [Fact]
    public void ControlNonLilianaPlaneswalker_OpponentDoesNotDiscard()
    {
        SeedCreature(_bob, "Runeclaw Bear");
        var card = SeedHandCard(_bob, "Lightning Bolt");

        // Alice controls a planeswalker, but it is not a Liliana.
        SeedPlaneswalker(_alice, "Chandra, Torch of Defiance", CardSubtype.Chandra);

        var def = LilianasTriumphFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def);

        card.Zone.Should().Be(ZoneType.Hand, "Chandra is not a Liliana planeswalker");
    }

    [Fact]
    public void ControlLilianaPlaneswalker_EachOpponentAlsoDiscards()
    {
        SeedCreature(_bob, "Runeclaw Bear");
        var bobCard = SeedHandCard(_bob, "Lightning Bolt");

        SeedCreature(_carol, "Grizzly Bears");
        var carolCard = SeedHandCard(_carol, "Counterspell");

        SeedPlaneswalker(_alice, "Liliana of the Veil", CardSubtype.Liliana);

        var def = LilianasTriumphFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def);

        bobCard.Zone.Should().Be(ZoneType.Graveyard, "Alice controls a Liliana planeswalker");
        carolCard.Zone.Should().Be(ZoneType.Graveyard, "every opponent discards");
    }

    [Fact]
    public void ControlLilianaPlaneswalker_ControllerDoesNotDiscard()
    {
        var aliceCard = SeedHandCard(_alice, "Thoughtseize");
        SeedPlaneswalker(_alice, "Liliana of the Veil", CardSubtype.Liliana);

        var def = LilianasTriumphFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def);

        aliceCard.Zone.Should().Be(ZoneType.Hand, "Alice is not an opponent of herself");
    }

    [Fact]
    public void DiscardHalf_AgentDrivenPick()
    {
        SeedCreature(_bob, "Runeclaw Bear");
        var bolt = SeedHandCard(_bob, "Lightning Bolt");
        var swamp = SeedHandCard(_bob, "Swamp");

        SeedPlaneswalker(_alice, "Liliana of the Veil", CardSubtype.Liliana);

        var agent = new ScriptedAgent();
        // Bob keeps the sacrifice deterministic (one creature) and chooses
        // which card to discard.
        agent.QueueFromBattlefield(candidates => candidates[0]);
        agent.QueueFromHand(candidates => candidates.First(c => c.Name == "Swamp"));

        var def = LilianasTriumphFactory.BuildDefinition(
            _alice, AllPlayers(), agent: agent);

        Run(def);

        swamp.Zone.Should().Be(ZoneType.Graveyard, "Bob's agent chose to discard the Swamp");
        bolt.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void DiscardHalf_EmptyHand_NoOp()
    {
        SeedCreature(_bob, "Runeclaw Bear");
        SeedPlaneswalker(_alice, "Liliana of the Veil", CardSubtype.Liliana);

        var def = LilianasTriumphFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        var act = () => Run(def);
        act.Should().NotThrow();

        // The sacrifice still happened; the empty-hand discard is a no-op.
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private IReadOnlyList<Player> AllPlayers() => new[] { _alice, _bob, _carol };

    private void Run(SpellDefinition def)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: AllPlayers());
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private static Creature SeedCreature(Player owner, string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Planeswalker SeedPlaneswalker(Player owner, string name, CardSubtype subtype)
    {
        var pw = new Planeswalker(name, "{1}{B}{B}", 3, subtypes: new[] { subtype });
        pw.SetOwner(owner);
        pw.SetController(owner);
        owner.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);
        return pw;
    }

    private static ICard SeedHandCard(Player owner, string name)
    {
        var c = new Instant(name, "{1}");
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }
}
