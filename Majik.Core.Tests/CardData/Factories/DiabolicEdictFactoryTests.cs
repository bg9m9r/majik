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
/// Tests for <see cref="DiabolicEdictFactory"/>.
///
/// Card: Diabolic Edict — Instant {1}{B} (Tempest et al.).
///   "Target player sacrifices a creature of their choice."
///
/// CR 701.16 — "sacrifice" bypasses Indestructible / regeneration and
/// moves the permanent from the battlefield to its owner's graveyard.
///
/// Covers:
///   - Identity: {1}{B} black Instant.
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single target-player request declared in the SpellDefinition.
///   - Target player with one creature → that creature goes to graveyard.
///   - Target player with multiple creatures + agent → agent-chosen one
///     goes to graveyard; others stay.
///   - Target player with no creatures → no-op (the edict is a no-op
///     when the target controls no creatures, per printed text).
///   - Illegal / non-Player target (CR 608.2b) → no effect.
/// </summary>
[Trait("Color", "B")]
public class DiabolicEdictFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DiabolicEdict_Identity()
    {
        var card = DiabolicEdictFactory.Create(_alice);

        card.Name.Should().Be("Diabolic Edict");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_HasSingleTargetPlayerRequest()
    {
        var def = DiabolicEdictFactory.BuildSpellDefinition(
            resolver: o => o!,
            agent: null);

        def.TargetRequests.Should().ContainSingle();
        var req = def.TargetRequests[0];
        req.Description.Should().Contain("player", because: "edict targets a player, not a creature");
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Resolve — one creature on the battlefield
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_TargetPlayerOneCreature_CreatureGoesToGraveyard()
    {
        var bear = SeedBattlefieldCreature(_bob, "Runeclaw Bear");

        var def = DiabolicEdictFactory.BuildSpellDefinition(
            resolver: o => o!,
            agent: null);

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(bear);
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve — multiple creatures, agent-driven pick
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_TargetPlayerMultipleCreatures_AgentPickedOneGoesToGraveyard()
    {
        var bear  = SeedBattlefieldCreature(_bob, "Runeclaw Bear");
        var goyf  = SeedBattlefieldCreature(_bob, "Tarmogoyf");
        var angel = SeedBattlefieldCreature(_bob, "Serra Angel");

        // Agent picks Tarmogoyf (the "best" blocker).
        var agent = new ScriptedAgent();
        agent.QueueFromBattlefield(candidates => candidates.First(c => c.Name == "Tarmogoyf"));

        var def = DiabolicEdictFactory.BuildSpellDefinition(
            resolver: o => o!,
            agent: agent);

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        // Exactly one card sacrificed: the agent's pick.
        goyf.Zone.Should().Be(ZoneType.Graveyard, "agent chose Tarmogoyf");
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(goyf);

        // The other two remain on the battlefield.
        bear.Zone.Should().Be(ZoneType.Battlefield);
        angel.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_AgentReturnsNull_FallsBackToFirstCreature()
    {
        var bear = SeedBattlefieldCreature(_bob, "Runeclaw Bear");
        var wolf = SeedBattlefieldCreature(_bob, "Garruk's Companion");

        var agent = new ScriptedAgent();
        agent.QueueFromBattlefield((ICard?)null); // agent declines

        var def = DiabolicEdictFactory.BuildSpellDefinition(
            resolver: o => o!,
            agent: agent);

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        // Deterministic fallback: first creature in battlefield order.
        bear.Zone.Should().Be(ZoneType.Graveyard);
        wolf.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Resolve — no creatures on the battlefield (no-op)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_TargetPlayerNoCreatures_NoOp()
    {
        // Bob has no creatures; edict resolves but effects nothing.
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();

        var def = DiabolicEdictFactory.BuildSpellDefinition(
            resolver: o => o!,
            agent: null);

        var effects = def.EffectFactory(MakeChosen(_bob));

        // Should not throw; graveyard stays empty.
        var act = () => { foreach (var e in effects) e.Execute(); };
        act.Should().NotThrow();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal target (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_IllegalTarget_NoEffect()
    {
        // Resolver returns a non-Player object → single-target spell
        // fizzles per CR 608.2b.
        var def = DiabolicEdictFactory.BuildSpellDefinition(
            resolver: _ => "not-a-player",
            agent: null);

        var act = () =>
        {
            var effects = def.EffectFactory(MakeChosen(_bob));
            foreach (var e in effects) e.Execute();
        };
        act.Should().NotThrow("fizzled spell does nothing — no exception");

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature SeedBattlefieldCreature(Player owner, string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static ChosenSpellParams MakeChosen(Player targetPlayer) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetPlayer } },
            Mana: ManaPayment.Empty);
}
