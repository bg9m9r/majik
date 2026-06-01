using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// Deferral #3 residual / #2 — modal PERMANENT MDFC backs. The cast-either-face
/// seam (deferral #3) is extended to MDFCs whose BACK face is a nonland
/// permanent (creature / artifact / enchantment / planeswalker — the Kaldheim
/// God MDFCs). Choosing the back casts it as that permanent (its own cost /
/// type / effect) and, on resolution, it enters the battlefield AS that face
/// (CR 712.3 / 608.3). No transform happens (CR 712.4).
///
/// Canonical card: Birgi, God of Storytelling // Harnfel, Horn of Bounty —
/// Legendary Creature // Legendary Artifact.
/// </summary>
public class MdfcPermanentBackTests : IDisposable
{
    public MdfcPermanentBackTests() => AgentRegistry.Clear();
    public void Dispose() => AgentRegistry.Clear();

    private static GameContext Ctx(Player self) =>
        new(self, new[] { self }, self, 1, PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

    // ------------------------------------------------------------------
    // Card model — front face carries a castable PERMANENT back descriptor.
    // ------------------------------------------------------------------

    [Fact]
    public void Birgi_FrontCard_OffersCastablePermanentBack()
    {
        var alice = new Player("Alice", 20);
        var birgi = BirgiGodOfStorytellingFactory.Create(alice);

        birgi.HasType(CardType.Creature).Should().BeTrue();
        birgi.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        birgi.HasSubtype(CardSubtype.God).Should().BeTrue();
        birgi.Power.Should().Be(3);
        birgi.Toughness.Should().Be(3);

        birgi.MdfcState.Should().NotBeNull();
        birgi.MdfcState!.CanCastEitherFace.Should().BeTrue("the back face is castable (CR 712.3)");
        var back = birgi.MdfcState!.CastableBackFace!;
        back.Name.Should().Be("Harnfel, Horn of Bounty");
        back.IsLand.Should().BeFalse("Harnfel is not a land");
        back.IsPermanent.Should().BeTrue("Harnfel is a nonland permanent (Legendary Artifact)");
        back.ManaCost.Should().Be("{4}{R}");
    }

    [Fact]
    public void Birgi_DispatchesThroughNamedFactory_WithPermanentBack()
    {
        var alice = new Player("Alice", 20);
        var card = Majik.Core.CardData.NamedCardFactory.Create("Birgi, God of Storytelling", alice);

        card.Should().BeOfType<Creature>();
        ((Creature)card).MdfcState!.CastableBackFace!.IsPermanent.Should().BeTrue();
    }

    [Fact]
    public void HarnfelBackCard_IsArtifact_AndDoesNotOfferAnotherFace()
    {
        var alice = new Player("Alice", 20);
        var harnfel = HarnfelHornOfBountyFactory.Create(alice);

        harnfel.Should().BeOfType<Artifact>();
        harnfel.HasType(CardType.Artifact).Should().BeTrue();
        harnfel.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        harnfel.MdfcState!.IsBackFace.Should().BeTrue();
        harnfel.MdfcState!.CanCastEitherFace.Should().BeFalse(
            "a materialized back-face permanent is already the chosen face");
    }

    // ------------------------------------------------------------------
    // ResolveFaceAsync — choosing the back returns the permanent face.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResolveFace_ChoosingBack_ReturnsPermanentBackFace()
    {
        var alice = new Player("Alice", 20);
        var birgi = BirgiGodOfStorytellingFactory.Create(alice);
        var agent = new ScriptedAgent();
        agent.QueueChoiceIndex(1); // back

        var chosen = await MdfcCastFlow.ResolveFaceAsync(birgi, alice, agent, Ctx(alice));

        chosen.Should().NotBeNull();
        chosen!.IsPermanent.Should().BeTrue();
        chosen.IsLand.Should().BeFalse();
        chosen.Name.Should().Be("Harnfel, Horn of Bounty");
    }

    // ------------------------------------------------------------------
    // Materialization — choosing the back builds the permanent as that face.
    // ------------------------------------------------------------------

    [Fact]
    public void PermanentBackFace_BuildsTheBackPermanent_NotTheFront()
    {
        var alice = new Player("Alice", 20);
        var birgi = BirgiGodOfStorytellingFactory.Create(alice);
        var back = birgi.MdfcState!.CastableBackFace!;

        var built = back.BuildCard(alice, new ReplacementBus());

        built.Should().BeOfType<Artifact>("choosing the back enters the back permanent (the Artifact)");
        built.Name.Should().Be("Harnfel, Horn of Bounty");
        built.HasType(CardType.Creature).Should().BeFalse("the battlefield permanent is NOT the creature front");
        ((Card)built).MdfcState!.ActiveFaceName.Should().Be("Harnfel, Horn of Bounty");
    }

    [Fact]
    public void PermanentBack_HasItsOwnActivatedAbility_NotBirgisTrigger()
    {
        var alice = new Player("Alice", 20);
        var harnfel = HarnfelHornOfBountyFactory.Create(alice);

        harnfel.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "Harnfel has its own discard-activated ability (CR 602.1), not Birgi's cast trigger");
        harnfel.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the front face's cast-trigger mana is NOT on the back face");
    }

    // ------------------------------------------------------------------
    // Back ability resolves: exile top two + grant cast-from-exile.
    // ------------------------------------------------------------------

    [Fact]
    public void Harnfel_DiscardAbility_ExilesTopTwo_GrantsPlayPermission()
    {
        var alice = new Player("Alice", 20);
        var harnfel = HarnfelHornOfBountyFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(harnfel);
        harnfel.SetZone(ZoneType.Battlefield);

        // Library: three cards (only the top two should be exiled).
        var c1 = new Creature("Top One", "{1}{R}", 1, 1) { Owner = alice };
        var c2 = new Sorcery("Top Two", "{2}{R}") { Owner = alice };
        var c3 = new Creature("Bottom", "{R}", 2, 2) { Owner = alice };
        foreach (var c in new ICard[] { c1, c2, c3 })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var ability = harnfel.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var fx in ability.Effects) fx.Execute();

        c1.Zone.Should().Be(ZoneType.Exile, "the top card is exiled");
        c2.Zone.Should().Be(ZoneType.Exile, "the second card is exiled");
        c3.Zone.Should().Be(ZoneType.Library, "only the TOP TWO are exiled");

        // CR 118.7 — "you may play those cards this turn."
        c1.RuntimeExileCastAllowedCaster.Should().BeSameAs(alice);
        c2.RuntimeExileCastAllowedCaster.Should().BeSameAs(alice);
    }
}
