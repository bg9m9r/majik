using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="InfernalGraspFactory"/> (Innistrad: Midnight
/// Hunt, {1}{B}).
///
/// Infernal Grasp — Instant.
/// Oracle text (verified against Scryfall):
///   "Destroy target creature. You lose 2 life."
///
/// Covers:
/// - Identity ({1}{B} Instant, name, owner/controller) loaded from the
///   embedded JSON def via <see cref="CardDefinitionLoader"/>.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: single 1..1 "target creature" request, no X.
/// - Resolve destroys ANY target creature (CR 701.7) — unlike Doom Blade,
///   colour is irrelevant.
/// - Resolve makes the caster lose 2 life (CR 119.3) regardless of whether
///   the destroy actually moved a permanent (the life loss is not conditional
///   on a legal target — CR 608.2c, both clauses are independent).
/// - Off-battlefield target → destroy no-ops, but the caster STILL loses 2
///   life (CR 608.2 — the "You lose 2 life" clause does not target).
/// </summary>
[Trait("Color", "B")]
public class InfernalGraspFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void InfernalGrasp_Identity_InstantAt1B()
    {
        var card = InfernalGraspFactory.Create(_alice);

        card.Name.Should().Be("Infernal Grasp");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void InfernalGrasp_SpellDefinition_HasSingleTargetCreatureRequest_NoX()
    {
        var def = InfernalGraspFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Destroy ───────────────────────────────────────────────────────────────

    [Fact]
    public void InfernalGrasp_DestroysAnyCreature_AndCasterLosesTwoLife()
    {
        // Black creature — Infernal Grasp destroys it regardless of colour
        // (unlike Doom Blade's nonblack filter).
        var imp = NewControlledCreature(_bob, "Putrid Imp", "{B}");

        Resolve(imp);

        imp.Zone.Should().Be(ZoneType.Graveyard,
            "Infernal Grasp destroys target creature regardless of colour (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(imp);
        _alice.LifeTotal.Should().Be(18, "the caster loses 2 life (CR 119.3)");
    }

    [Fact]
    public void InfernalGrasp_DestroysColorlessCreature()
    {
        var eldrazi = NewControlledCreature(_bob, "Eldrazi Mimic", "{2}");

        Resolve(eldrazi);

        eldrazi.Zone.Should().Be(ZoneType.Graveyard);
        _alice.LifeTotal.Should().Be(18);
    }

    // ── Life loss is independent of the destroy ───────────────────────────────

    [Fact]
    public void InfernalGrasp_TargetNotOnBattlefield_DestroyNoOps_ButCasterStillLosesTwoLife()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature);

        // CR 608.2b — illegal destroy target → that clause does nothing.
        creature.Zone.Should().Be(ZoneType.Graveyard);
        // The "You lose 2 life" clause does not target (CR 608.2) — it still
        // resolves even when the destroy clause is moot.
        _alice.LifeTotal.Should().Be(18,
            "the life loss does not target, so it resolves even when the creature is gone");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Resolve(object targetToken)
    {
        var def = InfernalGraspFactory.BuildSpellDefinition(_alice, resolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { targetToken } },
            Mana:      ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
