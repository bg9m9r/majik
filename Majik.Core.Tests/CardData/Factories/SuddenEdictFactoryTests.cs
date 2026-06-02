using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for <see cref="SuddenEdictFactory"/>.
///
/// Card: Sudden Edict — Instant {1}{B} (Modern Horizons 3).
///   "Split second (As long as this spell is on the stack, players can't cast
///    spells or activate abilities that aren't mana abilities.)
///    Target player sacrifices a creature of their choice."
///
/// CR 702.61 — Split second declared as a keyword marker (mirrors
/// <see cref="ExtirpateFactory"/>).
/// CR 701.16 — "sacrifice" bypasses Indestructible / regeneration and moves
/// the permanent from the battlefield to its owner's graveyard. The resolve
/// body mirrors <see cref="DiabolicEdictFactory"/> (single target player; the
/// target player sacrifices a creature of their choice).
///
/// Covers:
///   - Identity: {1}{B} black Instant carrying the Split second marker.
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single target-player request declared in the SpellDefinition.
///   - Target player with one creature → that creature goes to graveyard.
///   - Target player with multiple creatures + agent → agent-chosen one
///     goes to graveyard; others stay.
///   - Target player with no creatures → no-op.
///   - Illegal / non-Player target (CR 608.2b) → no effect.
/// </summary>
[Trait("Color", "B")]
public class SuddenEdictFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SuddenEdict_Identity()
    {
        var card = SuddenEdictFactory.Create(_alice);

        card.Name.Should().Be("Sudden Edict");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SuddenEdict_CarriesSplitSecondMarker()
    {
        var card = SuddenEdictFactory.Create(_alice);

        // CR 702.61 — Split second declared as a keyword marker, exactly as
        // ExtirpateFactory does. The priority manager consults the marker once
        // split-second restriction enforcement lands.
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Split second");
    }
    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_HasSingleTargetPlayerRequest()
    {
        var def = SuddenEdictFactory.BuildSpellDefinition(
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

        var def = SuddenEdictFactory.BuildSpellDefinition(
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

        var agent = new ScriptedAgent();
        agent.QueueFromBattlefield(candidates => candidates.First(c => c.Name == "Tarmogoyf"));

        var def = SuddenEdictFactory.BuildSpellDefinition(
            resolver: o => o!,
            agent: agent);

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        goyf.Zone.Should().Be(ZoneType.Graveyard, "agent chose Tarmogoyf");
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(goyf);

        bear.Zone.Should().Be(ZoneType.Battlefield);
        angel.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_AgentReturnsNull_FallsBackToFirstCreature()
    {
        var bear = SeedBattlefieldCreature(_bob, "Runeclaw Bear");
        var wolf = SeedBattlefieldCreature(_bob, "Garruk's Companion");

        var agent = new ScriptedAgent();
        agent.QueueFromBattlefield((ICard?)null);

        var def = SuddenEdictFactory.BuildSpellDefinition(
            resolver: o => o!,
            agent: agent);

        var effects = def.EffectFactory(MakeChosen(_bob));
        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard);
        wolf.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Resolve — no creatures on the battlefield (no-op)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_TargetPlayerNoCreatures_NoOp()
    {
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();

        var def = SuddenEdictFactory.BuildSpellDefinition(
            resolver: o => o!,
            agent: null);

        var effects = def.EffectFactory(MakeChosen(_bob));

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
        var def = SuddenEdictFactory.BuildSpellDefinition(
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
