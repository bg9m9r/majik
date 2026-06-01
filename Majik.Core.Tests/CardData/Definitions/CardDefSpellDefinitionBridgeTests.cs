using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Tests for <see cref="CardDefRuntime.BuildSpellDefinition"/> — the bridge
/// that compiles a fluent <c>.Resolve(...)</c> body into a full
/// <see cref="SpellDefinition"/> (auto-derived <see cref="TargetRequest"/>s +
/// an <see cref="SpellDefinition.EffectFactory"/> routed onto the shared
/// <see cref="Majik.Core.Primitives.Fx"/> vocabulary). This is the
/// authoring-cost win: a targeted instant / sorcery can be declared in ~10
/// fluent lines with NO bespoke SpellDefinition / EffectFactory.
/// </summary>
public class CardDefSpellDefinitionBridgeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private ChosenSpellParams Chosen(params object[][] targetSlots) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: targetSlots.Select(s => (IReadOnlyList<object>)s).ToArray(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

    // ── Target-request derivation ────────────────────────────────────────────

    [Fact]
    public void Bridge_DealDamageAnyTarget_EmitsSingleAnyTargetRequest()
    {
        CardDef def = CardDef
            .Instant("Shock", "{R}")
            .Resolve(c => c.DealDamage(2).To(TargetKind.AnyTarget));

        var spellDef = CardDefRuntime.BuildSpellDefinition(def, resolver: o => o);

        spellDef.Modes.Should().BeEmpty();
        spellDef.HasVariableX.Should().BeFalse();
        spellDef.TargetRequests.Should().HaveCount(1);
        spellDef.TargetRequests[0].Description.Should().Be("any target");
        spellDef.TargetRequests[0].MinTargets.Should().Be(1);
        spellDef.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void Bridge_MultipleTargetedSteps_EmitsOneRequestPerStepInOrder()
    {
        // "Deal 3 damage to any target, then destroy target creature."
        CardDef def = CardDef
            .Sorcery("Test Multi", "{2}{R}")
            .Resolve(c => c.DealDamage(3).To(TargetKind.AnyTarget)
                           .DestroyTarget(TargetKind.Creature));

        var spellDef = CardDefRuntime.BuildSpellDefinition(def, resolver: o => o);

        spellDef.TargetRequests.Should().HaveCount(2);
        spellDef.TargetRequests[0].Description.Should().Be("any target");
        spellDef.TargetRequests[1].Description.Should().Be("target creature");
    }

    [Fact]
    public void Bridge_UntargetedBody_EmitsNoTargetRequests()
    {
        CardDef def = CardDef
            .Sorcery("Cantrip", "{U}")
            .Resolve(c => c.DrawCards(1));

        var spellDef = CardDefRuntime.BuildSpellDefinition(def, resolver: o => o);

        spellDef.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void Bridge_NoResolveBody_Throws()
    {
        CardDef def = CardDef.Instant("Vanilla", "{R}");

        var act = () => CardDefRuntime.BuildSpellDefinition(def, resolver: o => o);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no Resolve(...) body*");
    }

    // ── Resolution behaviour (the actual authoring win) ──────────────────────

    [Fact]
    public void Bridge_DealDamage_HitsChosenTargetPlayer()
    {
        CardDef def = CardDef
            .Instant("Shock", "{R}")
            .Resolve(c => c.DealDamage(2).To(TargetKind.AnyTarget));

        var spellDef = CardDefRuntime.BuildSpellDefinition(def, resolver: o => o);

        // One target slot: the chosen target is Bob (a player).
        var effects = spellDef.EffectFactory(Chosen(new object[] { _bob }));

        var startLife = _bob.LifeTotal;
        foreach (var e in effects) e.Execute();
        _bob.LifeTotal.Should().Be(startLife - 2, "2 damage to a player == 2 life loss");
    }

    [Fact]
    public void Bridge_DealDamage_PlaneswalkerTarget_RemovesLoyalty()
    {
        // The bridge routes damage through Fx.DealDamageAny so a Planeswalker
        // takes loyalty removal (CR 306.7) — the "any target" resolution shape.
        CardDef def = CardDef
            .Instant("Shock", "{R}")
            .Resolve(c => c.DealDamage(2).To(TargetKind.AnyTarget));

        var spellDef = CardDefRuntime.BuildSpellDefinition(def, resolver: o => o);

        var pw = new Planeswalker("Test Walker", "{3}", startingLoyalty: 5);
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var effects = spellDef.EffectFactory(Chosen(new object[] { pw }));
        foreach (var e in effects) e.Execute();

        pw.Loyalty.Should().Be(3, "2 damage to a planeswalker removes 2 loyalty");
    }

    [Fact]
    public void Bridge_DestroyTargetCreature_MovesItToGraveyard()
    {
        CardDef def = CardDef
            .Sorcery("Murder-lite", "{1}{B}")
            .Resolve(c => c.DestroyTarget(TargetKind.Creature));

        var spellDef = CardDefRuntime.BuildSpellDefinition(def, resolver: o => o);

        var target = new Creature("Bear", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var effects = spellDef.EffectFactory(Chosen(new object[] { target }));
        foreach (var e in effects) e.Execute();

        target.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Bridge_PumpUntilEndOfTurn_RegistersOnChosenCreature()
    {
        CardDef def = CardDef
            .Instant("Giant Growth", "{G}")
            .Resolve(c => c.PumpUntilEndOfTurn(3, 3).To(TargetKind.Creature));

        var spellDef = CardDefRuntime.BuildSpellDefinition(def, resolver: o => o);

        var target = new Creature("Bear", "{1}{G}", 2, 2);
        target.SetOwner(_alice);
        target.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var effects = spellDef.EffectFactory(Chosen(new object[] { target }));
        foreach (var e in effects) e.Execute();

        target.GetPower().Should().Be(5);
        target.GetToughness().Should().Be(5);
    }

    [Fact]
    public void Bridge_Counter_RemovesNoncreatureSpellFromStack()
    {
        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());

        CardDef def = CardDef
            .Instant("Negate", "{1}{U}")
            .Resolve(c => c.Counter(TargetKind.NoncreatureSpell));

        var spellDef = CardDefRuntime.BuildSpellDefinition(def, resolver: o => o, stack: stack);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, effects: Array.Empty<IEffect>());
        stack.Push(spell);
        bolt.SetZone(ZoneType.Stack);

        var effects = spellDef.EffectFactory(Chosen(new object[] { spell }));
        foreach (var e in effects) e.Execute();

        bolt.Zone.Should().Be(ZoneType.Graveyard);
        stack.GetAll().Should().NotContain(spell);
    }

    [Fact]
    public void Bridge_Counter_DoesNotCounterCreatureSpell()
    {
        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());

        CardDef def = CardDef
            .Instant("Negate", "{1}{U}")
            .Resolve(c => c.Counter(TargetKind.NoncreatureSpell));

        var spellDef = CardDefRuntime.BuildSpellDefinition(def, resolver: o => o, stack: stack);

        var goblin = new Creature("Goblin", "{R}", 1, 1);
        goblin.SetOwner(_bob);
        var spell = new Majik.Core.Spells.Spell(goblin, _bob, effects: Array.Empty<IEffect>());
        stack.Push(spell);
        goblin.SetZone(ZoneType.Stack);

        var effects = spellDef.EffectFactory(Chosen(new object[] { spell }));
        foreach (var e in effects) e.Execute();

        goblin.Zone.Should().NotBe(ZoneType.Graveyard,
            "the noncreature filter gates a creature spell at resolution (CR 608.2b)");
    }

    // ── Controller-scoped untargeted steps ───────────────────────────────────

    [Fact]
    public void Bridge_GainLife_RoutesToExplicitController()
    {
        CardDef def = CardDef
            .Instant("Healing Salve", "{W}")
            .Resolve(c => c.GainLife(3));

        var spellDef = CardDefRuntime.BuildSpellDefinition(def, resolver: o => o, controller: _alice);

        var start = _alice.LifeTotal;
        var effects = spellDef.EffectFactory(Chosen());
        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(start + 3);
    }

    [Fact]
    public void Bridge_TargetedAndControllerScoped_BothResolve()
    {
        // "Deal 2 damage to any target. You gain 2 life." (Lightning Helix shape.)
        CardDef def = CardDef
            .Instant("Mini Helix", "{R}{W}")
            .Resolve(c => c.DealDamage(2).To(TargetKind.AnyTarget)
                           .GainLife(2));

        var spellDef = CardDefRuntime.BuildSpellDefinition(def, resolver: o => o, controller: _alice);

        spellDef.TargetRequests.Should().HaveCount(1, "only the damage step is targeted");

        var bobStart = _bob.LifeTotal;
        var aliceStart = _alice.LifeTotal;
        var effects = spellDef.EffectFactory(Chosen(new object[] { _bob }));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobStart - 2);
        _alice.LifeTotal.Should().Be(aliceStart + 2);
    }
}
