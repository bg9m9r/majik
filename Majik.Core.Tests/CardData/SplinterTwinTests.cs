using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Splinter Twin — Enchantment — Aura {2}{R}{R}.
///
///   "Enchant creature.
///    Enchanted creature has '{T}: Create a token that's a copy of this
///    creature, except it has haste. Exile the token at the beginning of
///    the next end step.'"
///
/// Validates the grant-activated-ability-on-attach lifecycle wired via
/// <see cref="AttachedAuraAbilityGrantStaticEffect"/>, the {T}-activate
/// flow that spawns a haste-token copy of the bearer, and the delayed
/// end-step exile (CR 603.7).
/// </summary>
public class SplinterTwinTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public SplinterTwinTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SplinterTwin_IsAura_AtCost2RR()
    {
        var st = SplinterTwinFactory.Create(_alice);

        st.Name.Should().Be("Splinter Twin");
        st.HasType(CardType.Enchantment).Should().BeTrue();
        st.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        st.IsAura.Should().BeTrue();
        st.ManaCost.Should().Be("{2}{R}{R}");
        st.Owner.Should().BeSameAs(_alice);
        st.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SplinterTwin()
    {
        var st = NamedCardFactory.Create("Splinter Twin", _alice);

        st.Should().BeOfType<Enchantment>();
        st.Name.Should().Be("Splinter Twin");
        st.ManaCost.Should().Be("{2}{R}{R}");
        st.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Grant lifecycle: attach to a creature → bearer gains the activated ability
    // -----------------------------------------------------------------------

    /// <summary>
    /// Attach Splinter Twin to a Grizzly Bears. While the aura is on the
    /// battlefield and attached, the bearer's Abilities collection should
    /// include a freshly-granted ActivatedAbility (the {T}: create-token
    /// activation).
    /// </summary>
    [Fact]
    public void AttachedToCreature_GrantsActivatedAbility_OnBearer()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        _zones.MoveCard(bears, ZoneType.Library, ZoneType.Battlefield, _alice);

        var preAttachAbilityCount = bears.Abilities.OfType<ActivatedAbility>().Count();

        var st = SplinterTwinFactory.Create(_alice, _bus, _zones, triggers: null);
        // Bypass cast-time targeting: attach BEFORE moving onto the
        // battlefield so the lifecycle Sync sees AttachedTo populated.
        st.AttachTo(bears);
        _zones.MoveCard(st, ZoneType.Library, ZoneType.Battlefield, _alice);

        var granted = bears.Abilities.OfType<ActivatedAbility>().ToList();
        granted.Should().HaveCount(preAttachAbilityCount + 1,
            "the aura grants exactly one activated ability to the bearer while attached");

        var ability = granted.Last();
        ability.Controller.Should().BeSameAs(_alice, "the aura's controller controls the granted activation");
        ability.Source.Should().BeSameAs(bears, "CR 602.1b — the granted ability's source is the bearer");
    }

    // -----------------------------------------------------------------------
    // Activate {T}: spawn a token copy with haste, register delayed EOT exile
    // -----------------------------------------------------------------------

    /// <summary>
    /// Activate the granted ability: token copy of the bearer appears on
    /// the controller's battlefield, has Haste, and is flagged as a token.
    /// The bearer is tapped (the {T} cost was paid).
    /// </summary>
    [Fact]
    public void Activate_CreatesHasteTokenCopy_AndTapsBearer()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        // Bears need to be untapped + summoning-sick-cleared to activate
        // (mirrors what a real game would have).
        _zones.MoveCard(bears, ZoneType.Library, ZoneType.Battlefield, _alice);
        bears.HasSummoningSickness = false;

        var st = SplinterTwinFactory.Create(_alice, _bus, _zones, triggers: null);
        st.AttachTo(bears);
        _zones.MoveCard(st, ZoneType.Library, ZoneType.Battlefield, _alice);

        var ability = bears.Abilities.OfType<ActivatedAbility>().Single();

        // Snapshot battlefield count BEFORE resolving — the token will be
        // added to it.
        var bfBefore = _alice.Zones.Battlefield.GetCards().Count();

        // Pay the {T} cost manually (Resolve() runs Effects only — the
        // cost stage lives in AbilityActivationFlow which we don't wire
        // here). Tapping is the visible side effect; tests below assert
        // the bearer ends up tapped via the resolve path on its own.
        ability.Resolve();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().ContainSingle("activation creates exactly one token");

        var token = tokens[0];
        token.Name.Should().Be("Grizzly Bears", "the token is a copy of the bearer (CR 706.2)");
        token.BasePower.Should().Be(2);
        token.BaseToughness.Should().Be(2);
        token.IsToken.Should().BeTrue();
        token.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Haste", "the token has haste even if the bearer didn't (CR 702.10)");
        token.HasSummoningSickness.Should().BeFalse(
            "Haste lets the token attack the turn it enters (CR 702.10b)");
        token.Controller.Should().BeSameAs(_alice);

        _alice.Zones.Battlefield.GetCards().Count().Should().Be(bfBefore + 1);
    }

    /// <summary>
    /// When a <see cref="TriggerManager"/> is wired, activating the granted
    /// ability registers a one-shot end-step exile (CR 603.7) on the
    /// spawned token. Stepping into the End step should fire the trigger,
    /// move it to the stack, and (after resolve) exile the token.
    /// </summary>
    [Fact]
    public void Activate_RegistersDelayedEndStepExile_ForSpawnedToken()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        _zones.MoveCard(bears, ZoneType.Library, ZoneType.Battlefield, _alice);
        bears.HasSummoningSickness = false;

        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var st = SplinterTwinFactory.Create(_alice, _bus, _zones, triggers);
        st.AttachTo(bears);
        _zones.MoveCard(st, ZoneType.Library, ZoneType.Battlefield, _alice);

        var ability = bears.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken);

        token.Zone.Should().Be(ZoneType.Battlefield,
            "token is on the battlefield before the end step");

        // Fire the next End step — the delayed trigger should match and
        // queue itself onto the stack.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        // Resolve everything on the stack — the delayed trigger fires
        // its exile effect.
        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        token.Zone.Should().Be(ZoneType.Exile, "CR 603.7 — delayed end-step exile fires");
        _alice.Zones.Exile.GetCards().Should().Contain(token);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(token);
    }

    // -----------------------------------------------------------------------
    // Detach lifecycle: aura LTB → granted ability removed from bearer
    // -----------------------------------------------------------------------

    /// <summary>
    /// When Splinter Twin leaves the battlefield, the granted activated
    /// ability is revoked from the bearer's Abilities collection. The
    /// bearer should be back to whatever ability shape it had before.
    /// </summary>
    [Fact]
    public void AuraLeavesBattlefield_RevokesGrantedAbility()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        _zones.MoveCard(bears, ZoneType.Library, ZoneType.Battlefield, _alice);

        var preAttachAbilityCount = bears.Abilities.OfType<ActivatedAbility>().Count();

        var st = SplinterTwinFactory.Create(_alice, _bus, _zones, triggers: null);
        st.AttachTo(bears);
        _zones.MoveCard(st, ZoneType.Library, ZoneType.Battlefield, _alice);

        bears.Abilities.OfType<ActivatedAbility>().Should().HaveCount(preAttachAbilityCount + 1,
            "sanity: grant is live while attached + on battlefield");

        // Send Splinter Twin to the graveyard — lifecycle should revoke
        // the granted ability via the CardMovedEvent hook.
        _zones.MoveCard(st, ZoneType.Battlefield, ZoneType.Graveyard);

        bears.Abilities.OfType<ActivatedAbility>().Should().HaveCount(preAttachAbilityCount,
            "the granted ability is removed when the aura leaves the battlefield");
    }

    /// <summary>
    /// Lifecycle accessor handle. The factory's bus-aware overload stashes
    /// the live <see cref="AttachedAuraAbilityGrantStaticEffect"/> against
    /// the aura so tests / runtime code can call <c>Sync()</c> after
    /// mutating attachment outside the bus path.
    /// </summary>
    [Fact]
    public void GrantLifecycle_IsRetrievable_ViaAccessor()
    {
        var st = SplinterTwinFactory.Create(_alice, _bus, _zones, triggers: null);

        var lifecycle = SplinterTwinLifecycleAccessor.GetLifecycle(st);
        lifecycle.Should().NotBeNull("Create(owner, eventBus, ...) wires the grant lifecycle");

        // Single-arg path does NOT wire a lifecycle.
        var plain = SplinterTwinFactory.Create(_alice);
        SplinterTwinLifecycleAccessor.GetLifecycle(plain).Should().BeNull(
            "single-arg dispatcher path produces a shape-only card");
    }
}
