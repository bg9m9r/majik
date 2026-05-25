using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Instant = Majik.Core.Cards.Instant;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="InquisitionOfKozilekFactory"/> — Sorcery {B}
/// (Rise of the Eldrazi).
///
/// "Target player reveals their hand. You choose a nonland card from it
/// with mana value 3 or less. That player discards that card."
///
/// Covers:
///   - Identity (Sorcery, {B}, owner / controller) + NamedCardFactory dispatch.
///   - SpellDefinition shape: one 1..1 "target player" request, Discard
///     intent, gatherer surfaces every player.
///   - Resolve: reveals every card in the target's hand
///     (<see cref="CardRevealedEvent"/> per card, CR 701.16).
///   - Resolve: respects the mana-value cap (mv ≤ 3); high-mv cards are
///     skipped.
///   - Resolve: skips lands.
///   - Resolve: caster pays no life (Thoughtseize differentiator).
///   - Resolve: no eligible card → no-op (CR 701.16 / 701.16 "if no card
///     can be chosen").
/// </summary>
public class InquisitionOfKozilekFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose()
    {
        // Clean up per-test agent registrations so other tests can't be
        // contaminated by a registered Alice / Bob agent.
        AgentRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = InquisitionOfKozilekFactory.Create(_alice);

        card.Name.Should().Be("Inquisition of Kozilek");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_InquisitionOfKozilek()
    {
        var card = NamedCardFactory.Create("Inquisition of Kozilek", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Inquisition of Kozilek");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresOneTargetPlayerWithDiscardIntent()
    {
        var def = InquisitionOfKozilekFactory.BuildSpellDefinition(
            _alice, t => t, eventBus: null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().ContainSingle();
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("target player");
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Discard);
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    private void Execute(EventBus? bus = null)
    {
        var def = InquisitionOfKozilekFactory.BuildSpellDefinition(_alice, t => t, bus);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    [Fact]
    public void Resolve_RevealsEveryCardInTargetsHandBeforeDiscard()
    {
        var forest = new Land("Forest") { Owner = _bob, Zone = ZoneType.Hand };
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(forest);
        _bob.Zones.Hand.AddCard(bolt);
        _bob.Zones.Hand.AddCard(bear);

        var bus = new EventBus();
        var reveals = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(reveals.Add);

        Execute(bus);

        reveals.Should().HaveCount(3, "every card in the target's hand becomes public per CR 701.16");
        reveals.Should().OnlyContain(r => r.Player == _bob);
        reveals.Should().OnlyContain(r => r.Reason == "Inquisition of Kozilek");
    }

    [Fact]
    public void Resolve_RespectsManaValueCap_DiscardsBoltSkipsCrypticCommand()
    {
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        var cryptic = new Instant("Cryptic Command", "{1}{U}{U}{U}") { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(bolt);
        _bob.Zones.Hand.AddCard(cryptic);

        Execute();

        _bob.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().Be(bolt, "Cryptic Command (mv 4) exceeds the mv-3 cap");
        _bob.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().Be(cryptic);
    }

    [Fact]
    public void Resolve_SkipsLandsEvenWhenManaValueIsZero()
    {
        var forest = new Land("Forest") { Owner = _bob, Zone = ZoneType.Hand };
        var cryptic = new Instant("Cryptic Command", "{1}{U}{U}{U}") { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(forest);
        _bob.Zones.Hand.AddCard(cryptic);

        Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty(
            "Forest is a land (filtered out) and Cryptic Command exceeds the mv cap");
        _bob.Zones.Hand.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_DoesNotCauseCasterLifeLoss()
    {
        // Inquisition's defining differentiator vs. Thoughtseize.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(bolt);

        var startLife = _alice.LifeTotal;

        Execute();

        _alice.LifeTotal.Should().Be(startLife,
            "Inquisition costs the caster no life (unlike Thoughtseize)");
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().Be(bolt);
    }

    [Fact]
    public void Resolve_NoEligibleCard_NoOp()
    {
        // Hand: lands + a mv-4 spell — nothing eligible.
        var forest = new Land("Forest") { Owner = _bob, Zone = ZoneType.Hand };
        var island = new Land("Island") { Owner = _bob, Zone = ZoneType.Hand };
        var cryptic = new Instant("Cryptic Command", "{1}{U}{U}{U}") { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(forest);
        _bob.Zones.Hand.AddCard(island);
        _bob.Zones.Hand.AddCard(cryptic);

        Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Hand.GetCards().Should().HaveCount(3);
    }

    [Fact]
    public void Resolve_AgentPath_ChoosesFromFilteredCandidates()
    {
        // With an explicit agent (DeterministicBotAgent picks first
        // candidate by default), the chosen card still respects the
        // factory's mv / nonland filter — the agent never sees an
        // illegal pick.
        var cryptic = new Instant("Cryptic Command", "{1}{U}{U}{U}") { Owner = _bob, Zone = ZoneType.Hand };
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(cryptic);
        _bob.Zones.Hand.AddCard(bear);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        Execute();

        // Cryptic Command (mv 4) is filtered out; Grizzly Bears (mv 2) is
        // the only legal pick and gets discarded.
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().Be(bear);
        _bob.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().Be(cryptic);
    }
}
