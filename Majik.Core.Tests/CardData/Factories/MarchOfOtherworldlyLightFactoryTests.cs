using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MarchOfOtherworldlyLightFactory"/>
/// (Kamigawa: Neon Dynasty, {X}{W}).
///
/// Instant. Oracle text:
///   "As an additional cost to cast this spell, you may exile any number
///    of white cards from your hand. This spell costs {2} less to cast
///    for each card exiled this way.
///    Exile target artifact, creature, or enchantment with mana value X
///    or less."
///
/// Covers:
///   - Identity / shape / NamedCardFactory dispatch.
///   - SpellDefinition: HasVariableX=true, one target request.
///   - Cost reduction: exile N white cards → generic reduced by {2N}.
///   - Resolve: exiles artifact with MV ≤ X.
///   - Resolve: exiles creature with MV ≤ X.
///   - Resolve: exiles enchantment with MV ≤ X.
///   - Resolve: does NOT exile when MV > X (spell fizzles for that target).
///   - Land NOT a legal target (CandidateGatherer excludes lands).
///   - Planeswalker NOT a legal target (CandidateGatherer excludes PWs).
///   - BuildAdditionalCost helper wires White MarchAdditionalCost.
///   - Empty exile list (optional cost — zero reduction).
/// </summary>
[Trait("Color", "W")]
public class MarchOfOtherworldlyLightFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);
    private readonly Majik.Core.Stack.Stack _stack = new();

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static object IdentityResolver(object t) => t;

    // ── identity ────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ShipsInstantShape_XW_White()
    {
        var march = MarchOfOtherworldlyLightFactory.Create(_alice);

        march.Should().BeOfType<Instant>();
        march.Name.Should().Be("March of Otherworldly Light");
        march.ManaCost.Should().Be("{X}{W}");
        march.HasType(CardType.Instant).Should().BeTrue();
        march.Owner.Should().BeSameAs(_alice);
        march.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(march).Should().Contain(ManaColor.White);
    }
    // ── SpellDefinition shape ───────────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_HasVariableX_AndArtCreatureEnchantTarget()
    {
        var def = MarchOfOtherworldlyLightFactory.BuildSpellDefinition(IdentityResolver);

        def.HasVariableX.Should().BeTrue();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be(
            "target artifact, creature, or enchantment");
    }

    // ── cost reduction via MarchAdditionalCost ──────────────────────────────

    [Fact]
    public void BuildAdditionalCost_WiresWhiteMarchCost_OneCard_TwoReduction()
    {
        var spell = MarchOfOtherworldlyLightFactory.Create(_alice);
        var whiteCard = new Creature("White Helper", "{1}{W}", 1, 1);
        whiteCard.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(whiteCard);
        whiteCard.SetZone(ZoneType.Hand);

        var cost = MarchOfOtherworldlyLightFactory.BuildAdditionalCost(
            spell, new ICard[] { whiteCard });

        cost.Should().BeOfType<MarchAdditionalCost>();
        cost.RequiredColor.Should().Be(ManaColor.White);
        cost.ExiledCount.Should().Be(1);
        cost.ReductionAmount.Should().Be(2, "one white card exiled → {2} reduction");
        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void BuildAdditionalCost_TwoCards_FourReduction()
    {
        var spell = MarchOfOtherworldlyLightFactory.Create(_alice);
        var white1 = new Creature("Plains Walker A", "{W}", 1, 1);
        var white2 = new Creature("Plains Walker B", "{1}{W}", 2, 2);

        foreach (var c in new Card[] { white1, white2 })
        {
            c.SetOwner(_alice);
            _alice.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
        }

        var cost = MarchOfOtherworldlyLightFactory.BuildAdditionalCost(
            spell, new ICard[] { white1, white2 });

        cost.ReductionAmount.Should().Be(4, "two white cards exiled → {4} reduction");
        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void BuildAdditionalCost_EmptyList_IsLegal_NoReduction()
    {
        var spell = MarchOfOtherworldlyLightFactory.Create(_alice);

        var cost = MarchOfOtherworldlyLightFactory.BuildAdditionalCost(
            spell, Array.Empty<ICard>());

        cost.ExiledCount.Should().Be(0);
        cost.ReductionAmount.Should().Be(0);
        cost.CanPay(_alice).Should().BeTrue("March is OPTIONAL — zero exiles is legal");
    }

    // ── resolve — artifact target ───────────────────────────────────────────

    [Fact]
    public void Resolve_ArtifactWithMvLessThanX_IsExiled()
    {
        var def = MarchOfOtherworldlyLightFactory.BuildSpellDefinition(IdentityResolver);

        var chalice = new Artifact("Chalice of the Void", "{X}");  // MV=0 (X counts as 0)
        PutOnBattlefield(_bob, chalice);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 3,
            Targets: new IReadOnlyList<object>[] { new object[] { chalice } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        chalice.Zone.Should().Be(ZoneType.Exile,
            "artifact with MV=0 ≤ X=3 is exiled (CR 701.21)");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(chalice);
        _bob.Zones.Exile.GetCards().Should().Contain(chalice);
    }

    [Fact]
    public void Resolve_ArtifactWithMvEqualX_IsExiled()
    {
        var def = MarchOfOtherworldlyLightFactory.BuildSpellDefinition(IdentityResolver);

        var rod = new Artifact("Rod", "{3}");  // MV=3
        PutOnBattlefield(_bob, rod);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 3,
            Targets: new IReadOnlyList<object>[] { new object[] { rod } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        rod.Zone.Should().Be(ZoneType.Exile, "MV=3 equals X=3 — boundary condition");
    }

    [Fact]
    public void Resolve_ArtifactWithMvGreaterThanX_IsNotExiled()
    {
        var def = MarchOfOtherworldlyLightFactory.BuildSpellDefinition(IdentityResolver);

        var wurmcoil = new Artifact("Wurmcoil Engine", "{6}");  // MV=6
        PutOnBattlefield(_bob, wurmcoil);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 3,
            Targets: new IReadOnlyList<object>[] { new object[] { wurmcoil } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        wurmcoil.Zone.Should().Be(ZoneType.Battlefield,
            "MV=6 > X=3 — target fails MV check at resolution, no exile");
    }

    // ── resolve — creature target ───────────────────────────────────────────

    [Fact]
    public void Resolve_CreatureWithMvLessThanX_IsExiled()
    {
        var def = MarchOfOtherworldlyLightFactory.BuildSpellDefinition(IdentityResolver);

        var soldier = new Creature("Soldier", "{1}{W}", 2, 2);  // MV=2
        PutOnBattlefield(_bob, soldier);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 4,
            Targets: new IReadOnlyList<object>[] { new object[] { soldier } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        soldier.Zone.Should().Be(ZoneType.Exile,
            "creature MV=2 ≤ X=4 — exiled (CR 701.21)");
    }

    // ── resolve — enchantment target ────────────────────────────────────────

    [Fact]
    public void Resolve_EnchantmentWithMvLessThanX_IsExiled()
    {
        var def = MarchOfOtherworldlyLightFactory.BuildSpellDefinition(IdentityResolver);

        var blood = new Enchantment("Blood Moon", "{2}{R}");  // MV=3
        PutOnBattlefield(_bob, blood);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 5,
            Targets: new IReadOnlyList<object>[] { new object[] { blood } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        blood.Zone.Should().Be(ZoneType.Exile,
            "enchantment MV=3 ≤ X=5 — exiled");
    }

    // ── land NOT a legal target ─────────────────────────────────────────────

    [Fact]
    public void CandidateGatherer_ExcludesLands()
    {
        var def = MarchOfOtherworldlyLightFactory.BuildSpellDefinition(IdentityResolver);
        var request = def.TargetRequests[0];

        var plains = new Land("Plains");
        PutOnBattlefield(_bob, plains);

        var creature = new Creature("Bear", "{1}{G}", 2, 2);
        PutOnBattlefield(_bob, creature);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);
        var candidates = request.ResolveCandidates(ctx)
            .Cast<ICard>()
            .ToList();

        candidates.Should().NotContain(plains,
            "lands are not artifacts, creatures, or enchantments");
        candidates.Should().Contain(creature);
    }

    // ── planeswalker NOT a legal target ─────────────────────────────────────

    [Fact]
    public void CandidateGatherer_ExcludesPlaneswalkers()
    {
        var def = MarchOfOtherworldlyLightFactory.BuildSpellDefinition(IdentityResolver);
        var request = def.TargetRequests[0];

        var jace = new Planeswalker("Jace, the Mind Sculptor", "{2}{U}{U}", 3);
        PutOnBattlefield(_bob, jace);

        var artifact = new Artifact("Mox Pearl", "{0}");
        PutOnBattlefield(_bob, artifact);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);
        var candidates = request.ResolveCandidates(ctx)
            .Cast<ICard>()
            .ToList();

        candidates.Should().NotContain(jace,
            "planeswalkers are not artifacts, creatures, or enchantments");
        candidates.Should().Contain(artifact);
    }

    // ── resolution guard — off-battlefield target ────────────────────────────

    [Fact]
    public void Resolve_TargetLeavesBeforeResolution_DoesNothing()
    {
        var def = MarchOfOtherworldlyLightFactory.BuildSpellDefinition(IdentityResolver);

        var skull = new Artifact("Skull Clamp", "{1}");
        skull.SetOwner(_bob);
        skull.SetController(_bob);
        // Not placed on battlefield — already in hand (simulates target leaving BF)
        _bob.Zones.Hand.AddCard(skull);
        skull.SetZone(ZoneType.Hand);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 5,
            Targets: new IReadOnlyList<object>[] { new object[] { skull } },
            Mana: ManaPayment.Empty);

        // Should not throw — CR 608.2b illegal-target at resolution = fizzle.
        var act = () => { foreach (var e in def.EffectFactory(chosen)) e.Execute(); };
        act.Should().NotThrow();
        skull.Zone.Should().Be(ZoneType.Hand, "target was never on battlefield — no exile");
    }
}
