using FluentAssertions;
using Majik.Core.CardData;
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
/// Tests for <see cref="SheoldredsEdictFactory"/>.
///
/// Card: Sheoldred's Edict — Instant {1}{B} (Phyrexia: All Will Be One).
///   "Choose one —
///     • Each opponent sacrifices a nontoken creature of their choice.
///     • Each opponent sacrifices a creature token of their choice.
///     • Each opponent sacrifices a planeswalker of their choice."
///
/// CR 700.2d — modal "Choose one —"; one mode picked at cast time.
/// CR 701.16 — "sacrifice" bypasses Indestructible / regeneration and moves
/// the permanent from the battlefield to its owner's graveyard. Each affected
/// player sacrifices a permanent matching the chosen mode's filter "of their
/// choice".
///
/// Mirrors <see cref="DiabolicEdictFactoryTests"/> (edict/agent-driven pick)
/// + <see cref="IzzetCharmTests"/> (modal choose-one shape).
/// </summary>
public class SheoldredsEdictFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);
    private readonly Player _carol = new("Carol", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SheoldredsEdict_Identity()
    {
        var card = SheoldredsEdictFactory.Create(_alice);

        card.Name.Should().Be("Sheoldred's Edict");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SheoldredsEdict()
    {
        var card = NamedCardFactory.Create("Sheoldred's Edict", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Sheoldred's Edict");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape — three modes, no mandatory targets
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_HasThreeModes_NoMandatoryTargets()
    {
        var def = SheoldredsEdictFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        def.Modes.Should().HaveCount(3);
        def.Modes[0].Should().Contain("nontoken creature");
        def.Modes[1].Should().Contain("creature token");
        def.Modes[2].Should().Contain("planeswalker");

        // Sheoldred's Edict has no targets — it's "each opponent" / "of their
        // choice". Nothing in the spell gates the cast on a target.
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Mode 0 — nontoken creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_EachOpponentSacrificesNontokenCreature_TokenSpared()
    {
        var token   = SeedCreature(_bob, "Zombie Token", isToken: true);
        var nonToken = SeedCreature(_bob, "Runeclaw Bear", isToken: false);

        var def = SheoldredsEdictFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def, SheoldredsEdictFactory.ModeNontokenCreature);

        nonToken.Zone.Should().Be(ZoneType.Graveyard, "mode 0 sacrifices a nontoken creature");
        token.Zone.Should().Be(ZoneType.Battlefield, "the token is not a nontoken creature");
    }

    [Fact]
    public void Mode0_DoesNotAffectController()
    {
        var bobBear   = SeedCreature(_bob, "Runeclaw Bear", isToken: false);
        var aliceBear = SeedCreature(_alice, "Grizzly Bears", isToken: false);

        var def = SheoldredsEdictFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def, SheoldredsEdictFactory.ModeNontokenCreature);

        bobBear.Zone.Should().Be(ZoneType.Graveyard, "Bob is an opponent");
        aliceBear.Zone.Should().Be(ZoneType.Battlefield, "Alice cast it — not an opponent");
    }

    [Fact]
    public void Mode0_HitsEveryOpponent()
    {
        var bobBear   = SeedCreature(_bob, "Runeclaw Bear", isToken: false);
        var carolBear = SeedCreature(_carol, "Grizzly Bears", isToken: false);

        var def = SheoldredsEdictFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def, SheoldredsEdictFactory.ModeNontokenCreature);

        bobBear.Zone.Should().Be(ZoneType.Graveyard);
        carolBear.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Mode0_AgentDrivenPick()
    {
        var bear  = SeedCreature(_bob, "Runeclaw Bear", isToken: false);
        var goyf  = SeedCreature(_bob, "Tarmogoyf", isToken: false);

        var agent = new ScriptedAgent();
        agent.QueueFromBattlefield(candidates => candidates.First(c => c.Name == "Tarmogoyf"));

        var def = SheoldredsEdictFactory.BuildDefinition(
            _alice, AllPlayers(), agent: agent);

        Run(def, SheoldredsEdictFactory.ModeNontokenCreature);

        goyf.Zone.Should().Be(ZoneType.Graveyard, "agent chose Tarmogoyf");
        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Mode0_NoNontokenCreature_NoOp()
    {
        var token = SeedCreature(_bob, "Zombie Token", isToken: true);

        var def = SheoldredsEdictFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        var act = () => Run(def, SheoldredsEdictFactory.ModeNontokenCreature);
        act.Should().NotThrow();

        token.Zone.Should().Be(ZoneType.Battlefield, "no nontoken creature to sacrifice");
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Mode 1 — creature token
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_EachOpponentSacrificesCreatureToken_NontokenSpared()
    {
        var token    = SeedCreature(_bob, "Zombie Token", isToken: true);
        var nonToken = SeedCreature(_bob, "Runeclaw Bear", isToken: false);

        var def = SheoldredsEdictFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def, SheoldredsEdictFactory.ModeCreatureToken);

        token.Zone.Should().Be(ZoneType.Graveyard, "mode 1 sacrifices a creature token");
        nonToken.Zone.Should().Be(ZoneType.Battlefield, "the nontoken creature is not a token");
    }

    [Fact]
    public void Mode1_NoToken_NoOp()
    {
        var nonToken = SeedCreature(_bob, "Runeclaw Bear", isToken: false);

        var def = SheoldredsEdictFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        var act = () => Run(def, SheoldredsEdictFactory.ModeCreatureToken);
        act.Should().NotThrow();

        nonToken.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Mode 2 — planeswalker
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode2_EachOpponentSacrificesPlaneswalker_CreatureSpared()
    {
        var pw   = SeedPlaneswalker(_bob, "Liliana of the Veil");
        var bear = SeedCreature(_bob, "Runeclaw Bear", isToken: false);

        var def = SheoldredsEdictFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def, SheoldredsEdictFactory.ModePlaneswalker);

        pw.Zone.Should().Be(ZoneType.Graveyard, "mode 2 sacrifices a planeswalker");
        bear.Zone.Should().Be(ZoneType.Battlefield, "the creature is not a planeswalker");
    }

    [Fact]
    public void Mode2_NoPlaneswalker_NoOp()
    {
        var bear = SeedCreature(_bob, "Runeclaw Bear", isToken: false);

        var def = SheoldredsEdictFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        var act = () => Run(def, SheoldredsEdictFactory.ModePlaneswalker);
        act.Should().NotThrow();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private IReadOnlyList<Player> AllPlayers() => new[] { _alice, _bob, _carol };

    private void Run(SpellDefinition def, int mode)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: mode,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: AllPlayers());
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private static Creature SeedCreature(Player owner, string name, bool isToken)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        if (isToken) c.MarkAsToken();
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Planeswalker SeedPlaneswalker(Player owner, string name)
    {
        var pw = new Planeswalker(name, "{1}{B}{B}", 3);
        pw.SetOwner(owner);
        pw.SetController(owner);
        owner.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);
        return pw;
    }
}
