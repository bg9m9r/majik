using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="RecrossThePathsFactory"/> — Sorcery {2}{G}.
///
/// "Reveal cards from the top of your library until you reveal a land card.
///  Put that card onto the battlefield and the rest on the bottom of your
///  library in any order. Clash with an opponent. If you win, return Recross
///  the Paths to its owner's hand."
///
/// Covers:
///   - Identity (Sorcery, {2}{G}) + NamedCardFactory dispatch.
///   - Clause 1: reveal-until-land puts the land onto the battlefield and the
///     non-land cards on the bottom of the library.
///   - Clause 2 (CR 701.32): clash with the opponent; a clash WIN stamps the
///     return-to-hand sentinel so the spell goes to its owner's hand on
///     resolution; a clash LOSS leaves it heading to the graveyard.
/// </summary>
public class RecrossThePathsTests : IDisposable
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly SpellCastFlow _flow;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public RecrossThePathsTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
        ZoneServiceRegistry.Set(_alice, _zones);
        ZoneServiceRegistry.Set(_bob, _zones);
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private Sorcery NewRecross()
    {
        var card = RecrossThePathsFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);
        return card;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = RecrossThePathsFactory.Create(_alice);

        card.Name.Should().Be("Recross the Paths");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsRecrossShape()
    {
        var dispatched = NamedCardFactory.Create("Recross the Paths", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Recross the Paths");
    }

    // -----------------------------------------------------------------------
    // Clause 1 — reveal until land onto battlefield, rest on bottom.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Resolve_RevealUntilLand_PutsLandOnBattlefield_RestOnBottom()
    {
        var recross = NewRecross();

        // Library top-down: spell, spell, LAND, deepCard.
        var spell1 = new Instant("Spell One", "{U}") { Owner = _alice };
        var spell2 = new Instant("Spell Two", "{U}") { Owner = _alice };
        var land = new Land("Forest") { Owner = _alice };
        var deep = new Instant("Deep Card", "{U}") { Owner = _alice };
        _alice.Zones.Library.AddCard(spell1); // top
        _alice.Zones.Library.AddCard(spell2);
        _alice.Zones.Library.AddCard(land);
        _alice.Zones.Library.AddCard(deep);   // bottom-most

        // Bob loses the clash trivially (empty library) — irrelevant here.
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var spell = await _flow.CastAsync(_alice, recross,
            RecrossThePathsFactory.BuildSpellDefinition(_alice, recross), agent, Ctx());
        await spell.ResolveAsync(agent, Ctx());

        _alice.Zones.Battlefield.GetCards().Should().Contain(land,
            "the revealed land is put onto the battlefield");

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().NotContain(land);
        // The two non-land cards revealed before the land are now on the bottom,
        // after the cards that were already below the land (deep).
        lib.Should().Contain(spell1).And.Contain(spell2);
        lib.IndexOf(spell1).Should().BeGreaterThan(lib.IndexOf(deep),
            "the revealed non-land cards go on the BOTTOM, below cards that were " +
            "already beneath the revealed land");
    }

    // -----------------------------------------------------------------------
    // Clause 2 — clash WIN returns Recross to its owner's hand (CR 701.32 /
    // CR 608.3 override).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Resolve_ClashWin_ReturnsRecrossToOwnersHand()
    {
        var recross = NewRecross();

        // Alice's library: a high-mv card on top so her clash card beats Bob's.
        var bigClash = new Instant("Big", "{4}{U}") { Owner = _alice }; // mv 5
        _alice.Zones.Library.AddCard(bigClash);
        // Bob's clash card is cheap → Alice wins.
        _bob.Zones.Library.AddCard(new Instant("Tiny", "{U}") { Owner = _bob }); // mv 1

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        AgentRegistry.Set(_bob, new ScriptedAgent());

        var spell = await _flow.CastAsync(_alice, recross,
            RecrossThePathsFactory.BuildSpellDefinition(_alice, recross), agent, Ctx());

        // Drive resolution through the StackResolver so the post-resolution
        // disposition (CR 608.3 override) runs.
        await _resolver.ResolveTopAsync(_stack, _ => agent, Ctx());

        recross.Zone.Should().Be(ZoneType.Hand,
            "CR 701.32 / 608.3 — Alice won the clash, so Recross returns to its " +
            "owner's hand instead of the graveyard");
        _alice.Zones.Hand.GetCards().Should().Contain(recross);
    }

    [Fact]
    public async Task Resolve_ClashLoss_RecrossGoesToGraveyard()
    {
        var recross = NewRecross();

        // Alice reveals a cheap clash card; Bob reveals an expensive one → Alice loses.
        _alice.Zones.Library.AddCard(new Instant("Tiny", "{U}") { Owner = _alice }); // mv 1
        _bob.Zones.Library.AddCard(new Instant("Huge", "{6}{U}") { Owner = _bob });  // mv 7

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        AgentRegistry.Set(_bob, new ScriptedAgent());

        var spell = await _flow.CastAsync(_alice, recross,
            RecrossThePathsFactory.BuildSpellDefinition(_alice, recross), agent, Ctx());

        await _resolver.ResolveTopAsync(_stack, _ => agent, Ctx());

        recross.Zone.Should().Be(ZoneType.Graveyard,
            "CR 608.3 — Alice lost the clash, so Recross goes to the graveyard " +
            "(the default instant/sorcery disposition)");
    }
}
