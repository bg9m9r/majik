using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AllThatGlittersFactory"/>.
///
/// Card: All That Glitters — Enchantment — Aura {1}{W} (Throne of Eldraine).
///   "Enchant creature"
///   "Enchanted creature gets +1/+1 for each artifact and/or enchantment
///    you control."
///
/// Covers:
///   - Identity / dispatch (Aura subtype, {1}{W} cost).
///   - Plain "Enchant creature" cast-time targeting via
///     <see cref="AuraSpellDefinitionBuilder"/> — any creature is legal,
///     non-creatures excluded.
///   - Dynamic +N/+N boost where N = controller's live count of
///     artifacts and/or enchantments (the aura itself counts — it is an
///     enchantment you control).
///   - Boost grows as more artifacts/enchantments enter.
///   - Boost is inert while unattached / off-battlefield.
/// </summary>
public class AllThatGlittersTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AllThatGlitters_Identity()
    {
        var c = AllThatGlittersFactory.Create(_alice);

        c.Name.Should().Be("All That Glitters");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AllThatGlitters()
    {
        var card = NamedCardFactory.Create("All That Glitters", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("All That Glitters");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Dynamic +N/+N boost
    // -----------------------------------------------------------------------

    [Fact]
    public void Attached_GrowsBoost_AsArtifactAndEnchantmentCountRises()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var aura = AllThatGlittersFactory.Create(_alice, svc);
        aura.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(aura);

        aura.AttachTo(bear);

        // Only the aura itself counts (it is an enchantment you control) → +1/+1.
        bear.GetPower().Should().Be(2 + 1, "+1/+1 from one enchantment (the aura itself)");
        bear.GetToughness().Should().Be(2 + 1);

        // Add an artifact under Alice's control. Wire ActiveEffects so its zone
        // entry invalidates the layer-system cache.
        var bauble = new Artifact("Bauble", "0");
        bauble.SetOwner(_alice);
        bauble.SetController(_alice);
        bauble.ActiveEffects = svc;
        bauble.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(bauble);

        bear.GetPower().Should().Be(2 + 2, "+2/+2 from one artifact + one enchantment");
        bear.GetToughness().Should().Be(2 + 2);

        // Add another enchantment under Alice's control.
        var ench = new Enchantment("Trinket", "{1}", supertypes: null, subtypes: null);
        ench.SetOwner(_alice);
        ench.SetController(_alice);
        ench.ActiveEffects = svc;
        ench.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(ench);

        bear.GetPower().Should().Be(2 + 3, "+3/+3 from one artifact + two enchantments");
        bear.GetToughness().Should().Be(2 + 3);
    }

    [Fact]
    public void Unattached_BoostIsZero()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var aura = AllThatGlittersFactory.Create(_alice, svc);
        aura.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(aura);
        // intentionally not attached

        bear.GetPower().Should().Be(2, "the boost gates on AttachedTo");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void CountArtifactsAndEnchantments_ReadsControllerBattlefield()
    {
        var aura = AllThatGlittersFactory.Create(_alice);
        aura.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(aura);

        AllThatGlittersFactory.CountArtifactsAndEnchantments(aura).Should().Be(1,
            "only the aura itself (an enchantment) is on the battlefield");

        var bauble = new Artifact("Bauble", "0");
        bauble.SetOwner(_alice);
        bauble.SetController(_alice);
        bauble.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(bauble);

        AllThatGlittersFactory.CountArtifactsAndEnchantments(aura).Should().Be(2,
            "artifact + the aura");
    }

    [Fact]
    public void CountArtifactsAndEnchantments_ArtifactEnchantment_CountsOnce()
    {
        var aura = AllThatGlittersFactory.Create(_alice);
        aura.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(aura);

        // An artifact-enchantment satisfies "artifact and/or enchantment"
        // but must be counted only once (CR 700.5 — "and/or" does not
        // double-count a single permanent).
        var artEnch = new Artifact("Artifact Enchantment", "{2}");
        artEnch.AddCardType(CardType.Enchantment);
        artEnch.SetOwner(_alice);
        artEnch.SetController(_alice);
        artEnch.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(artEnch);

        AllThatGlittersFactory.CountArtifactsAndEnchantments(aura).Should().Be(2,
            "aura + the single artifact-enchantment (counted once, not twice)");
    }

    // -----------------------------------------------------------------------
    // Cast-time targeting — "Enchant creature"
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_OnlyCreaturesAreLegalTargets()
    {
        var aura = AllThatGlittersFactory.Create(_alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        var land = new Land("Plains");

        var battlefield = new Permanent[] { bear, land };
        var def = AllThatGlittersFactory.BuildSpellDefinition(aura, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(bear);
        candidates.Should().NotContain(land, "Enchant creature — non-creatures fail");
    }
}
