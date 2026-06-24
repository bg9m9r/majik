using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HeraldicBannerFactory"/> — Heraldic Banner
/// (Core Set 2020, {3}). Colourless Artifact. Oracle text:
///   "As this artifact enters, choose a color.
///    Creatures you control of the chosen color get +1/+0.
///    {T}: Add one mana of the chosen color."
///
/// Combines <see cref="ColdsteelHeartFactory"/>'s "choose a color as it enters
/// + {T} add chosen mana" (CR 614.12 / 605.1a) with a DYNAMIC colour-scoped
/// anthem (<see cref="ControllerCreatureAnthemEffect"/> reading the same
/// <see cref="ColorChoice"/> holder, CR 613.7c). No snow supertype, no
/// enters-tapped.
///
/// Covers:
/// - Identity ({3} colourless Artifact, owner/controller).
/// - One dynamic {T} mana ability seeded to the White default; no triggered abilities.
/// - ColorChoice holder stashed for the overlay binder; overlay binds.
/// - {T}: add one mana of the chosen color (each of W/U/B/R/G).
/// - Anthem +1/+0 to the controller's creatures OF THE CHOSEN COLOR;
///   non-chosen-color and opponents' creatures unaffected; LTB lifts.
/// - End-to-end agent choice drives BOTH mana production and the anthem.
/// </summary>
[Trait("Color", "C")]
public class HeraldicBannerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HeraldicBanner_IsColorlessArtifact_AtCost3()
    {
        var artifact = HeraldicBannerFactory.Create(_alice);

        artifact.Name.Should().Be("Heraldic Banner");
        artifact.ManaCost.Should().Be("{3}");
        artifact.HasType(CardType.Artifact).Should().BeTrue();
        artifact.HasSupertype(CardSupertype.Snow).Should().BeFalse();
        artifact.Owner.Should().BeSameAs(_alice);
        artifact.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HeraldicBanner_SingleArgPath_HasOneDynamicManaAbility_SeededToDefault()
    {
        // CR 614.12 — agent-gated: ONE dynamic {T} mana ability reading a
        // ColorChoice holder (seeded White until the ETB replacement stamps the
        // agent's pick), and no other abilities. Exactly ONE producible colour.
        var artifact = HeraldicBannerFactory.Create(_alice);

        var mana = artifact.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(1, "one dynamic {T}: Add one mana of the chosen color");
        mana[0].ManaGenerated.White.Should().Be(1, "the holder is seeded to the White default");
        artifact.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    [Fact]
    public void HeraldicBanner_SingleArgPath_StashesColorChoiceForOverlayBinder()
    {
        var artifact = HeraldicBannerFactory.Create(_alice);
        ColorChoiceRegistry.Get(artifact).Should().NotBeNull(
            "the factory stashes the holder for ChooseColorPermanentBinder");
    }

    [Fact]
    public void HeraldicBanner_OverlayBinder_RegistersAgentPromptingReplacement()
    {
        var artifact = HeraldicBannerFactory.Create(_alice);
        var bus = new ReplacementBus();
        ChooseColorPermanentBinder.Bind(artifact, bus).Should().BeTrue(
            "Heraldic Banner stashed a ColorChoice holder");
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
    public void HeraldicBanner_ChosenColor_ManaAbilityProducesThatColor(ManaColor chosen, string pip)
    {
        var artifact = HeraldicBannerFactory.Create(_alice);
        ColorChoiceRegistry.Get(artifact)!.Choose(chosen);

        var mana = artifact.Abilities.OfType<ManaAbility>().Single();
        var produced = mana.Activate();
        var expected = ManaCost.Parse(pip);
        produced.White.Should().Be(expected.White);
        produced.Blue.Should().Be(expected.Blue);
        produced.Black.Should().Be(expected.Black);
        produced.Red.Should().Be(expected.Red);
        produced.Green.Should().Be(expected.Green);
    }

    // -----------------------------------------------------------------------
    // Anthem: "Creatures you control of the chosen color get +1/+0" (CR 613.7c)
    // -----------------------------------------------------------------------

    [Fact]
    public void HeraldicBanner_Anthem_BuffsControllersChosenColorCreatures()
    {
        var svc = new ContinuousEffectsService();
        var redBear = MakeCreature("Bear", _alice, svc, 2, 2, "R");

        var banner = HeraldicBannerFactory.Create(_alice, svc);
        ColorChoiceRegistry.Get(banner)!.Choose(ManaColor.Red);
        PlaceOnBattlefield(banner, svc, _alice);

        redBear.GetPower().Should().Be(3, "+1/+0 to red creatures (the chosen color)");
        redBear.GetToughness().Should().Be(2, "the anthem is +1/+0 — toughness is unchanged");
    }

    [Fact]
    public void HeraldicBanner_Anthem_DoesNotBuffNonChosenColorCreatures()
    {
        var svc = new ContinuousEffectsService();
        var greenBear = MakeCreature("Bear", _alice, svc, 2, 2, "G");

        var banner = HeraldicBannerFactory.Create(_alice, svc);
        ColorChoiceRegistry.Get(banner)!.Choose(ManaColor.Red);
        PlaceOnBattlefield(banner, svc, _alice);

        greenBear.GetPower().Should().Be(2, "green is not the chosen colour (red)");
        greenBear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void HeraldicBanner_Anthem_DoesNotBuffOpponentsChosenColorCreatures()
    {
        var svc = new ContinuousEffectsService();
        var bobRed = MakeCreature("Bob's Goblin", _bob, svc, 1, 1, "R");

        var banner = HeraldicBannerFactory.Create(_alice, svc);
        ColorChoiceRegistry.Get(banner)!.Choose(ManaColor.Red);
        PlaceOnBattlefield(banner, svc, _alice);

        bobRed.GetPower().Should().Be(1,
            "the anthem keys on 'you control' — opponents' creatures are unaffected");
        bobRed.GetToughness().Should().Be(1);
    }

    [Fact]
    public void HeraldicBanner_Anthem_LeavingBattlefield_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();
        var redBear = MakeCreature("Bear", _alice, svc, 2, 2, "R");

        var banner = HeraldicBannerFactory.Create(_alice, svc);
        ColorChoiceRegistry.Get(banner)!.Choose(ManaColor.Red);
        PlaceOnBattlefield(banner, svc, _alice);
        redBear.GetPower().Should().Be(3);

        banner.SetZone(ZoneType.Graveyard);
        _alice.Zones.Battlefield.RemoveCard(banner);
        _alice.Zones.Graveyard.AddCard(banner);

        redBear.GetPower().Should().Be(2,
            "the anthem's IsActive gates on the source being on the battlefield");
    }

    // -----------------------------------------------------------------------
    // Agent-gated choose-a-color (CR 614.12) — drives mana AND anthem end to end
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HeraldicBanner_AgentChoice_DrivesManaAndAnthem_EndToEnd()
    {
        AgentRegistry.Clear();
        try
        {
            var svc = new ContinuousEffectsService();
            var blackZombie = MakeCreature("Zombie", _alice, svc, 2, 2, "B");

            var banner = HeraldicBannerFactory.Create(_alice, svc);
            var bus = new ReplacementBus();
            ChooseColorPermanentBinder.Bind(banner, bus).Should().BeTrue();

            var agent = new ScriptedAgent();
            agent.QueueChoice(cands => new[]
            {
                cands.First(c => (ManaColor)c == ManaColor.Black),
            });
            var ctx = ResolutionContext.For(_alice, agent, game: null, chosenTargets: null);

            var intent = new ZoneMoveIntent(
                Card: banner, FromZone: ZoneType.Hand,
                ToZone: ZoneType.Battlefield, Controller: _alice);
            await bus.ApplyAsync(intent, ctx);

            // Now actually place it on the battlefield so the anthem is active.
            PlaceOnBattlefield(banner, svc, _alice);

            // Mana production tracks the agent's pick (black).
            var mana = banner.Abilities.OfType<ManaAbility>().Single();
            mana.Activate().Black.Should().Be(1,
                "the agent chose Black, so {T} adds one black mana (CR 614.12 / 605.1a)");

            // The anthem tracks the same pick: the black creature is buffed.
            blackZombie.GetPower().Should().Be(3,
                "the agent chose Black, so the +1/+0 anthem buffs the black creature");
            blackZombie.GetToughness().Should().Be(2);
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void HeraldicBanner_Create_ThrowsOnNullOwner()
    {
        var act = () => HeraldicBannerFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void PlaceOnBattlefield(Artifact banner, ContinuousEffectsService svc, Player controller)
    {
        banner.SetZone(ZoneType.Battlefield);
        banner.ActiveEffects = svc;
        controller.Zones.Battlefield.AddCard(banner);
    }

    private static Creature MakeCreature(string name, Player owner,
        ContinuousEffectsService svc, int p, int t, string manaColorPip)
    {
        // Mana cost pip drives the creature's printed color (CR 105.2a) so
        // GetColors() reports it. e.g. "{R}" → red, "{B}" → black.
        var c = new Creature(name, $"{{{manaColorPip}}}", p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        c.ActiveEffects = svc;
        return c;
    }
}
