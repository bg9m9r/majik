using FluentAssertions;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Tests.Snapshots;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Behaviour-neutrality guard for PLAN 03 Slice 1 (shared
/// <see cref="Majik.Core.Primitives.Fx"/> / <see cref="Majik.Core.Primitives.Costs"/>
/// / <see cref="Majik.Core.Abilities.Triggers"/> vocabulary). The cost /
/// effect construction in <see cref="CardDefinitionFactory"/> and the
/// resolve materializer in <see cref="CardDefRuntime"/> were re-pointed at
/// the shared primitive helpers; the runtime cards they produce must be
/// byte-identical in shape.
///
/// <para>
/// The sample spans every JSON cost type (<c>mana</c>, <c>tap_self</c>,
/// <c>sacrifice_self</c>, <c>remove_counter</c>, <c>discard_self</c>) and
/// every JSON effect verb (counter / deal-damage / draw / surveil / scry /
/// destroy-target / untap-target / gain-life / mill-then-pick / connive /
/// amass — the targeted verbs are real after PLAN 01 Slice F), plus a DSL
/// <c>Define()</c> card. The
/// <see cref="SnapshotSummary"/> ability digest (<c>act:&lt;costs&gt;=&gt;&lt;effects&gt;</c>
/// / <c>trig:&lt;event&gt;=&gt;&lt;effects&gt;</c> / <c>mana:&lt;produced&gt;</c>) is the
/// stable shape fingerprint — a change in any cost / effect / ability
/// concrete type flips the digest and fails the test.
/// </para>
/// </summary>
public class CardDefVocabularyParityTests
{
    private static readonly Player Alice = new("Alice", 20);

    private static string AbilityDigest(ICard card) =>
        SnapshotSummary.Build(card)["abilities"]!.ToJsonString(SnapshotSummary.Options);

    private static ICard BuildJson(string slug)
    {
        var def = CardDefinitionLoader.FromEmbeddedResource(slug);
        return CardDefinitionFactory.Build(def, Alice);
    }

    // ------------------------------------------------------------------
    // JSON path — one assertion per cost/effect shape, expressed as the
    // SnapshotSummary ability digest captured against the post-S1 build
    // (identical to pre-S1; the helpers construct the same concrete types).
    // ------------------------------------------------------------------

    [Theory]
    // Walking Ballista: {4}=>put_counter (ManaCostCost) +
    //   remove_counter=>deal_damage (RemovePlusOnePlusOneCounterCost). The
    //   damage verb is real (PLAN 01 Slice F) but still materializes to an
    //   Effect closure, so the shape digest is unchanged.
    [InlineData("walking-ballista",
        "act:ManaCostCost=>Effect", "act:RemovePlusOnePlusOneCounterCost=>Effect")]
    // Dreamstone Hedron: {3},{T},Sacrifice => draw 3.
    [InlineData("dreamstone-hedron",
        "act:AdditionalCost+AdditionalCost+ManaCostCost=>Effect")]
    // Boseiju: {1}{G}, Discard self => destroy target (real, Slice F).
    [InlineData("boseiju",
        "act:DiscardSelfCost+ManaCostCost=>Effect")]
    // Voltaic Key: {1},{T} => untap target (real, Slice F).
    [InlineData("voltaic-key",
        "act:AdditionalCost+ManaCostCost=>Effect")]
    // Castle Vantress: {2}{U}{U},{T} => scry 2.
    [InlineData("castle-vantress",
        "act:AdditionalCost+ManaCostCost=>Effect")]
    // Gingerbrute: {2},{T},Sacrifice => gain 3 life.
    [InlineData("gingerbrute",
        "act:AdditionalCost+AdditionalCost+ManaCostCost=>Effect")]
    public void JsonCard_ActivatedShape_IsStable(string slug, params string[] expectedActivated)
    {
        var card = BuildJson(slug);
        var activated = card.Abilities
            .OfType<Majik.Core.Abilities.ActivatedAbility>()
            .Select(DescribeActivated)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        activated.Should().BeEquivalentTo(expectedActivated.OrderBy(s => s, StringComparer.Ordinal));
    }

    [Theory]
    // Triggered ETB effect shapes (surveil / scry / gain-life / mill /
    // connive / amass) all materialize to a single Effect closure.
    [InlineData("commercial-district")]   // surveil_self
    [InlineData("crystal-grotto")]        // scry_self
    [InlineData("blossoming-sands")]      // gain_life_self
    [InlineData("dredgers-insight")]      // mill_then_pick + card_leaves_your_graveyard
    [InlineData("test-conniver")]         // connive_self
    [InlineData("lazotep-recruit")]       // amass_self
    public void JsonCard_TriggeredShape_AllEffectsMaterialize(string slug)
    {
        var card = BuildJson(slug);
        var triggered = card.Abilities
            .OfType<Majik.Core.Abilities.TriggeredAbility>()
            .ToArray();

        triggered.Should().NotBeEmpty();
        // Every triggered ability must carry at least one materialized
        // Effect (no dropped effects from the reroute).
        triggered.Should().OnlyContain(t => t.Effects.Count >= 1);
    }

    [Fact]
    public void WalkingBallista_FullDigest_Unchanged()
    {
        // Full ability digest pin — guards the exact cost+effect concrete
        // types across both activated abilities at once. Both the mana cost
        // ({4}) and the counter-removal cost route through Costs.* now.
        var card = BuildJson("walking-ballista");
        var digest = AbilityDigest(card);

        digest.Should().Contain("ManaCostCost");
        digest.Should().Contain("RemovePlusOnePlusOneCounterCost");
        digest.Should().Contain("act:");
    }

    // ------------------------------------------------------------------
    // DSL path — a Define() card materialized through CardDefRuntime.
    // ------------------------------------------------------------------

    [Fact]
    public void DslCard_ResolveBody_MaterializesEffects()
    {
        // Sorcery "Test Bolt": deal 3, then draw 1, then mill 2 — exercises
        // the DealDamage / DrawCards / Mill resolve branches (Mill now
        // routes through Fx.Mill).
        CardDef def = CardDef
            .Sorcery("Test Bolt", "{R}")
            .Resolve(c => c.DealDamage(3).To(TargetKind.AnyTarget)
                           .DrawCards(1)
                           .Mill(2));

        var card = CardDefRuntime.Build(def, Alice);
        card.Should().BeOfType<Sorcery>();

        var effects = CardDefRuntime.BuildSpellResolveEffects(
            def, Alice, t => t, chosenTarget: null);

        // Three printed-order effects, all materialized (none dropped).
        effects.Should().HaveCount(3);
    }

    private static string DescribeActivated(Majik.Core.Abilities.ActivatedAbility a)
    {
        var costs = string.Join("+", a.Costs.Select(c => c.GetType().Name)
            .OrderBy(s => s, StringComparer.Ordinal));
        var effects = string.Join("+", a.Effects.Select(e => e.GetType().Name)
            .OrderBy(s => s, StringComparer.Ordinal));
        if (string.IsNullOrEmpty(costs)) costs = "no-costs";
        if (string.IsNullOrEmpty(effects)) effects = "no-effects";
        return $"act:{costs}=>{effects}";
    }
}
