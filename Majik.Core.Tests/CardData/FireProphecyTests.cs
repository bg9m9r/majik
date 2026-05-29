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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Fire Prophecy (Ikoria, {1}{R}, Instant).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "Fire Prophecy deals 3 damage to target creature. You may put a card
///    from your hand on the bottom of your library. If you do, draw a card."
///
/// Covers:
///   - Card identity (Instant, {1}{R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve: 3 damage to the targeted creature (CR 115.4 — single creature
///     target).
///   - "May" rummage rider (CR 121.x — bottom-then-draw):
///     * decline -> hand/library unchanged, no draw.
///     * accept   -> chosen card goes to bottom of library, then a card is
///       drawn from the top.
///     * accept with empty hand -> nothing to bottom, no draw (the "if you do"
///       clause never fires).
///   - Damage hits only a creature target (resolution-time legality, CR 608.2b).
/// </summary>
public class FireProphecyTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FireProphecyTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FireProphecy_IsInstant_At1R()
    {
        var fp = FireProphecyFactory.Create(_alice);

        fp.Name.Should().Be("Fire Prophecy");
        fp.ManaCost.Should().Be("{1}{R}");
        fp.HasType(CardType.Instant).Should().BeTrue();
        fp.Owner.Should().BeSameAs(_alice);
        fp.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FireProphecy()
    {
        var card = NamedCardFactory.Create("Fire Prophecy", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Fire Prophecy");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — 3 damage
    // -----------------------------------------------------------------------

    [Fact]
    public void FireProphecy_Deals3DamageToTargetCreature()
    {
        var target = MakeCreature(_bob, "Tarmogoyf", 4, 5);

        var def = FireProphecyFactory.BuildSpellDefinition(_alice, t => t);
        ResolveDamage(def, target);

        target.Damage.Should().Be(3,
            "Fire Prophecy deals 3 damage to the targeted creature");
    }

    [Fact]
    public void FireProphecy_DoesNotDamageNonCreatureTarget()
    {
        // Resolution-time legality re-check (CR 608.2b): if the resolved token
        // isn't a creature on the battlefield, the damage clause does nothing.
        var def = FireProphecyFactory.BuildSpellDefinition(_alice, t => t);
        var chosen = MakeChosen(new object[] { _bob });
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(20, "Fire Prophecy targets only creatures");
    }

    // -----------------------------------------------------------------------
    // Rummage rider — "may put a card from hand on bottom, then draw"
    // -----------------------------------------------------------------------

    [Fact]
    public void FireProphecy_Decline_LeavesHandAndLibraryUnchanged()
    {
        var target = MakeCreature(_bob, "Tarmogoyf", 4, 5);

        var handCard = new Instant("Opt", "{U}") { Owner = _alice };
        _alice.Zones.Hand.AddCard(handCard);
        var topOfLibrary = new Sorcery("Lava Spike", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(topOfLibrary);

        // Decline the "may" — no bottom, no draw.
        var def = FireProphecyFactory.BuildSpellDefinition(
            _alice, t => t, mayBottom: () => false);
        ResolveDamage(def, target);

        target.Damage.Should().Be(3);
        _alice.Zones.Hand.GetCards().Should().Contain(handCard,
            "declining the rummage leaves the hand untouched");
        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(topOfLibrary,
                "no card was drawn, library unchanged");
    }

    [Fact]
    public void FireProphecy_Accept_BottomsChosenCardThenDraws()
    {
        var target = MakeCreature(_bob, "Tarmogoyf", 4, 5);

        // Hand has one card to bottom; library top is a distinct card to draw.
        var handCard = new Instant("Opt", "{U}") { Owner = _alice };
        _alice.Zones.Hand.AddCard(handCard);
        var topOfLibrary = new Sorcery("Lava Spike", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(topOfLibrary);

        var def = FireProphecyFactory.BuildSpellDefinition(
            _alice, t => t,
            mayBottom: () => true,
            cardChooser: hand => hand.First());
        ResolveDamage(def, target);

        target.Damage.Should().Be(3);

        // handCard went to the bottom of the library; topOfLibrary was drawn
        // into hand. Net hand count is back to 1, but the contents swapped.
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(topOfLibrary,
                "the drawn card is the former top of library");
        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(handCard,
                "the bottomed hand card is now the only card in library");
        handCard.Zone.Should().Be(ZoneType.Library);
        topOfLibrary.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void FireProphecy_Accept_EmptyHand_NoBottomNoDraw()
    {
        var target = MakeCreature(_bob, "Tarmogoyf", 4, 5);

        // Empty hand — "if you do" never fires because nothing was bottomed.
        var topOfLibrary = new Sorcery("Lava Spike", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(topOfLibrary);

        var def = FireProphecyFactory.BuildSpellDefinition(
            _alice, t => t,
            mayBottom: () => true,
            cardChooser: hand => hand.FirstOrDefault());
        ResolveDamage(def, target);

        target.Damage.Should().Be(3);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(topOfLibrary,
                "no card was bottomed, so no draw occurred");
    }

    // -----------------------------------------------------------------------
    // Full cast harness (mirrors FieryImpulseTests)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FireProphecy_CastFromHand_Deals3Damage()
    {
        var target = MakeCreature(_bob, "Tarmogoyf", 4, 5);
        await CastAndResolveTargeting(target);
        target.Damage.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private ChosenSpellParams MakeChosen(object[] target) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { target },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

    private void ResolveDamage(SpellDefinition def, object target)
    {
        var chosen = MakeChosen(new[] { target });
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private static Creature MakeCreature(Player owner, string name, int p, int t)
    {
        var c = new Creature(name, "{1}{G}", p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private async Task CastAndResolveTargeting(object target)
    {
        var fp = FireProphecyFactory.Create(_alice);
        fp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fp);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, fp,
            FireProphecyFactory.BuildSpellDefinition(_alice, t => t),
            agent, ctx);

        fp.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }
}
