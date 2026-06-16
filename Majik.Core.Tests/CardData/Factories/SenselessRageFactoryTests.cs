using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SenselessRageFactory"/>.
///
/// Card: Senseless Rage — Enchantment — Aura {1}{R} (Shadows over Innistrad).
///   "Enchant creature
///    Enchanted creature gets +2/+2.
///    Madness {1}{R}"
///
/// Covers:
///   - Identity: {1}{R} Enchantment — Aura.
///   - Named-card dispatcher routes to this factory.
///   - Static +2/+2 via AttachedBoostEffect (Layer 7c):
///       2/2 becomes 4/4.
///   - Effect is inert while the aura is unattached.
///   - BuildSpellDefinition: legal candidates are creatures only.
///   - PROD-WIRING SEAM: building through the production effects-aware
///     entrypoint <see cref="NamedCardFactory.Create(string, Player,
///     ContinuousEffectsService?)"/> registers the +2/+2 boost against the
///     live per-game service (the deferral this card pays down).
///   - Madness {1}{R} is catalogued intrinsically (CR 702.35).
/// </summary>
[Trait("Color", "R")]
public class SenselessRageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SenselessRage_Identity()
    {
        var c = SenselessRageFactory.Create(_alice);

        c.Name.Should().Be("Senseless Rage");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SenselessRage()
    {
        var card = NamedCardFactory.Create("Senseless Rage", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Senseless Rage");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Static +2/+2 boost
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_PlusTwoPlusTwo_TwoTwoBecomesFourFour()
    {
        var effects = new ContinuousEffectsService();
        var aura = SenselessRageFactory.Create(_alice, effects);
        PlaceOnBattlefield(aura, _alice);

        var bear = MakeBattlefieldCreature("Bear", 2, 2);
        aura.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(4, "2 + 2 = 4 while enchanted");
        chars.Toughness.Should().Be(4, "2 + 2 = 4 while enchanted");
    }

    [Fact]
    public void Static_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var aura = SenselessRageFactory.Create(_alice, effects);
        PlaceOnBattlefield(aura, _alice);

        var bear = MakeBattlefieldCreature("Bear", 2, 2);
        // Don't attach.

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2, "unattached — no effect");
        chars.Toughness.Should().Be(2, "unattached — no effect");
    }

    // -----------------------------------------------------------------------
    // Prod-wiring seam — the deferral this card pays down.
    //
    // The production card-build path threads the live per-game
    // ContinuousEffectsService through NamedCardFactory.Create(name, owner,
    // effects), which dispatches to the two-parameter effects-aware overload.
    // This test proves the +2/+2 boost registers via that prod entrypoint
    // (not only under a directly-supplied test service).
    // -----------------------------------------------------------------------

    [Fact]
    public void ProdEntrypoint_WiresStaticBoost_ViaEffectsAwareOverload()
    {
        var effects = new ContinuousEffectsService();

        var built = NamedCardFactory.Create("Senseless Rage", _alice, effects);

        var aura = built.Should().BeOfType<Enchantment>().Subject;
        PlaceOnBattlefield(aura, _alice);

        var bear = MakeBattlefieldCreature("Bear", 2, 2);
        aura.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(4,
            "the prod effects-aware dispatch must register the +2/+2 boost");
        chars.Toughness.Should().Be(4,
            "the prod effects-aware dispatch must register the +2/+2 boost");
    }

    // -----------------------------------------------------------------------
    // Madness — catalogued intrinsically (CR 702.35).
    // -----------------------------------------------------------------------

    [Fact]
    public void Madness_IsCatalogued_AtOneRed()
    {
        var aura = SenselessRageFactory.Create(_alice);

        MadnessCatalog.HasMadness(aura).Should().BeTrue();
        MadnessCatalog.CostFor(aura).Should().Be(ManaCost.Parse("{1}{R}"));
    }

    // -----------------------------------------------------------------------
    // BuildSpellDefinition — candidate filter
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_OnlyCreaturesAreLegalTargets()
    {
        var aura = SenselessRageFactory.Create(_alice);

        var creature = MakeBattlefieldCreature("Bear", 2, 2);
        var land = new Land("Mountain");
        var artifact = new Artifact("Mox Ruby", "{0}");

        var battlefield = new Permanent[] { creature, land, artifact };
        var def = SenselessRageFactory.BuildSpellDefinition(aura, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(creature);
        candidates.Should().NotContain(land);
        candidates.Should().NotContain(artifact);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Creature MakeBattlefieldCreature(string name, int power, int toughness)
    {
        var c = new Creature(name, "{1}{G}", power, toughness);
        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static void PlaceOnBattlefield(Enchantment aura, Player owner)
    {
        aura.SetOwner(owner);
        aura.SetController(owner);
        owner.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
    }
}
