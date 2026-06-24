using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="TorchTheWitnessFactory"/> — Torch the Witness
/// (Murders at Karlov Manor, {X}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Torch the Witness deals twice X damage to target creature. If excess
///    damage was dealt to that creature this way, investigate. (Create a
///    Clue token. It's an artifact with '{2}, Sacrifice this token: Draw a
///    card.')"
///
/// Covers the card's UNIQUE behaviour:
/// - Identity: {X}{R}, mono-red Sorcery.
/// - SpellDefinition: HasVariableX=true, one 1..1 target-creature request.
/// - 2X damage (X=2 → 4 damage) to the chosen creature.
/// - Excess damage (CR 120.10 / 121.x) → investigate (one Clue token).
/// - Exactly-lethal damage (no excess) → NO Clue token.
/// - Sub-lethal damage → NO Clue token.
/// - Excess considers damage already marked this turn (ruling).
/// - X=0 → 0 damage, no Clue.
/// </summary>
[Trait("Color", "R")]
public class TorchTheWitnessFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static object IdentityResolver(object t) => t;

    private static ChosenSpellParams MakeChosen(int x, params object[] targets) =>
        new(
            ModeIndex: null,
            X: x,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty);

    private static int ClueCount(Player p) =>
        p.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Count(a => a.Name == "Clue");

    // ── identity ────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ShipsSorceryShape_XR_Red()
    {
        var card = TorchTheWitnessFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Torch the Witness");
        card.ManaCost.Should().Be("{X}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.Blue);
    }

    // ── SpellDefinition shape ───────────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_HasVariableX_AndSingleCreatureTarget()
    {
        var def = TorchTheWitnessFactory.BuildSpellDefinition(_alice, IdentityResolver, zoneService: null);

        def.HasVariableX.Should().BeTrue("Torch the Witness is an X-spell");
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ── resolution — 2X damage ──────────────────────────────────────────────

    [Fact]
    public void Resolve_X2_DealsTwiceX_4Damage()
    {
        // X=2 → twice X = 4 damage. Tough enough to survive so no excess.
        var wall = new Creature("Big Wall", "{4}", 0, 8);
        PutOnBattlefield(_bob, wall);

        var def = TorchTheWitnessFactory.BuildSpellDefinition(_alice, IdentityResolver, zoneService: null);
        foreach (var e in def.EffectFactory(MakeChosen(2, wall))) e.Execute();

        wall.Damage.Should().Be(4, "twice X = 2*2 = 4");
        ClueCount(_alice).Should().Be(0, "4 < toughness 8 → no excess → no investigate");
    }

    // ── resolution — excess → investigate ───────────────────────────────────

    [Fact]
    public void Resolve_ExcessDamage_Investigates_OneClue()
    {
        // X=3 → 6 damage to a 2/2. Lethal = 2, dealt 6 → 4 excess → investigate.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_bob, bear);

        var def = TorchTheWitnessFactory.BuildSpellDefinition(_alice, IdentityResolver, zoneService: null);
        foreach (var e in def.EffectFactory(MakeChosen(3, bear))) e.Execute();

        bear.Damage.Should().Be(6, "twice X = 6");
        ClueCount(_alice).Should().Be(1,
            "6 damage to a 2/2 is excess (CR 120.10) → the controller investigates");
    }

    [Fact]
    public void Resolve_ExactlyLethal_NoExcess_NoClue()
    {
        // X=2 → 4 damage to a 4-toughness creature. Lethal = 4, dealt 4 →
        // no excess → no investigate.
        var ogre = new Creature("Ogre", "{3}{R}", 4, 4);
        PutOnBattlefield(_bob, ogre);

        var def = TorchTheWitnessFactory.BuildSpellDefinition(_alice, IdentityResolver, zoneService: null);
        foreach (var e in def.EffectFactory(MakeChosen(2, ogre))) e.Execute();

        ogre.Damage.Should().Be(4);
        ClueCount(_alice).Should().Be(0, "exactly lethal — no excess → no Clue");
    }

    [Fact]
    public void Resolve_SubLethal_NoClue()
    {
        // X=1 → 2 damage to a 5-toughness creature. Not lethal → no excess.
        var giant = new Creature("Hill Giant", "{3}{R}", 3, 5);
        PutOnBattlefield(_bob, giant);

        var def = TorchTheWitnessFactory.BuildSpellDefinition(_alice, IdentityResolver, zoneService: null);
        foreach (var e in def.EffectFactory(MakeChosen(1, giant))) e.Execute();

        giant.Damage.Should().Be(2);
        ClueCount(_alice).Should().Be(0, "2 < toughness 5 → no excess → no Clue");
    }

    [Fact]
    public void Resolve_ExcessConsidersDamageAlreadyMarked()
    {
        // 3-toughness creature already has 2 damage marked. Lethal needed = 1.
        // X=1 → 2 damage. 2 > 1 → excess → investigate.
        var bear = new Creature("Wounded Bear", "{1}{G}", 2, 3);
        PutOnBattlefield(_bob, bear);
        bear.TakeDamage(2); // pre-existing damage this turn

        var def = TorchTheWitnessFactory.BuildSpellDefinition(_alice, IdentityResolver, zoneService: null);
        foreach (var e in def.EffectFactory(MakeChosen(1, bear))) e.Execute();

        bear.Damage.Should().Be(4, "2 prior + 2 from Torch");
        ClueCount(_alice).Should().Be(1,
            "lethal needed was 1 (3 toughness - 2 marked); 2 dealt → excess → investigate");
    }

    // ── X=0 no-op ───────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_X0_NoDamage_NoClue()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_bob, bear);

        var def = TorchTheWitnessFactory.BuildSpellDefinition(_alice, IdentityResolver, zoneService: null);
        foreach (var e in def.EffectFactory(MakeChosen(0, bear))) e.Execute();

        bear.Damage.Should().Be(0, "twice X = 0");
        ClueCount(_alice).Should().Be(0, "no damage dealt → no excess → no Clue");
    }
}
