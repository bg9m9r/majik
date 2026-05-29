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
/// Tests for <see cref="FireFactory"/> and <see cref="IceFactory"/> — the two
/// halves of the split card Fire // Ice (Apocalypse / various reprints,
/// {1}{R} // {1}{U}).
///
/// Oracle text (verified against Scryfall):
///   Fire — Instant {1}{R}: "Fire deals 2 damage divided as you choose among
///     one or two targets."
///   Ice  — Instant {1}{U}: "Tap target permanent.\nDraw a card."
///
/// ## Split-card modelling (CR 712 / CR 709)
/// A split card is a single physical card with two halves; the caster picks a
/// half on cast and casts only that half. The engine's minimal posture (same
/// as the Sink into Stupor // Soporific Springs MDFC) gives each printed half
/// its own <c>[CardName]</c>-dispatched factory:
///   * "Fire" → <see cref="FireFactory"/> → Instant {1}{R} damage half.
///   * "Ice"  → <see cref="IceFactory"/>  → Instant {1}{U} tap + draw half.
/// The combined seed row "Fire // Ice" flips <c>IsImplemented</c> via the
/// front-face check in <see cref="EmbeddedCardRepository"/> because the front
/// half "Fire" is in the <see cref="ImplementedCardNames"/> registry.
///
/// Covers:
/// - Identity + colour for both halves, loaded from the embedded JSON defs.
/// - <see cref="NamedCardFactory"/> dispatch for both face names.
/// - Both halves carry an <see cref="Majik.Core.CardData.MDFCs.MdfcState"/>
///   face tracker so the OTHER half's name is observable.
/// - Fire — divided 2 damage: one target (all 2), two targets (default 1+1,
///   caller-supplied skew), illegal-target re-check (CR 608.2b), all-illegal
///   fizzle.
/// - Ice — tap target permanent (CR 701.27) then the caster draws a card
///   (CR 121.1); illegal-target re-check (still draws — the draw is not gated
///   on the tap, CR 608.2c).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class FireIceFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    // ── Fire half — identity + dispatch ───────────────────────────────────

    [Fact]
    public void Fire_Identity_InstantAt1R()
    {
        var card = FireFactory.Create(_alice);

        card.Name.Should().Be("Fire");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Fire_IsRed()
    {
        var card = FireFactory.Create(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColorEnum.Red);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Fire()
    {
        var card = NamedCardFactory.Create("Fire", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Fire");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void Fire_CarriesMdfcState_FireFront_IceBack()
    {
        var card = FireFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull("Fire is the front half of the split card");
        card.MdfcState!.FrontFaceName.Should().Be("Fire");
        card.MdfcState!.BackFaceName.Should().Be("Ice");
        card.MdfcState!.IsBackFace.Should().BeFalse();
    }

    // ── Fire half — spell definition shape ─────────────────────────────────

    [Fact]
    public void Fire_SpellDefinition_HasOneToTwoTargetRequest_NoX()
    {
        var def = FireFactory.BuildSpellDefinition(resolver: x => x);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(2);
    }

    // ── Fire half — divided damage ─────────────────────────────────────────

    [Fact]
    public void Fire_OneTarget_DealsAll2Damage()
    {
        var def = FireFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(18, "all 2 damage lands on the single target");
    }

    [Fact]
    public void Fire_TwoTargets_DefaultSplit_OneEach()
    {
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = FireFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob, creature } }, ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(19, "default split is 1 damage to each of two targets (CR 119.4)");
        creature.Damage.Should().Be(1, "the other 1 damage lands on the creature");
    }

    [Fact]
    public void Fire_TwoTargets_CallerSkewedSplit_Honoured()
    {
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        // Caller assigns all 2 to the creature, 0 to the player.
        var def = FireFactory.BuildSpellDefinition(
            resolver: x => x,
            distribute: legal => new Dictionary<object, int>
            {
                [creature] = 2,
                [_bob] = 0,
            });
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob, creature } }, ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        creature.Damage.Should().Be(2, "caller allocated both damage to the creature");
        _bob.LifeTotal.Should().Be(20, "player took 0 damage");
    }

    [Fact]
    public void Fire_AllTargetsIllegal_Fizzles_NoDamage()
    {
        // Resolver maps the chosen token to a non-damageable object → illegal.
        var def = FireFactory.BuildSpellDefinition(resolver: _ => new object());
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(20, "no legal target → spell deals no damage (CR 608.2b)");
    }

    // ── Ice half — identity + dispatch ─────────────────────────────────────

    [Fact]
    public void Ice_Identity_InstantAt1U()
    {
        var card = IceFactory.Create(_alice);

        card.Name.Should().Be("Ice");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{U}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Ice_IsBlue()
    {
        var card = IceFactory.Create(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColorEnum.Blue);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Ice()
    {
        var card = NamedCardFactory.Create("Ice", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Ice");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void Ice_CarriesMdfcState_FireFront_IceBack()
    {
        var card = IceFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull("Ice is the back half of the split card");
        card.MdfcState!.FrontFaceName.Should().Be("Fire");
        card.MdfcState!.BackFaceName.Should().Be("Ice");
        card.MdfcState!.IsBackFace.Should().BeTrue("Ice is built pre-flipped to the back half");
    }

    // ── Ice half — spell definition shape ──────────────────────────────────

    [Fact]
    public void Ice_SpellDefinition_HasSingleTargetPermanentRequest_NoX()
    {
        var def = IceFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ── Ice half — tap + draw ──────────────────────────────────────────────

    [Fact]
    public void Ice_TapsTargetPermanent_ThenCasterDraws()
    {
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var topCard = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(topCard);

        var def = IceFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { creature } }, ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        creature.IsTapped.Should().BeTrue("Ice taps the target permanent (CR 701.27)");
        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "the caster draws a card (CR 121.1)");
        _alice.Zones.Library.GetCards().Should().NotContain(topCard);
    }

    [Fact]
    public void Ice_TapsLandPermanent()
    {
        var land = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain })
        {
            Owner = _bob,
            Controller = _bob,
        };
        land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        var topCard = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(topCard);

        var def = IceFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { land } }, ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        land.IsTapped.Should().BeTrue("Ice can tap ANY permanent, including a land (CR 701.27)");
        _alice.Zones.Hand.GetCards().Should().Contain(topCard);
    }

    [Fact]
    public void Ice_IllegalTarget_StillDraws()
    {
        // CR 608.2c — "Draw a card" is a separate, untargeted instruction; it
        // still happens even if the tap target became illegal at resolution.
        var topCard = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(topCard);

        var def = IceFactory.BuildSpellDefinition(_alice, resolver: _ => new object());
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "the untargeted draw resolves regardless of the tap target's legality");
    }

    [Fact]
    public void Ice_EmptyLibrary_DrawMarksTriedToDraw()
    {
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = IceFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { creature } }, ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        creature.IsTapped.Should().BeTrue();
        // Empty-library draw flags the SBA loss rather than throwing (CR 704.5b).
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
