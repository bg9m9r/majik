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
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="WearFactory"/> and <see cref="TearFactory"/> — the two
/// halves of the split card Wear // Tear (Dragon's Maze, {1}{R} // {W}).
///
/// Oracle text (verified against Scryfall 2026-06-01):
///   Wear — Instant {1}{R}: "Destroy target artifact.
///     Fuse (You may cast one or both halves of this card from your hand.)"
///   Tear — Instant {W}:    "Destroy target enchantment.
///     Fuse (You may cast one or both halves of this card from your hand.)"
///
/// ## Split-card modelling (CR 712 / CR 709)
/// A split card is a single physical card with two halves; the caster picks a
/// half on cast and casts only that half. The engine's minimal v1 posture (the
/// same as Fire // Ice — see <see cref="FireIceFactoryTests"/>) gives each
/// printed half its own <c>[CardName]</c>-dispatched factory:
///   * "Wear" → <see cref="WearFactory"/> → Instant {1}{R} destroy-artifact half.
///   * "Tear" → <see cref="TearFactory"/> → Instant {W} destroy-enchantment half.
/// The combined seed row "Wear // Tear" flips <c>IsImplemented</c> via the
/// front-face check in <see cref="EmbeddedCardRepository"/> because the front
/// half "Wear" is in the <see cref="ImplementedCardNames"/> registry.
///
/// Fuse (CR 702.102) lets a player cast BOTH halves from hand as one split
/// spell. The engine has no split-cast / fuse cast surface yet (Fire // Ice
/// has the same gap), so v1 models each half independently; the Fuse keyword
/// is informational only. Each half's destroy behaviour is exercised here.
///
/// Covers:
/// - Identity + colour for both halves, loaded from the embedded JSON defs.
/// - <see cref="NamedCardFactory"/> dispatch for both face names.
/// - Both halves carry an <see cref="Majik.Core.CardData.MDFCs.MdfcState"/>
///   face tracker so the OTHER half's name is observable.
/// - Wear — destroy target artifact → graveyard (CR 701.7); no-op on a
///   non-artifact target or a target gone from the battlefield (CR 608.2b).
/// - Tear — destroy target enchantment → graveyard; no-op on a non-enchantment
///   target or a target gone from the battlefield (CR 608.2b).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class WearTearFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    // ── Wear half — identity + dispatch ────────────────────────────────────

    [Fact]
    public void Wear_Identity_InstantAt1R()
    {
        var card = WearFactory.Create(_alice);

        card.Name.Should().Be("Wear");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Wear_IsRed()
    {
        var card = WearFactory.Create(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColorEnum.Red);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Wear()
    {
        var card = NamedCardFactory.Create("Wear", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Wear");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void Wear_CarriesMdfcState_WearFront_TearBack()
    {
        var card = WearFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull("Wear is the front half of the split card");
        card.MdfcState!.FrontFaceName.Should().Be("Wear");
        card.MdfcState!.BackFaceName.Should().Be("Tear");
        card.MdfcState!.IsBackFace.Should().BeFalse();
    }

    // ── Wear half — spell definition shape ─────────────────────────────────

    [Fact]
    public void Wear_SpellDefinition_HasSingleTargetArtifactRequest_NoX()
    {
        var def = WearFactory.BuildDefinition(o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("artifact");
    }

    // ── Wear half — destroy target artifact ────────────────────────────────

    [Fact]
    public void Wear_DestroysArtifact_MovesToGraveyard()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        ResolveWear(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Wear destroys target artifact (CR 701.7)");
    }

    [Fact]
    public void Wear_TargetEnchantment_DoesNothing()
    {
        var enchantment = NewControlledPermanent<Enchantment>(_bob, "Sylvan Library", "{1}{G}");

        ResolveWear(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Battlefield,
            because: "Wear targets artifact only — an enchantment is an illegal target (CR 608.2b)");
    }

    [Fact]
    public void Wear_TargetNotOnBattlefield_DoesNothing()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        _bob.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(artifact);

        ResolveWear(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
    }

    // ── Tear half — identity + dispatch ────────────────────────────────────

    [Fact]
    public void Tear_Identity_InstantAtW()
    {
        var card = TearFactory.Create(_alice);

        card.Name.Should().Be("Tear");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{W}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Tear_IsWhite()
    {
        var card = TearFactory.Create(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColorEnum.White);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Tear()
    {
        var card = NamedCardFactory.Create("Tear", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Tear");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void Tear_CarriesMdfcState_WearFront_TearBack()
    {
        var card = TearFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull("Tear is the back half of the split card");
        card.MdfcState!.FrontFaceName.Should().Be("Wear");
        card.MdfcState!.BackFaceName.Should().Be("Tear");
        card.MdfcState!.IsBackFace.Should().BeTrue("Tear is built pre-flipped to the back half");
    }

    // ── Tear half — spell definition shape ─────────────────────────────────

    [Fact]
    public void Tear_SpellDefinition_HasSingleTargetEnchantmentRequest_NoX()
    {
        var def = TearFactory.BuildDefinition(o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("enchantment");
    }

    // ── Tear half — destroy target enchantment ─────────────────────────────

    [Fact]
    public void Tear_DestroysEnchantment_MovesToGraveyard()
    {
        var enchantment = NewControlledPermanent<Enchantment>(_bob, "Sylvan Library", "{1}{G}");

        ResolveTear(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            because: "Tear destroys target enchantment (CR 701.7)");
    }

    [Fact]
    public void Tear_TargetArtifact_DoesNothing()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        ResolveTear(artifact);

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            because: "Tear targets enchantment only — an artifact is an illegal target (CR 608.2b)");
    }

    [Fact]
    public void Tear_TargetNotOnBattlefield_DoesNothing()
    {
        var enchantment = NewControlledPermanent<Enchantment>(_bob, "Sylvan Library", "{1}{G}");

        _bob.Zones.Battlefield.RemoveCard(enchantment);
        enchantment.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(enchantment);

        ResolveTear(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void ResolveWear(ICard target)
    {
        var def = WearFactory.BuildDefinition(o => o);
        ResolveDefinition(def, target);
    }

    private static void ResolveTear(ICard target)
    {
        var def = TearFactory.BuildDefinition(o => o);
        ResolveDefinition(def, target);
    }

    private static void ResolveDefinition(SpellDefinition def, ICard target)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static T NewControlledPermanent<T>(Player owner, string name, string cost)
        where T : ICard
    {
        T card;
        if (typeof(T) == typeof(Artifact))
        {
            card = (T)(ICard)new Artifact(name, cost);
        }
        else if (typeof(T) == typeof(Enchantment))
        {
            card = (T)(ICard)new Enchantment(name, cost);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported type {typeof(T)}");
        }

        ((Card)(ICard)card).SetOwner(owner);
        ((Card)(ICard)card).SetController(owner);
        ((Card)(ICard)card).SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }
}
