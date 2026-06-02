using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VexingShusherFactory"/>.
///
/// Vexing Shusher (Shadowmoor), Creature — Goblin Shaman 2/2, {R/G}{R/G}.
/// Oracle text (Scryfall, verified):
///   "This spell can't be countered.
///    {R/G}: Target spell can't be countered."
///
/// Covers:
/// - Identity (name, {R/G}{R/G} cost, Goblin + Shaman subtypes, 2/2,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Cast-uncounterable self marker (CR 701.5b — "This spell can't be
///   countered") wired as a KeywordAbility("Uncounterable") read by
///   SpellCastFlow, same posture as Emrakul, the Aeons Torn.
/// - Activated ability "{R/G}: Target spell can't be countered" present
///   with a single {R/G} mana cost and a 1-target spell request.
/// - Resolving the activated ability stamps the chosen spell's
///   CannotBeCountered flag (CR 701.5b).
/// </summary>
[Trait("Color", "M")]
public class VexingShusherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void VexingShusher_Identity()
    {
        var c = VexingShusherFactory.Create(_alice);

        c.Name.Should().Be("Vexing Shusher");
        c.ManaCost.Should().Be("{R/G}{R/G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // ── "This spell can't be countered" self marker ──────────────────────

    [Fact]
    public void VexingShusher_HasUncounterableSelfMarker()
    {
        // CR 701.5b — "This spell can't be countered." Wired as the
        // KeywordAbility("Uncounterable") marker that SpellCastFlow reads
        // at cast time to stamp Spell.CannotBeCountered (same shape as
        // Emrakul, the Aeons Torn).
        var c = VexingShusherFactory.Create(_alice);

        var marker = c.Abilities.OfType<KeywordAbility>()
            .FirstOrDefault(k => k.Keyword == "Uncounterable");

        marker.Should().NotBeNull(
            "\"This spell can't be countered\" is wired as the Uncounterable marker.");
    }

    // ── Activated ability shape ──────────────────────────────────────────

    [Fact]
    public void VexingShusher_HasTargetedUncounterableActivatedAbility()
    {
        var c = VexingShusherFactory.Create(_alice);

        var activated = c.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1,
            "Vexing Shusher has exactly one activated ability ({R/G}: Target spell can't be countered).");

        var ability = activated[0];
        // CR 107.4e — {R/G} parses to exactly one hybrid pip (R or G).
        // ManaCost.ToString() omits hybrid pips, so assert on the parsed
        // HybridPips collection, the real signal that the cost is {R/G}.
        var cost = ability.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Cost;
        cost.HybridPips.Should().ContainSingle("the activation cost is a single {R/G} hybrid pip.");
        cost.TotalValue.Should().Be(1, "a single {R/G} pip has mana value 1.");

        ability.TargetRequests.Should().ContainSingle(
            "the ability targets exactly one spell.");
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ── Resolution stamps the chosen spell ───────────────────────────────

    [Fact]
    public void VexingShusher_Resolve_StampsTargetSpellUncounterable()
    {
        // CR 701.5b — resolving "{R/G}: Target spell can't be countered"
        // sets the chosen spell's CannotBeCountered flag.
        var shusher = VexingShusherFactory.Create(_alice);
        shusher.SetZone(ZoneType.Battlefield);

        var ability = shusher.Abilities.OfType<ActivatedAbility>().Single();

        // Bob casts some spell; it sits on the stack.
        var targetCard = new Creature("Grizzly Bears", "1G", 2, 2);
        targetCard.SetOwner(_bob);
        targetCard.SetController(_bob);
        var targetSpell = new Spell(targetCard, _bob);
        targetSpell.CannotBeCountered.Should().BeFalse(
            "freshly-cast spells are counterable by default.");

        // Alice activates and chooses the spell as the target.
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { targetSpell },
        });

        ability.Resolve();

        targetSpell.CannotBeCountered.Should().BeTrue(
            "Vexing Shusher's ability makes the target spell can't be countered.");
    }

    [Fact]
    public void VexingShusher_Resolve_NoTarget_NoOp()
    {
        // Defensive: with no chosen target the resolution is a harmless
        // no-op (the production agent always supplies a legal target since
        // MinTargets = 1, but the effect must not throw).
        var shusher = VexingShusherFactory.Create(_alice);
        var ability = shusher.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => ability.Resolve();

        act.Should().NotThrow();
    }
}
