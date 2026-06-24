using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MimeoplasmReveredOneFactory"/>.
///
/// Oracle (Scryfall-verified 2026-06-24):
///   "As Mimeoplasm enters, exile up to X creature cards from your graveyard.
///    It enters with three +1/+1 counters on it for each creature card exiled
///    this way.
///    {2}: Mimeoplasm becomes a copy of target creature card exiled with it,
///    except it's 0/0 and has this ability."
///
/// Covers (the card's UNIQUE behaviour + one identity assert):
/// - Identity: {X}{B}{G}{U}, 0/0, Legendary, Creature — Ooze, BUG colours.
/// - As-enters: exile up to X creature cards from the graveyard, link each to
///   this Mimeoplasm, enter with 3 +1/+1 counters per card exiled.
/// - As-enters "up to X": exiling fewer than X is honoured; an empty graveyard
///   yields zero counters.
/// - {2} copy ability shape: cost {2}, 1..1 "creature card exiled with it"
///   target whose candidates are the linked exile-zone creatures.
/// - Copy resolution: becomes a copy of the chosen exiled creature card, except
///   0/0 — the +1/+1 counters add on top (CR 613.7d) and the copy ability
///   instance survives ("has this ability").
/// </summary>
[Trait("Color", "M")]
public class MimeoplasmReveredOneFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature GraveyardCreature(
        Player owner, string name, int power, int toughness,
        IEnumerable<CardSubtype>? subtypes = null)
    {
        var c = new Creature(name, "{2}{G}", power, toughness, subtypes: subtypes)
            { Owner = owner, Controller = owner };
        c.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(c);
        return c;
    }

    private static TriggeredAbility EnterTrigger(Creature m) =>
        m.Abilities.OfType<TriggeredAbility>().Single();

    private static ActivatedAbility CopyAbility(Creature m) =>
        m.Abilities.OfType<ActivatedAbility>().Single();

    // ------------------------------------------------------------------
    // Identity
    // ------------------------------------------------------------------

    [Fact]
    public void Identity_IsLegendaryOozeCreature_0_0_BUG()
    {
        var m = MimeoplasmReveredOneFactory.Create(_alice);

        m.Name.Should().Be("Mimeoplasm, Revered One");
        m.ManaCost.Should().Be("{X}{B}{G}{U}");
        m.BasePower.Should().Be(0);
        m.BaseToughness.Should().Be(0);
        m.HasType(CardType.Creature).Should().BeTrue();
        m.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        m.Subtypes.Should().Contain(CardSubtype.Ooze);

        var colors = Majik.Core.Cards.CardColors.GetColors(m);
        colors.Should().BeEquivalentTo(new[]
        {
            ManaColor.Black, ManaColor.Green, ManaColor.Blue,
        });
    }

    // ------------------------------------------------------------------
    // As-enters: exile up to X creature cards + counters
    // ------------------------------------------------------------------

    [Fact]
    public async Task Enter_ExilesChosenCreatureCards_EntersWithThreeCountersEach()
    {
        var grizzly = GraveyardCreature(_alice, "Grizzly Bears", 2, 2);
        var elf = GraveyardCreature(_alice, "Llanowar Elves", 1, 1);

        var m = MimeoplasmReveredOneFactory.Create(
            _alice, effects: null, triggers: null, replacements: null, eventBus: null);
        m.SetPendingCastX(2); // cast with X = 2
        m.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(m);

        // Exile BOTH creature cards (2 of 2 allowed).
        var agent = new ScriptedAgent();
        agent.QueueChoice(c => c); // pick the full candidate pool

        await EnterTrigger(m).ResolveAsync(agent, null);

        // Both creature cards exiled + linked to this Mimeoplasm.
        grizzly.Zone.Should().Be(ZoneType.Exile);
        elf.Zone.Should().Be(ZoneType.Exile);
        grizzly.ExiledWith.Should().Be(m.InstanceId);
        elf.ExiledWith.Should().Be(m.InstanceId);

        // 3 counters per creature card exiled = 6.
        m.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(6);
    }

    [Fact]
    public async Task Enter_UpToX_ExilingFewerThanXIsHonoured()
    {
        var grizzly = GraveyardCreature(_alice, "Grizzly Bears", 2, 2);
        GraveyardCreature(_alice, "Llanowar Elves", 1, 1);

        var m = MimeoplasmReveredOneFactory.Create(
            _alice, effects: null, triggers: null, replacements: null, eventBus: null);
        m.SetPendingCastX(5);
        m.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(m);

        // "Up to X" — exile just ONE of the two available.
        var agent = new ScriptedAgent();
        agent.QueueChoice(c => new[] { c[0] });

        await EnterTrigger(m).ResolveAsync(agent, null);

        grizzly.Zone.Should().Be(ZoneType.Exile);
        // Exactly one creature card exiled ⇒ 3 counters.
        m.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);
    }

    [Fact]
    public async Task Enter_EmptyGraveyard_NoCounters()
    {
        var m = MimeoplasmReveredOneFactory.Create(
            _alice, effects: null, triggers: null, replacements: null, eventBus: null);
        m.SetPendingCastX(3);
        m.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(m);

        var agent = new ScriptedAgent();
        agent.QueueChoice(c => c);

        await EnterTrigger(m).ResolveAsync(agent, null);

        m.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    // ------------------------------------------------------------------
    // {2} copy ability
    // ------------------------------------------------------------------

    [Fact]
    public void CopyAbility_Shape_Costs2_TargetsExiledWithCreature()
    {
        var m = MimeoplasmReveredOneFactory.Create(_alice);
        var ability = CopyAbility(m);

        ability.Costs.OfType<ManaCostCost>().Single().Cost.Generic
            .Should().Be(2);
        var req = ability.TargetRequests.Single();
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
    }

    [Fact]
    public void ExiledWithThis_GathersOnlyLinkedExileCreatures()
    {
        var linked = new Creature("Tarmogoyf", "{1}{G}", 0, 1)
            { Owner = _alice, Controller = _alice };
        var unlinked = new Creature("Stranger", "{1}{G}", 2, 2)
            { Owner = _alice, Controller = _alice };

        var m = MimeoplasmReveredOneFactory.Create(_alice);

        // Both in exile, only one linked to this Mimeoplasm.
        foreach (var c in new[] { linked, unlinked })
        {
            c.SetZone(ZoneType.Exile);
            _alice.Zones.Exile.AddCard(c);
        }
        linked.SetExiledWith(m.InstanceId);

        var candidates = MimeoplasmReveredOneFactory.ExiledWithThis(m);
        candidates.Should().ContainSingle().Which.Should().BeSameAs(linked);
    }

    [Fact]
    public void CopyAbility_Resolve_BecomesCopy_Except0_0_CountersAddOnTop()
    {
        var effects = new ContinuousEffectsService();
        var m = MimeoplasmReveredOneFactory.Create(
            _alice, effects, triggers: null, replacements: null, eventBus: null);
        m.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(m);

        // Two +1/+1 counters already on Mimeoplasm (e.g. from a prior enter).
        m.Counters.Add(CounterType.PlusOnePlusOne, 2);

        // A 5/5 creature card exiled with Mimeoplasm.
        var beast = new Creature("Craw Wurm", "{4}{G}{G}", 5, 5,
            subtypes: new[] { CardSubtype.Beast })
            { Owner = _alice, Controller = _alice };
        beast.SetZone(ZoneType.Exile);
        _alice.Zones.Exile.AddCard(beast);
        beast.SetExiledWith(m.InstanceId);

        var ability = CopyAbility(m);
        ability.SetChosenTargets(new[] { new object[] { beast } });
        ability.Resolve();

        // Copied the type line (Beast subtype).
        var chars = effects.Compute(m);
        chars.Subtypes.Should().Contain(CardSubtype.Beast, "copied Craw Wurm's subtype");

        // "Except it's 0/0" — the copied 5/5 is overridden to 0/0, then the two
        // +1/+1 counters add on top (CR 613.7b set-base + 613.7d counters) ⇒ 2/2.
        m.GetPower().Should().Be(2, "0/0 base + 2 counters");
        m.GetToughness().Should().Be(2, "0/0 base + 2 counters");

        // "Has this ability" — the copy ability instance survives the copy.
        m.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {2} copy ability is retained after copying");
    }
}
