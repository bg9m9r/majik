using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WitheringTormentFactory"/> (Murders at Karlov
/// Manor, {2}{B}). "Destroy target creature or enchantment. You lose 2 life."
/// </summary>
[Trait("Color", "B")]
public class WitheringTormentFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ChosenSpellParams Chosen(object target) =>
        new(ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

    // ── Identity ────────────────────────────────────────────────────────────

    [Fact]
    public void Identity_InstantAt2B_BlackColoured()
    {
        var card = WitheringTormentFactory.Create(_alice);

        card.Name.Should().Be("Withering Torment");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{2}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // ── SpellDefinition shape ────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_SingleCreatureOrEnchantmentTargetRequest()
    {
        var def = WitheringTormentFactory.BuildDefinition(_alice, o => o);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature or enchantment");
    }

    [Fact]
    public void Gatherer_OffersCreaturesAndEnchantments_AnyController()
    {
        // Unlike Feed the Swarm, Withering Torment is NOT opponent-restricted:
        // the caster's own permanents are legal targets too.
        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        aliceBear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceBear);

        var bobOgre = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            Owner = _bob,
            Controller = _bob,
        };
        bobOgre.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobOgre);

        var bobAura = new Enchantment("Pacifism", "{1}{W}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        bobAura.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobAura);

        var def = WitheringTormentFactory.BuildDefinition(_alice, o => o);
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, new Majik.Core.Stack.Stack());
        var candidates = def.TargetRequests[0].CandidateGatherer!(ctx);

        candidates.Should().Contain(bobOgre);
        candidates.Should().Contain(bobAura);
        candidates.Should().Contain(aliceBear,
            "Withering Torment can target any creature or enchantment, including the caster's own");
    }

    // ── Happy path: opponent creature — flat 2 life loss (NOT mana value) ─────

    [Fact]
    public void Resolve_OpponentCreature_MovesToGraveyard_CasterLosesFixed2Life()
    {
        // Opponent creature with mana value 4 — life loss is still a flat 2.
        var ogre = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            Owner = _bob,
            Controller = _bob,
        };
        ogre.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(ogre);

        var def = WitheringTormentFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(ogre))) e.Execute();

        ogre.Zone.Should().Be(ZoneType.Graveyard,
            "Withering Torment destroys the target — it goes to the graveyard");
        _alice.LifeTotal.Should().Be(18,
            "Withering Torment is a FLAT 2 life loss, independent of the target's mana value");
    }

    // ── Happy path: opponent enchantment ─────────────────────────────────────

    [Fact]
    public void Resolve_OpponentEnchantment_MovesToGraveyard_CasterLosesFixed2Life()
    {
        var aura = new Enchantment("Pacifism", "{1}{W}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        aura.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(aura);

        var def = WitheringTormentFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(aura))) e.Execute();

        aura.Zone.Should().Be(ZoneType.Graveyard,
            "Withering Torment destroys the target enchantment");
        _alice.LifeTotal.Should().Be(18,
            "flat 2 life loss");
    }

    // ── No-op: target left battlefield (CR 608.2b) ───────────────────────────

    [Fact]
    public void Resolve_TargetNotOnBattlefield_NoEffect_NoCasterLifeLoss()
    {
        var ogre = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            Owner = _bob,
            Controller = _bob,
        };
        ogre.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(ogre);

        var def = WitheringTormentFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(ogre))) e.Execute();

        _alice.LifeTotal.Should().Be(20,
            "CR 608.2b — target not on the battlefield → spell does nothing, no life loss");
    }
}
