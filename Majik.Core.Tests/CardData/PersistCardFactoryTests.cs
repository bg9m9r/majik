using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
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
/// Unit tests for <see cref="PersistCardFactory"/>.
///
/// Persist — Sorcery {2}{B} (Modern Horizons 3):
///   "Return target creature card with mana value 3 or less from your
///    graveyard to the battlefield. It gains haste. Exile it at the
///    beginning of the next end step."
///
/// Note: this is the named-card factory, distinct from the
/// <see cref="Majik.Core.Keywords.PersistFactory"/> keyword-ability
/// helper used by Kitchen Finks / Murderous Redcap / Glen Elendra
/// Archmage.
/// </summary>
public class PersistCardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public PersistCardFactoryTests()
    {
        _zones = new ZoneService(_bus);
    }

    private static ChosenSpellParams ChooseTarget(object? target)
    {
        var targets = target == null
            ? Array.Empty<IReadOnlyList<object>>()
            : new IReadOnlyList<object>[] { new[] { target } };
        return new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: targets,
            Mana: ManaPayment.Empty);
    }

    [Fact]
    public void Persist_Identity()
    {
        var c = PersistCardFactory.Create(_alice);

        c.Name.Should().Be("Persist");
        c.Should().BeOfType<Sorcery>();
        c.ManaCost.Should().Be("{2}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Persist_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Persist", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Persist");
        c.ManaCost.Should().Be("{2}{B}");
    }

    [Fact]
    public void Persist_BuildSpellDefinition_FiltersTargetsByManaValue()
    {
        var smallGuy = AddGraveyardCreature(new Creature("Grizzly Bears", "{1}{G}", 2, 2));      // mv 2
        var exactlyThree = AddGraveyardCreature(new Creature("Hill Giant", "{3}{R}", 3, 3));     // mv 4 — NOT legal
        var atCap = AddGraveyardCreature(new Creature("Watchwolf", "{G}{W}", 3, 3));             // mv 2
        var tooBig = AddGraveyardCreature(new Creature("Serra Angel", "{3}{W}{W}", 4, 4));       // mv 5 — NOT legal

        var spell = PersistCardFactory.BuildSpellDefinition(_alice, _zones, triggers: null);
        var request = spell.TargetRequests.Should().ContainSingle().Subject;

        request.LegalCandidates.Should().BeEquivalentTo(new object[] { smallGuy, atCap },
            "only creature cards in caster's graveyard with mv ≤ 3 are legal targets");
        request.LegalCandidates.Should().NotContain(exactlyThree, "mv 4 > 3 — excluded");
        request.LegalCandidates.Should().NotContain(tooBig, "mv 5 > 3 — excluded");
    }

    [Fact]
    public void Persist_Resolve_ReanimatesTargetWithHaste()
    {
        var continuous = new ContinuousEffectsService();
        var goblin = new Creature("Goblin Guide", "{R}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Graveyard,
            ActiveEffects = continuous,
            HasSummoningSickness = true,
        };
        _alice.Zones.Graveyard.AddCard(goblin);

        var spell = PersistCardFactory.BuildSpellDefinition(_alice, _zones, triggers: null);
        foreach (var fx in spell.EffectFactory(ChooseTarget(goblin)))
        {
            fx.Execute();
        }

        goblin.Zone.Should().Be(ZoneType.Battlefield,
            "the targeted creature card returns to the battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(goblin);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(goblin);
        goblin.Controller.Should().BeSameAs(_alice);

        CombatAbilities.HasHaste(goblin).Should().BeTrue(
            "Persist grants Haste to the reanimated creature (CR 702.10)");
        goblin.HasSummoningSickness.Should().BeFalse(
            "Haste clears summoning sickness (CR 702.10b)");
    }

    [Fact]
    public void Persist_Resolve_NoTarget_IsCleanNoOp()
    {
        var spell = PersistCardFactory.BuildSpellDefinition(_alice, _zones, triggers: null);
        var effects = spell.EffectFactory(ChooseTarget(target: null));

        effects.Should().BeEmpty("no legal target on resolution → CR 608.2b 'spell does nothing'");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Persist_Resolve_RegistersDelayedEndStepExile()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var bloodghast = new Creature("Bloodghast", "{B}{B}", 2, 1)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Graveyard,
        };
        _alice.Zones.Graveyard.AddCard(bloodghast);

        var spell = PersistCardFactory.BuildSpellDefinition(_alice, _zones, triggers);
        foreach (var fx in spell.EffectFactory(ChooseTarget(bloodghast)))
        {
            fx.Execute();
        }

        bloodghast.Zone.Should().Be(ZoneType.Battlefield);

        // Fire the next End step — the delayed trigger queues onto the
        // stack and resolves into Battlefield → Exile.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        bloodghast.Zone.Should().Be(ZoneType.Exile,
            "CR 603.7 — delayed end-step exile (battlefield → exile)");
        _alice.Zones.Exile.GetCards().Should().Contain(bloodghast);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bloodghast);
    }

    [Fact]
    public void Persist_Resolve_EmptyTargetList_NoDelayedTrigger()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var spell = PersistCardFactory.BuildSpellDefinition(_alice, _zones, triggers);
        var effects = spell.EffectFactory(ChooseTarget(target: null));

        // No effect to execute → nothing to register.
        effects.Should().BeEmpty();

        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().Be(0,
            "no target → no delayed exile trigger registered");
    }

    [Fact]
    public void Persist_Resolve_RoutesThroughZoneService_PublishesCardMovedEvent()
    {
        var movedEvents = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Graveyard,
        };
        _alice.Zones.Graveyard.AddCard(bear);

        var spell = PersistCardFactory.BuildSpellDefinition(_alice, _zones, triggers: null);
        foreach (var fx in spell.EffectFactory(ChooseTarget(bear)))
        {
            fx.Execute();
        }

        movedEvents.Should().ContainSingle(
            e => ReferenceEquals(e.Card, bear)
                 && e.FromZone == ZoneType.Graveyard
                 && e.ToZone == ZoneType.Battlefield,
            "graveyard → battlefield routes through ZoneService (CR 603.6a)");
    }

    private TCreature AddGraveyardCreature<TCreature>(TCreature c) where TCreature : Creature
    {
        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);
        return c;
    }
}
