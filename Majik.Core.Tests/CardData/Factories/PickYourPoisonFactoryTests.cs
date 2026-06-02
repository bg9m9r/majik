using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for <see cref="PickYourPoisonFactory"/>.
///
/// Card: Pick Your Poison — Sorcery {G} (Wilds of Eldraine).
///   "Choose one —
///     • Each opponent sacrifices an artifact of their choice.
///     • Each opponent sacrifices an enchantment of their choice.
///     • Each opponent sacrifices a creature with flying of their choice."
///
/// CR 700.2d — modal "Choose one —"; one mode picked at cast time.
/// CR 701.16 — "sacrifice" bypasses Indestructible / regeneration and moves
/// the permanent from the battlefield to its owner's graveyard. Each affected
/// player sacrifices a permanent matching the chosen mode's filter "of their
/// choice".
///
/// Mirrors <see cref="SheoldredsEdictFactoryTests"/> (modal each-opponent edict
/// of-their-choice shape); the per-mode filter differs (artifact / enchantment
/// / creature-with-flying).
/// </summary>
[Trait("Color", "G")]
public class PickYourPoisonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);
    private readonly Player _carol = new("Carol", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PickYourPoison_Identity()
    {
        var card = PickYourPoisonFactory.Create(_alice);

        card.Name.Should().Be("Pick Your Poison");
        card.ManaCost.Should().Be("{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // SpellDefinition shape — three modes, no mandatory targets
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_HasThreeModes_NoMandatoryTargets()
    {
        var def = PickYourPoisonFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        def.Modes.Should().HaveCount(3);
        def.Modes[0].Should().Contain("artifact");
        def.Modes[1].Should().Contain("enchantment");
        def.Modes[2].Should().Contain("flying");

        // Pick Your Poison has no targets — it's "each opponent" / "of their
        // choice". Nothing in the spell gates the cast on a target.
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Mode 0 — artifact
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_EachOpponentSacrificesArtifact_NonArtifactSpared()
    {
        var artifact = SeedArtifact(_bob, "Mishra's Bauble");
        var bear     = SeedCreature(_bob, "Runeclaw Bear", flying: false);

        var def = PickYourPoisonFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def, PickYourPoisonFactory.ModeArtifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard, "mode 0 sacrifices an artifact");
        bear.Zone.Should().Be(ZoneType.Battlefield, "the creature is not an artifact");
    }

    [Fact]
    public void Mode0_DoesNotAffectController()
    {
        var bobArtifact   = SeedArtifact(_bob, "Mishra's Bauble");
        var aliceArtifact = SeedArtifact(_alice, "Bonesplitter");

        var def = PickYourPoisonFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def, PickYourPoisonFactory.ModeArtifact);

        bobArtifact.Zone.Should().Be(ZoneType.Graveyard, "Bob is an opponent");
        aliceArtifact.Zone.Should().Be(ZoneType.Battlefield, "Alice cast it — not an opponent");
    }

    [Fact]
    public void Mode0_HitsEveryOpponent()
    {
        var bobArtifact   = SeedArtifact(_bob, "Mishra's Bauble");
        var carolArtifact = SeedArtifact(_carol, "Bonesplitter");

        var def = PickYourPoisonFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def, PickYourPoisonFactory.ModeArtifact);

        bobArtifact.Zone.Should().Be(ZoneType.Graveyard);
        carolArtifact.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Mode0_AgentDrivenPick()
    {
        var bauble  = SeedArtifact(_bob, "Mishra's Bauble");
        var sword   = SeedArtifact(_bob, "Bonesplitter");

        var agent = new ScriptedAgent();
        agent.QueueFromBattlefield(candidates => candidates.First(c => c.Name == "Bonesplitter"));

        var def = PickYourPoisonFactory.BuildDefinition(
            _alice, AllPlayers(), agent: agent);

        Run(def, PickYourPoisonFactory.ModeArtifact);

        sword.Zone.Should().Be(ZoneType.Graveyard, "agent chose Bonesplitter");
        bauble.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Mode0_NoArtifact_NoOp()
    {
        var bear = SeedCreature(_bob, "Runeclaw Bear", flying: false);

        var def = PickYourPoisonFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        var act = () => Run(def, PickYourPoisonFactory.ModeArtifact);
        act.Should().NotThrow();

        bear.Zone.Should().Be(ZoneType.Battlefield, "no artifact to sacrifice");
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Mode 1 — enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_EachOpponentSacrificesEnchantment_NonEnchantmentSpared()
    {
        var enchantment = SeedEnchantment(_bob, "Oblivion Ring");
        var bear        = SeedCreature(_bob, "Runeclaw Bear", flying: false);

        var def = PickYourPoisonFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def, PickYourPoisonFactory.ModeEnchantment);

        enchantment.Zone.Should().Be(ZoneType.Graveyard, "mode 1 sacrifices an enchantment");
        bear.Zone.Should().Be(ZoneType.Battlefield, "the creature is not an enchantment");
    }

    [Fact]
    public void Mode1_NoEnchantment_NoOp()
    {
        var artifact = SeedArtifact(_bob, "Mishra's Bauble");

        var def = PickYourPoisonFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        var act = () => Run(def, PickYourPoisonFactory.ModeEnchantment);
        act.Should().NotThrow();

        artifact.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Mode 2 — creature with flying
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode2_EachOpponentSacrificesFlyer_NonFlyerSpared()
    {
        var flyer    = SeedCreature(_bob, "Serra Angel", flying: true);
        var grounded = SeedCreature(_bob, "Runeclaw Bear", flying: false);

        var def = PickYourPoisonFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        Run(def, PickYourPoisonFactory.ModeFlyer);

        flyer.Zone.Should().Be(ZoneType.Graveyard, "mode 2 sacrifices a creature with flying");
        grounded.Zone.Should().Be(ZoneType.Battlefield, "the grounded creature does not have flying");
    }

    [Fact]
    public void Mode2_NonCreatureFlyingIrrelevant_OnlyCreaturesEligible()
    {
        // An enchantment / artifact is never a "creature with flying".
        var enchantment = SeedEnchantment(_bob, "Oblivion Ring");

        var def = PickYourPoisonFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        var act = () => Run(def, PickYourPoisonFactory.ModeFlyer);
        act.Should().NotThrow();

        enchantment.Zone.Should().Be(ZoneType.Battlefield, "an enchantment is not a creature with flying");
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Mode2_NoFlyer_NoOp()
    {
        var grounded = SeedCreature(_bob, "Runeclaw Bear", flying: false);

        var def = PickYourPoisonFactory.BuildDefinition(
            _alice, AllPlayers(), agent: null);

        var act = () => Run(def, PickYourPoisonFactory.ModeFlyer);
        act.Should().NotThrow();

        grounded.Zone.Should().Be(ZoneType.Battlefield);
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

    private static Creature SeedCreature(Player owner, string name, bool flying)
    {
        var c = new Creature(name, "{1}{W}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        if (flying)
        {
            // CR 702.9 — Flying keyword marker (read by CombatAbilities.HasFlying).
            c.AddAbility(new KeywordAbility("Flying", c, owner));
        }
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Artifact SeedArtifact(Player owner, string name)
    {
        var a = new Artifact(name, "{1}");
        a.SetOwner(owner);
        a.SetController(owner);
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }

    private static Enchantment SeedEnchantment(Player owner, string name)
    {
        var e = new Enchantment(name, "{2}{W}");
        e.SetOwner(owner);
        e.SetController(owner);
        owner.Zones.Battlefield.AddCard(e);
        e.SetZone(ZoneType.Battlefield);
        return e;
    }
}
