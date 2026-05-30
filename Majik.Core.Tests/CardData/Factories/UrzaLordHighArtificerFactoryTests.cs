using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Urza, Lord High Artificer (Modern Horizons). Legendary
/// Creature — Human Artificer 1/4, {2}{U}{U}.
///
/// Covers:
///   - Identity: Legendary Creature — Human Artificer, 1/4, {2}{U}{U}.
///   - ETB triggered ability creates a 0/0 colourless Construct artifact
///     creature token whose P/T scales with the controller's artifact
///     count via the supplied ContinuousEffectsService.
///   - Mana ability "Tap an untapped artifact you control: Add {U}" — taps
///     ANOTHER artifact (not Urza), produces {U}, gated on an eligible
///     untapped artifact existing.
///   - {5} impulse ability: shuffle library, exile top card, stamp a
///     zero-cost runtime exile-cast grant; the grant clears on the next
///     Cleanup when an event bus is supplied.
///   - NamedCardFactory dispatch.
/// </summary>
public class UrzaLordHighArtificerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOntoBattlefield(Permanent p, Player owner)
    {
        owner.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);
        p.SetController(owner);
    }

    private static TriggeredAbility EtbTrigger(Creature urza) =>
        urza.Abilities.OfType<TriggeredAbility>().Single();

    private static void FireEtb(Creature urza)
    {
        foreach (var effect in EtbTrigger(urza).Effects)
        {
            effect.Execute();
        }
    }

    // ----------------------------------------------------------------------
    // Identity + dispatch
    // ----------------------------------------------------------------------

    [Fact]
    public void Urza_IsLegendaryHumanArtificer_1_4_TwoUU()
    {
        var urza = UrzaLordHighArtificerFactory.Create(_alice);

        urza.Name.Should().Be("Urza, Lord High Artificer");
        urza.HasType(CardType.Creature).Should().BeTrue();
        urza.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        urza.HasSubtype(CardSubtype.Human).Should().BeTrue();
        urza.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
        urza.ManaCost.Should().Be("{2}{U}{U}");
        urza.BasePower.Should().Be(1);
        urza.BaseToughness.Should().Be(4);
        urza.Owner.Should().BeSameAs(_alice);
        urza.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Urza_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Urza, Lord High Artificer", _alice);

        card.Should().NotBeNull();
        card.Name.Should().Be("Urza, Lord High Artificer");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    // ----------------------------------------------------------------------
    // ETB — Construct token
    // ----------------------------------------------------------------------

    [Fact]
    public void Urza_Etb_CreatesColourlessConstructArtifactCreatureToken()
    {
        var effects = new ContinuousEffectsService();
        var urza = UrzaLordHighArtificerFactory.Create(
            _alice, eventBus: null, triggers: null, zoneService: null, effects: effects);
        PutOntoBattlefield(urza, _alice);

        FireEtb(urza);

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Construct")
            .ToList();
        tokens.Should().HaveCount(1);

        var construct = tokens[0];
        construct.HasType(CardType.Creature).Should().BeTrue();
        construct.HasType(CardType.Artifact).Should().BeTrue(
            "printed 'colorless Construct artifact creature token'");
        construct.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        construct.BasePower.Should().Be(0, "printed 0/0");
        construct.BaseToughness.Should().Be(0);
    }

    [Fact]
    public void Urza_Etb_ConstructPowerScalesWithArtifactCount()
    {
        var effects = new ContinuousEffectsService();
        var urza = UrzaLordHighArtificerFactory.Create(
            _alice, eventBus: null, triggers: null, zoneService: null, effects: effects);
        PutOntoBattlefield(urza, _alice);

        FireEtb(urza);

        var construct = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken && c.Name == "Construct");

        // Urza is NOT an artifact; the only artifact on the battlefield is
        // the Construct token itself → +1/+1.
        construct.GetPower().Should().Be(1);
        construct.GetToughness().Should().Be(1);

        // Add three more artifacts → token tracks the count (1 self + 3 = 4).
        for (var i = 0; i < 3; i++)
        {
            var art = new Artifact($"Bauble{i}", "0") { Owner = _alice, Controller = _alice };
            PutOntoBattlefield(art, _alice);
        }
        // Bystander artifacts entered via raw zone ops (no ActiveEffects
        // wired); invalidate the layer-system cache explicitly, as production's
        // CardMovedEvent would.
        effects.Clear();

        construct.GetPower().Should().Be(4);
        construct.GetToughness().Should().Be(4);
    }

    // ----------------------------------------------------------------------
    // Mana ability — Tap an untapped artifact you control: Add {U}
    // ----------------------------------------------------------------------

    private static ManaAbility ManaAbilityOf(Creature urza) =>
        urza.Abilities.OfType<ManaAbility>().Single();

    [Fact]
    public void Urza_ManaAbility_ProducesBlue()
    {
        var urza = UrzaLordHighArtificerFactory.Create(_alice);
        urza.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        ManaAbilityOf(urza).ManaGenerated.Blue.Should().Be(1);
    }

    [Fact]
    public void Urza_ManaAbility_TapsAnotherArtifact_NotUrza_ProducesU()
    {
        var urza = UrzaLordHighArtificerFactory.Create(_alice);
        PutOntoBattlefield(urza, _alice);

        var rock = new Artifact("Sol Ring", "1") { Owner = _alice, Controller = _alice };
        PutOntoBattlefield(rock, _alice);

        var mana = ManaAbilityOf(urza);
        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        produced.Blue.Should().Be(1);
        rock.IsTapped.Should().BeTrue("the tap-an-untapped-artifact cost taps the rock");
        urza.IsTapped.Should().BeFalse("Urza is NOT tapped by his own mana ability (no {T})");
    }

    [Fact]
    public void Urza_ManaAbility_CannotActivate_WhenNoUntappedArtifact()
    {
        var urza = UrzaLordHighArtificerFactory.Create(_alice);
        PutOntoBattlefield(urza, _alice);

        // Urza is on the battlefield but he is not an artifact; no other
        // artifact exists → the tap-another-artifact cost cannot be paid.
        ManaAbilityOf(urza).CanActivate().Should().BeFalse();
    }

    [Fact]
    public void Urza_ManaAbility_CannotActivate_WhenOnlyArtifactIsTapped()
    {
        var urza = UrzaLordHighArtificerFactory.Create(_alice);
        PutOntoBattlefield(urza, _alice);

        var rock = new Artifact("Sol Ring", "1") { Owner = _alice, Controller = _alice };
        PutOntoBattlefield(rock, _alice);
        rock.Tap();

        ManaAbilityOf(urza).CanActivate().Should().BeFalse(
            "the only artifact is already tapped");
    }

    // ----------------------------------------------------------------------
    // {5} impulse ability — shuffle, exile top, may play free until EOT
    // ----------------------------------------------------------------------

    private static ActivatedAbility ImpulseAbilityOf(Creature urza) =>
        urza.Abilities.OfType<ActivatedAbility>().Single();

    [Fact]
    public void Urza_ImpulseAbility_CostsFive()
    {
        var urza = UrzaLordHighArtificerFactory.Create(_alice);

        var ability = ImpulseAbilityOf(urza);
        ability.Costs.Should().HaveCount(1);
        ability.Costs[0].Description.Should().Contain("5");
    }

    [Fact]
    public void Urza_ImpulseAbility_ExilesTopCard_AndGrantsZeroCostPlay()
    {
        var urza = UrzaLordHighArtificerFactory.Create(_alice);
        PutOntoBattlefield(urza, _alice);

        // Single card in library so the post-shuffle "top card" is
        // deterministic regardless of the shuffle order.
        var spell = new Sorcery("Wrath of God", "2WW") { Owner = _alice };
        _alice.Zones.Library.AddCard(spell);

        foreach (var effect in ImpulseAbilityOf(urza).Effects)
        {
            effect.Execute();
        }

        _alice.Zones.Exile.GetCards().Should().Contain(spell);
        _alice.Zones.Library.GetCards().Should().NotContain(spell);
        spell.Zone.Should().Be(ZoneType.Exile);

        spell.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "Urza grants the controller permission to play the exiled card");
        spell.RuntimeExileCastCost.Should().Be(ManaCost.Zero,
            "'without paying its mana cost' — the grant cost is zero");
    }

    [Fact]
    public void Urza_ImpulseAbility_EmptyLibrary_NoOp()
    {
        var urza = UrzaLordHighArtificerFactory.Create(_alice);
        PutOntoBattlefield(urza, _alice);

        foreach (var effect in ImpulseAbilityOf(urza).Effects)
        {
            effect.Execute();
        }

        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }
}
