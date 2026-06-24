using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KindledHeroismFactory"/> (Tarkir: Dragonstorm, {R}).
///
/// Kindled Heroism — Instant.
/// Oracle text (verified against Scryfall 2026-06-24):
///   "Target creature gets +1/+0 and gains first strike until end of turn.
///    Scry 1."
///
/// Covers only the card's UNIQUE resolve behaviour (contract test already
/// asserts dispatch + well-formedness):
/// - Identity ({R} Instant) loaded from the embedded JSON def.
/// - Spell definition shape: single 1..1 "target creature" request, no X.
/// - Resolve pumps the target +1/+0 and grants First strike (CR 613.1g /
///   CR 613.1c) until end of turn; both expire on cleanup (CR 514.2).
/// - Resolve scrys 1 unconditionally for the caster (CR 701.20) — default
///   (no agent) sends the peeked card to the bottom; an agent decision is
///   honoured; an empty library no-ops cleanly.
/// - Illegal pump target (creature off the battlefield's effects service is
///   null) → the pump/grant no-ops, but the spell still scrys.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
[Trait("Color", "R")]
public class KindledHeroismFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    public void Dispose() => AgentRegistry.Clear();

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void KindledHeroism_Identity_InstantAtR()
    {
        var card = KindledHeroismFactory.Create(_alice);

        card.Name.Should().Be("Kindled Heroism");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void KindledHeroism_SpellDefinition_HasSingleTargetCreatureRequest_NoX()
    {
        var def = KindledHeroismFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature");
    }

    // ── Pump + first strike ───────────────────────────────────────────────────

    [Fact]
    public void KindledHeroism_Resolve_TargetGetsPlusOnePlusZeroAndFirstStrike()
    {
        var bear = BuildBear(_bob);
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse();

        ExecuteResolve(bear);

        // CR 613.1g Layer 7c — +1/+0; CR 613.1c Layer 6 — First strike.
        bear.GetPower().Should().Be(3, "Kindled Heroism grants +1 power");
        bear.GetToughness().Should().Be(2, "Kindled Heroism grants +0 toughness");
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue(
            "Kindled Heroism grants First strike until end of turn");
    }

    [Fact]
    public void KindledHeroism_PumpAndFirstStrike_ExpireAtEndOfTurn()
    {
        var bear = BuildBear(_bob);
        var svc = bear.ActiveEffects!;

        ExecuteResolve(bear);

        bear.GetPower().Should().Be(3);
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue();

        // CR 514.2 — "until end of turn" effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2);
        bear.GetToughness().Should().Be(2);
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse(
            "First strike grant expires at end of turn");
    }

    // ── Scry 1 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void KindledHeroism_Resolve_ScryOne_DefaultSendsCardToBottom()
    {
        // Library: [a, b, c]. Scry sees [a]; default sends it to bottom.
        var bear = BuildBear(_bob);
        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");
        var c = SeedLibraryCard(_alice, "C");

        ExecuteResolve(bear);

        // CR 701.20 — scry 1; default sends the peeked card to the bottom.
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b, c, a });
    }

    [Fact]
    public void KindledHeroism_Resolve_ScryOne_AgentKeepsCardOnTop()
    {
        var bear = BuildBear(_bob);
        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");
        var c = SeedLibraryCard(_alice, "C");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { a }));
        AgentRegistry.Set(_alice, agent);

        ExecuteResolve(bear);

        _alice.Zones.Library.GetCards().Should().Equal(new[] { a, b, c },
            "the agent kept the peeked card on top");
    }

    [Fact]
    public void KindledHeroism_Resolve_EmptyLibrary_ScryNoOpsCleanly()
    {
        var bear = BuildBear(_bob);
        // Alice's library is empty.

        Action act = () => ExecuteResolve(bear);

        act.Should().NotThrow();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        // The pump/first-strike clause still applied before the (no-op) scry.
        bear.GetPower().Should().Be(3);
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue();
    }

    // ── Illegal pump target ───────────────────────────────────────────────────

    [Fact]
    public void KindledHeroism_TargetWithoutEffectsService_PumpNoOp_StillScrys()
    {
        // Target has no live continuous-effects service (CR 608.2b — the pump
        // body no-ops). The scry clause is unconditional and still happens.
        var inert = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob, ActiveEffects = null };

        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");

        ExecuteResolve(inert);

        inert.GetPower().Should().Be(2, "the pump no-ops without an effects service (CR 608.2b)");
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b, a },
            "the scry clause is unconditional");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ExecuteResolve(Creature target)
    {
        var def = KindledHeroismFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { target } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();
    }

    private static Creature BuildBear(Player owner)
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = owner, Controller = owner, ActiveEffects = new ContinuousEffectsService() };
        bear.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(bear);
        return bear;
    }

    private static Card SeedLibraryCard(Player player, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(player);
        player.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
