using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="NikoLightOfHopeFactory"/>.
///
/// Oracle (Scryfall-confirmed 2026-06-23, Kaldheim):
///   "When Niko enters, create two Shard tokens. (They're enchantments with
///    "{2}, Sacrifice this token: Scry 1, then draw a card.")
///    {2}, {T}: Exile target nonlegendary creature you control. Shards you
///    control become copies of it until the next end step. Return it to the
///    battlefield under its owner's control at the beginning of the next end
///    step."
///
/// Covers ONLY Niko's unique behaviour (the contract test covers dispatch +
/// well-formedness):
/// - Identity: {2}{W}{U}, 3/4, Legendary Creature — Human Wizard.
/// - ETB makes two Shard enchantment tokens, each with the {2}+sac scry-draw
///   ability.
/// - Shard sac ability: scry 1 (default keep-on-top) then draw a card +
///   sacrifices itself.
/// - {2},{T} ability shape: mana + tap costs, 1..1 nonlegendary-creature
///   target; the nonlegendary filter excludes legendary creatures.
/// - Exile/copy/return resolution: target exiled, Shards become a copy of it,
///   copy expires at end of turn.
/// </summary>
[Trait("Color", "M")]
public class NikoLightOfHopeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ActivatedAbility ExileAbility(Creature niko) =>
        niko.Abilities.OfType<ActivatedAbility>().Single();

    private static TriggeredAbility EtbTrigger(Creature niko) =>
        niko.Abilities.OfType<TriggeredAbility>().Single();

    private static IReadOnlyList<Enchantment> Shards(Player p) =>
        p.Zones.Battlefield.GetCards()
            .OfType<Enchantment>()
            .Where(e => e.Name == NikoLightOfHopeFactory.ShardName)
            .ToList();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_Identity()
    {
        var niko = NikoLightOfHopeFactory.Create(_alice);

        niko.Name.Should().Be("Niko, Light of Hope");
        niko.ManaCost.Should().Be("{2}{W}{U}");
        niko.GetPower().Should().Be(3);
        niko.GetToughness().Should().Be(4);
        niko.HasType(CardType.Creature).Should().BeTrue();
        niko.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        niko.HasSubtype(CardSubtype.Human).Should().BeTrue();
        niko.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB — create two Shard tokens
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_CreatesTwoShardEnchantmentTokens()
    {
        var niko = NikoLightOfHopeFactory.Create(_alice, effects: null, triggers: null, zones: null);
        niko.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(niko);

        EtbTrigger(niko).Resolve();

        var shards = Shards(_alice);
        shards.Should().HaveCount(2, "Niko's ETB creates two Shard tokens");
        shards.Should().AllSatisfy(s =>
        {
            s.IsToken.Should().BeTrue();
            s.HasType(CardType.Enchantment).Should().BeTrue("Shards are enchantments");
            s.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
                "each Shard has \"{2}, Sacrifice this token: Scry 1, then draw a card\"");
        });
    }

    // -----------------------------------------------------------------------
    // Shard sac ability — scry 1, then draw, sacrifice self
    // -----------------------------------------------------------------------

    [Fact]
    public void ShardSacAbility_HasManaCost_AndIsActivated()
    {
        var shard = NikoLightOfHopeFactory.CreateShard(_alice);
        var ability = shard.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Description.Should().Contain("2");
    }

    [Fact]
    public void ShardSacAbility_Resolve_DrawsACard_AndSacrificesSelf()
    {
        var shard = NikoLightOfHopeFactory.CreateShard(_alice);

        // Two cards in the library so the draw succeeds (and scry has something
        // to peek at).
        var top = new Creature("Top", "{G}", 1, 1) { Owner = _alice };
        var second = new Creature("Second", "{G}", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        _alice.Zones.Library.AddCard(second);

        var ability = shard.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        // CR 701.20 default keep-on-top: top stays on top and is the card drawn.
        _alice.Zones.Hand.GetCards().Should().Contain(top, "scry 1 (keep on top) then draw a card");
        // CR 701.16 — the Shard sacrificed itself.
        shard.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(shard);
    }

    // -----------------------------------------------------------------------
    // {2},{T} ability shape + nonlegendary-creature target filter
    // -----------------------------------------------------------------------

    [Fact]
    public void ExileAbility_HasManaAndTapCost_AndNonlegendaryCreatureTarget()
    {
        var niko = NikoLightOfHopeFactory.Create(_alice);
        var ability = ExileAbility(niko);

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        ability.Costs.OfType<AdditionalCost>().Should().Contain(c => c.Description.Contains("Tap"));

        ability.TargetRequests.Should().ContainSingle();
        var req = ability.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("nonlegendary");
    }

    [Fact]
    public void NonlegendaryCreaturesYouControl_ExcludesLegendaryAndOpponentCreatures()
    {
        var niko = NikoLightOfHopeFactory.Create(_alice);
        niko.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(niko); // legendary — excluded

        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var bob = new Player("Bob", 20);
        var bobBear = new Creature("Bob's Bear", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bobBear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobBear);

        var candidates = NikoLightOfHopeFactory.NonlegendaryCreaturesYouControl(_alice);

        candidates.Should().ContainSingle().Which.Should().BeSameAs(bear,
            "only Alice's nonlegendary creature qualifies — Niko is legendary, Bob's bear isn't controlled by Alice");
    }

    // -----------------------------------------------------------------------
    // Exile / copy / return resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void ExileAbility_Resolve_ExilesTarget_AndShardsBecomeCopies()
    {
        var effects = new ContinuousEffectsService();
        var niko = NikoLightOfHopeFactory.Create(_alice, effects, triggers: null, zones: null);
        niko.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(niko);

        // Two Shards on the battlefield (built directly to avoid the ETB plumbing).
        var shard1 = NikoLightOfHopeFactory.CreateShard(_alice);
        var shard2 = NikoLightOfHopeFactory.CreateShard(_alice);

        // The nonlegendary creature to exile.
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 4, 5,
            subtypes: new[] { CardSubtype.Lhurgoyf }) { Owner = _alice, Controller = _alice };
        goyf.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(goyf);

        var ability = ExileAbility(niko);
        ability.SetChosenTargets(new[] { new object[] { goyf } });
        ability.Resolve();

        // CR 701.21 — the target was exiled.
        goyf.Zone.Should().Be(ZoneType.Exile);

        // CR 707.2 — each Shard is now a copy of the exiled creature.
        foreach (var shard in new[] { shard1, shard2 })
        {
            var chars = effects.Compute(shard);
            chars.Types.Should().Contain(CardType.Creature, "the Shard became a copy of Tarmogoyf");
            chars.Subtypes.Should().Contain(CardSubtype.Lhurgoyf);
            chars.Types.Should().NotContain(CardType.Enchantment, "copiable values overwrite the type line");
        }
    }

    [Fact]
    public void ExileAbility_Resolve_ShardCopyExpiresAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var niko = NikoLightOfHopeFactory.Create(_alice, effects, triggers: null, zones: null);
        niko.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(niko);

        var shard = NikoLightOfHopeFactory.CreateShard(_alice);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var ability = ExileAbility(niko);
        ability.SetChosenTargets(new[] { new object[] { bear } });
        ability.Resolve();

        effects.Compute(shard).Types.Should().Contain(CardType.Creature, "copy active");

        // CR 514.2 — "until the next end step" lifts at the cleanup step.
        effects.ExpireEndOfTurn();

        var chars = effects.Compute(shard);
        chars.Types.Should().Contain(CardType.Enchantment, "the copy expired; Shard is an Enchantment again");
        chars.Types.Should().NotContain(CardType.Creature);
    }

    [Fact]
    public void ExileAbility_Resolve_NoOp_WhenTargetIsLegendary()
    {
        var effects = new ContinuousEffectsService();
        var niko = NikoLightOfHopeFactory.Create(_alice, effects, triggers: null, zones: null);
        niko.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(niko);

        var shard = NikoLightOfHopeFactory.CreateShard(_alice);

        // A legendary creature — the resolution-time filter must reject it.
        var legend = new Creature("Emrakul", "{15}", 15, 15,
            supertypes: new[] { CardSupertype.Legendary }) { Owner = _alice, Controller = _alice };
        legend.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(legend);

        var ability = ExileAbility(niko);
        ability.SetChosenTargets(new[] { new object[] { legend } });
        ability.Resolve();

        legend.Zone.Should().Be(ZoneType.Battlefield, "a legendary creature is not a legal target — no exile");
        effects.Compute(shard).Types.Should().Contain(CardType.Enchantment, "no copy registered");
    }
}
