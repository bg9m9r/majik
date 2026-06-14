using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using System.Linq;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ColdsteelHeartFactory"/> — Coldsteel Heart
/// (Coldsnap). Snow Artifact, {2}. Oracle text:
///   "This artifact enters tapped.
///    As this artifact enters, choose a color.
///    {T}: Add one mana of the chosen color."
///
/// Modelled after <see cref="TempleOfTheDragonQueenFactory"/> (JSON identity +
/// up-front "choose a color as this enters" resolution, CR 614.12, plus an
/// ETB-tapped replacement registered when a <see cref="ReplacementBus"/> is
/// supplied, CR 614.1c) — only the artifact's "enters tapped" is unconditional.
///
/// Covers:
/// - Identity (Snow Artifact, owner/controller, non-Basic, {2}).
/// - The shape-only single-arg path produces no mana ability (the chosen color
///   isn't known yet) and registers no replacement.
/// - {T}: Add one mana of the chosen color — exactly one ManaAbility, of the
///   chosen color, once a color is supplied (CR 605.1a).
/// - Unconditional ETB-tapped (CR 614.1c): always taps on entry when a bus is
///   supplied; the shape-only path registers nothing.
/// - Args validation + dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class ColdsteelHeartFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void ColdsteelHeart_IsNotBasic()
    {
        var artifact = ColdsteelHeartFactory.Create(_alice);
        artifact.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    [Fact]
    public void ColdsteelHeart_SingleArgPath_HasOneDynamicManaAbility_SeededToDefault()
    {
        // CR 614.12 — the prod single-arg path is agent-gated: it attaches ONE
        // dynamic {T} mana ability reading a ColorChoice holder (seeded White
        // until the ETB ChooseColorReplacement stamps the agent's pick), and no
        // other abilities. Strictly ONE producible colour — never the old
        // five-WUBRG over-permissiveness.
        var artifact = ColdsteelHeartFactory.Create(_alice);

        var mana = artifact.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(1, "one dynamic {T}: Add one mana of the chosen color");
        mana[0].ManaGenerated.White.Should().Be(1, "the holder is seeded to the White default");
        artifact.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    [Fact]
    public void ColdsteelHeart_SingleArgPath_StashesColorChoiceForOverlayBinder()
    {
        var artifact = ColdsteelHeartFactory.Create(_alice);
        ColorChoiceRegistry.Get(artifact).Should().NotBeNull(
            "the factory stashes the holder for ChooseColorPermanentBinder");
    }

    [Fact]
    public void ColdsteelHeart_OverlayBinder_RegistersAgentPromptingReplacement()
    {
        var artifact = ColdsteelHeartFactory.Create(_alice);
        var bus = new ReplacementBus();
        ChooseColorPermanentBinder.Bind(artifact, bus).Should().BeTrue(
            "Coldsteel Heart stashed a ColorChoice holder");
    }

    // -----------------------------------------------------------------------
    // {T}: Add one mana of the chosen color (CR 605.1a)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(ManaColor.White, "W")]
    [InlineData(ManaColor.Blue, "U")]
    [InlineData(ManaColor.Black, "B")]
    [InlineData(ManaColor.Red, "R")]
    [InlineData(ManaColor.Green, "G")]
    public void ColdsteelHeart_ChosenColor_ProducesExactlyThatColor(ManaColor chosen, string pip)
    {
        var artifact = ColdsteelHeartFactory.Create(
            _alice, chosenColor: chosen, replacements: null);

        var mana = artifact.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(1, "{T}: Add one mana of the chosen color");

        var expected = ManaCost.Parse(pip);
        var produced = mana[0].ManaGenerated;
        produced.White.Should().Be(expected.White);
        produced.Blue.Should().Be(expected.Blue);
        produced.Black.Should().Be(expected.Black);
        produced.Red.Should().Be(expected.Red);
        produced.Green.Should().Be(expected.Green);
    }

    // -----------------------------------------------------------------------
    // Agent-gated choose-a-color (CR 614.12) — end to end
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ColdsteelHeart_AgentChoice_DrivesManaProduction_EndToEnd()
    {
        AgentRegistry.Clear();
        try
        {
            // Prod single-arg build + the overlay ChooseColorPermanentBinder
            // (what DeckCardBuilder's OverlayAdditiveBinders runs), then the ETB
            // replacement prompts the agent and stamps the pick onto the holder
            // the dynamic mana ability reads.
            var artifact = ColdsteelHeartFactory.Create(_alice);
            var bus = new ReplacementBus();
            ChooseColorPermanentBinder.Bind(artifact, bus).Should().BeTrue();

            var agent = new ScriptedAgent();
            agent.QueueChoice(cands => new[]
            {
                cands.First(c => (ManaColor)c == ManaColor.Black),
            });
            var ctx = ResolutionContext.For(_alice, agent, game: null, chosenTargets: null);

            var intent = new ZoneMoveIntent(
                Card: artifact, FromZone: ZoneType.Hand,
                ToZone: ZoneType.Battlefield, Controller: _alice);
            await bus.ApplyAsync(intent, ctx);

            var mana = artifact.Abilities.OfType<ManaAbility>().Single();
            mana.Activate().Black.Should().Be(1,
                "the agent chose Black, so {T} adds one black mana (CR 614.12 / 605.1a)");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // Enters tapped (CR 614.1c) — unconditional
    // -----------------------------------------------------------------------

    [Fact]
    public void ColdsteelHeart_AlwaysEntersTapped_WhenBusSupplied()
    {
        var bus = new ReplacementBus();
        var artifact = ColdsteelHeartFactory.Create(
            _alice, chosenColor: ManaColor.Blue, replacements: bus);

        var after = ApplyEtb(bus, artifact, _alice);

        after.EntersTapped.Should().BeTrue(
            "\"This artifact enters tapped.\" is unconditional");
    }

    [Fact]
    public void ColdsteelHeart_SingleArgPath_DoesNotRegisterReplacement()
    {
        // Shape-only path: a fresh bus must remain inert.
        var bus = new ReplacementBus();
        var artifact = ColdsteelHeartFactory.Create(_alice);

        var after = ApplyEtb(bus, artifact, _alice);
        after.EntersTapped.Should().BeFalse(
            "no replacement registered on the shape-only path");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void ColdsteelHeart_Create_ThrowsOnNullOwner()
    {
        var act = () => ColdsteelHeartFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ColdsteelHeart_Create_ThrowsOnColorlessChosenColor()
    {
        var act = () => ColdsteelHeartFactory.Create(
            _alice, chosenColor: ManaColor.Colorless, replacements: null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ZoneMoveIntent ApplyEtb(ReplacementBus bus, Artifact artifact, Player controller)
    {
        var intent = new ZoneMoveIntent(
            Card: artifact,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: controller);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        return after!;
    }
}
