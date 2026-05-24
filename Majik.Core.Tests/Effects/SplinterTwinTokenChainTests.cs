using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// Splinter Twin "ability projection" — the famous Twin combo. When the
/// aura's granted "{T}: create token copy" ability is activated, the
/// resulting token is a copy of the bearer per CR 706.2, which means it
/// must inherit the bearer's full ability set INCLUDING the granted
/// activated ability (CR 706.2 — copiable values are fixed at copy time and
/// include "the abilities that the object generates"). Practical
/// consequence: when paired with a self-untap creature (Pestermite,
/// Deceiver Exarch) the chain is infinite.
///
/// These tests use a minimal "Pestermite-like" stand-in (a vanilla Creature
/// the test taps + untaps manually) since neither Pestermite nor Deceiver
/// Exarch ship as factories yet. The combo machinery under test — token
/// ability inheritance + the resulting recursion — is exercised here
/// independently of those cards.
/// </summary>
public class SplinterTwinTokenChainTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public SplinterTwinTokenChainTests()
    {
        _zones = new ZoneService(_bus);
    }

    private Creature AttachedBearerOnBattlefield(string name = "Pestermite")
    {
        // Pestermite-like fixture: 2/1 Faerie with Flash + Flying + an ETB
        // "tap/untap target permanent" trigger. The test only exercises
        // mana-cost-irrelevant copy-ability behavior, so we keep printed
        // abilities minimal — the granted Twin ability is what we assert
        // on after the copy.
        var bearer = new Creature(name, "{2}{U}", 2, 1);
        bearer.SetOwner(_alice);
        bearer.SetController(_alice);
        bearer.AddAbility(new KeywordAbility("Flying", bearer, _alice));
        _zones.MoveCard(bearer, ZoneType.Library, ZoneType.Battlefield, _alice);
        bearer.HasSummoningSickness = false;
        return bearer;
    }

    private Enchantment AttachSplinterTwin(
        Creature bearer,
        TriggerManager? triggers = null)
    {
        var st = SplinterTwinFactory.Create(_alice, _bus, _zones, triggers);
        st.AttachTo(bearer);
        _zones.MoveCard(st, ZoneType.Library, ZoneType.Battlefield, _alice);
        return st;
    }

    // -----------------------------------------------------------------------
    // Copy fidelity: token inherits the granted activated ability
    // -----------------------------------------------------------------------

    [Fact]
    public void TokenCopy_InheritsGrantedActivatedAbility()
    {
        var bearer = AttachedBearerOnBattlefield();
        AttachSplinterTwin(bearer);

        var ability = bearer.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken);

        // CR 706.2 — the token, as a copy of the bearer, must carry the
        // bearer's granted activated ability. v1's lossy CopyEffect dropped
        // activated/triggered abilities; the Splinter Twin combo is
        // unwireable without this projection.
        var tokenActivated = token.Abilities.OfType<ActivatedAbility>().ToList();
        tokenActivated.Should().ContainSingle(
            "the token inherits the bearer's granted '{T}: create token copy' ability");

        var inherited = tokenActivated.Single();
        inherited.Source.Should().BeSameAs(token,
            "CR 602.1b — the copied ability's source is the token (the new bearer)");
        inherited.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Chain: token activates → spawns another token, which also inherits
    // -----------------------------------------------------------------------

    [Fact]
    public void TokenActivatesItsGrantedAbility_SpawnsAnotherTokenCopy()
    {
        var bearer = AttachedBearerOnBattlefield();
        AttachSplinterTwin(bearer);

        // First activation: bearer creates token #1.
        bearer.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        var token1 = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken);

        token1.HasSummoningSickness.Should().BeFalse("token has Haste (CR 702.10b)");

        // Activate the token's inherited ability. Resolve() drives effects
        // only (cost stage lives in AbilityActivationFlow) — same shape as
        // the existing SplinterTwinTests.Activate_* fixtures.
        var tokenAbility = token1.Abilities.OfType<ActivatedAbility>().Single();
        tokenAbility.Resolve();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(2,
            "activating the token's inherited ability spawns a second token");

        var token2 = tokens.Single(t => !ReferenceEquals(t, token1));
        token2.Name.Should().Be(bearer.Name,
            "CR 706.2 — token2 is a copy of its source (token1, which itself is " +
            "a copy of the bearer; copy-of-a-copy carries the bearer's copiable values)");
        token2.IsToken.Should().BeTrue();
        token2.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Haste");
        token2.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "the chain perpetuates — token2 also inherits the granted ability");
    }

    // -----------------------------------------------------------------------
    // EOT cleanup: every spawned token's delayed exile fires at end step
    // -----------------------------------------------------------------------

    [Fact]
    public void EndOfTurn_ExilesEverySpawnedTokenInTheChain()
    {
        var bearer = AttachedBearerOnBattlefield();
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        AttachSplinterTwin(bearer, triggers);

        // First activation — token #1.
        bearer.Abilities.OfType<ActivatedAbility>().Single().Resolve();
        var token1 = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken);

        // Activate token1's inherited ability — token #2.
        token1.Abilities.OfType<ActivatedAbility>().Single().Resolve();
        var token2 = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken && !ReferenceEquals(c, token1));

        // Step into End step — both delayed triggers should fire.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        // CR 603.7 — each delayed end-step exile, registered at its own
        // activation, fires independently. Every token spawned this turn
        // is exiled by the cleanup.
        token1.Zone.Should().Be(ZoneType.Exile);
        token2.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken)
            .Should().BeEmpty("every spawned token exiles at EOT");
    }

    // -----------------------------------------------------------------------
    // Lifecycle independence: aura LTB does NOT revoke the token's grant
    // -----------------------------------------------------------------------

    [Fact]
    public void AuraLTB_DoesNotRevokeTokenInheritedAbility()
    {
        var bearer = AttachedBearerOnBattlefield();
        var aura = AttachSplinterTwin(bearer);

        // Spawn a token while attached.
        bearer.Abilities.OfType<ActivatedAbility>().Single().Resolve();
        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken);

        token.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "sanity: token inherits the granted ability");

        // Aura leaves the battlefield — bearer's grant is revoked by the
        // lifecycle binder.
        _zones.MoveCard(aura, ZoneType.Battlefield, ZoneType.Graveyard);

        bearer.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "bearer's grant is revoked when the aura LTBs (existing behaviour)");

        // CR 706.2 — copiable values are fixed at copy time. The token's
        // ability was inherited as a copy, not granted via the aura's
        // lifecycle binder, so it persists.
        token.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "the token's inherited ability is NOT tied to the aura's lifecycle " +
            "(copied abilities persist independently per CR 706.2)");
    }
}
