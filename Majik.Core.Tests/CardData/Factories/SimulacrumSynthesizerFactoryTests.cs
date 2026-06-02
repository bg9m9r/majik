using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Simulacrum Synthesizer (The Brothers' War, {2}{U}). Artifact.
///
/// Oracle text (verified against Scryfall):
///   "When this artifact enters, scry 2.
///    Whenever another artifact you control with mana value 3 or greater
///    enters, create a 0/0 colorless Construct artifact creature token with
///    'This token gets +1/+1 for each artifact you control.'"
///
/// Covers:
///   - Identity: Artifact, {2}{U}, mana value 3.
///   - ETB scry-2 trigger present (CR 603.1 / 701.20).
///   - Artifact-ETB trigger mints a 0/0 colourless Construct artifact
///     creature token whose P/T scales with the controller's artifact count
///     when ANOTHER artifact with mana value >= 3 enters under your control.
///   - The MV>=3 gate: a cheap (MV 2) artifact entering does NOT mint a token.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "U")]
public class SimulacrumSynthesizerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOntoBattlefield(Permanent p, Player owner)
    {
        owner.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);
        p.SetController(owner);
    }

    // Both triggers fire off CardMovedEvent (the scry one is the self-ETB
    // condition). Select the token trigger by its effect description.
    private static TriggeredAbility ArtifactEtbTrigger(Artifact synth) =>
        synth.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("Construct")));

    private static void FireArtifactEtb(Artifact synth, CardMovedEvent e)
    {
        var trigger = ArtifactEtbTrigger(synth);
        if (!trigger.Condition.Matches(e, trigger)) return;
        foreach (var effect in trigger.Effects)
        {
            effect.Execute();
        }
    }

    private static CardMovedEvent EntersBattlefield(ICard card) =>
        new(card, ZoneType.Library, ZoneType.Battlefield);

    // ----------------------------------------------------------------------
    // Identity + dispatch
    // ----------------------------------------------------------------------

    [Fact]
    public void Synthesizer_IsArtifact_TwoU()
    {
        var synth = SimulacrumSynthesizerFactory.Create(_alice);

        synth.Name.Should().Be("Simulacrum Synthesizer");
        synth.HasType(CardType.Artifact).Should().BeTrue();
        synth.HasType(CardType.Creature).Should().BeFalse("printed as a noncreature Artifact");
        synth.ManaCost.Should().Be("{2}{U}");
        synth.ManaCostValue.TotalValue.Should().Be(3);
        synth.Owner.Should().BeSameAs(_alice);
        synth.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Synthesizer_HasScryEtbTrigger_AndArtifactEtbTrigger()
    {
        var synth = SimulacrumSynthesizerFactory.Create(_alice);

        var triggers = synth.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2,
            "one self-ETB scry trigger + one another-artifact-ETB token trigger");

        triggers.Should().Contain(t => t.Condition is EventTriggerCondition<CardMovedEvent>,
            "the another-artifact-ETB trigger fires off CardMovedEvent");
    }

    // ----------------------------------------------------------------------
    // Artifact-ETB token trigger
    // ----------------------------------------------------------------------

    [Fact]
    public void Synthesizer_AnotherArtifactMv3Enters_CreatesConstructToken()
    {
        var effects = new ContinuousEffectsService();
        var synth = SimulacrumSynthesizerFactory.Create(
            _alice, eventBus: null, triggers: null, zoneService: null, effects: effects);
        PutOntoBattlefield(synth, _alice);

        // A mana-value-3 artifact enters under Alice's control.
        var rock = new Artifact("Worn Powerstone", "3") { Owner = _alice, Controller = _alice };
        PutOntoBattlefield(rock, _alice);
        FireArtifactEtb(synth, EntersBattlefield(rock));

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
    public void Synthesizer_ConstructPowerScalesWithArtifactCount()
    {
        var effects = new ContinuousEffectsService();
        var synth = SimulacrumSynthesizerFactory.Create(
            _alice, eventBus: null, triggers: null, zoneService: null, effects: effects);
        PutOntoBattlefield(synth, _alice);

        var rock = new Artifact("Worn Powerstone", "3") { Owner = _alice, Controller = _alice };
        PutOntoBattlefield(rock, _alice);
        FireArtifactEtb(synth, EntersBattlefield(rock));

        var construct = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken && c.Name == "Construct");

        // Artifacts on the battlefield: Synthesizer + rock + the token = 3.
        effects.Clear();
        construct.GetPower().Should().Be(3);
        construct.GetToughness().Should().Be(3);
    }

    [Fact]
    public void Synthesizer_CheapArtifactEnters_DoesNotCreateToken()
    {
        var effects = new ContinuousEffectsService();
        var synth = SimulacrumSynthesizerFactory.Create(
            _alice, eventBus: null, triggers: null, zoneService: null, effects: effects);
        PutOntoBattlefield(synth, _alice);

        // Mana value 2 — below the "3 or greater" gate.
        var bauble = new Artifact("Mishra's Bauble", "2") { Owner = _alice, Controller = _alice };
        PutOntoBattlefield(bauble, _alice);
        FireArtifactEtb(synth, EntersBattlefield(bauble));

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Should().NotContain(c => c.IsToken && c.Name == "Construct");
    }

    [Fact]
    public void Synthesizer_OpponentArtifactEnters_DoesNotCreateToken()
    {
        var effects = new ContinuousEffectsService();
        var synth = SimulacrumSynthesizerFactory.Create(
            _alice, eventBus: null, triggers: null, zoneService: null, effects: effects);
        PutOntoBattlefield(synth, _alice);

        // MV-3 artifact, but Bob controls it — "another artifact YOU control".
        var rock = new Artifact("Worn Powerstone", "3") { Owner = _bob, Controller = _bob };
        PutOntoBattlefield(rock, _bob);
        FireArtifactEtb(synth, EntersBattlefield(rock));

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Should().NotContain(c => c.IsToken && c.Name == "Construct");
    }

    [Fact]
    public void Synthesizer_NonArtifactEnters_DoesNotCreateToken()
    {
        var effects = new ContinuousEffectsService();
        var synth = SimulacrumSynthesizerFactory.Create(
            _alice, eventBus: null, triggers: null, zoneService: null, effects: effects);
        PutOntoBattlefield(synth, _alice);

        // MV-3 nonartifact creature.
        var bear = new Creature("Watchwolf", "{1}{G}{W}", 3, 3) { Owner = _alice, Controller = _alice };
        PutOntoBattlefield(bear, _alice);
        FireArtifactEtb(synth, EntersBattlefield(bear));

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Should().NotContain(c => c.IsToken && c.Name == "Construct");
    }

    // ----------------------------------------------------------------------
    // Dispatch
    // ----------------------------------------------------------------------

    [Fact]
    public void Synthesizer_DispatchesThroughNamedCardFactory()
    {
        var card = Majik.Core.CardData.NamedCardFactory.Create("Simulacrum Synthesizer", _alice);
        card.Should().NotBeNull();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }
}
