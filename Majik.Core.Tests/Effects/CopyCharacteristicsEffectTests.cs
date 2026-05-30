using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 707.2 / 613.2 (Layer 1) — generalized "becomes a copy" continuous
/// effect that copies the full copiable characteristics of ANY permanent
/// card (creature, artifact, enchantment, land) onto a target permanent
/// in place, with an "until end of turn" duration (CR 514.2).
/// </summary>
public class CopyCharacteristicsEffectTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land BattlefieldLand(Player owner, string name = "Some Land")
    {
        var land = new Land(name);
        land.SetOwner(owner);
        land.SetController(owner);
        land.Zone = Majik.Core.Zones.ZoneType.Battlefield;
        return land;
    }

    [Fact]
    public void Copy_OfNoncreatureArtifact_CopiesTypesAndSubtypes()
    {
        var svc = new ContinuousEffectsService();
        var land = BattlefieldLand(_alice);

        // Source: an Artifact card (Equipment subtype).
        var artifact = new Artifact("Bonesplitter", "{1}",
            subtypes: new[] { CardSubtype.Equipment });

        svc.Register(new CopyCharacteristicsEffect(land, artifact));

        var chars = svc.Compute(land);
        chars.Types.Should().Contain(CardType.Artifact);
        chars.Subtypes.Should().Contain(CardSubtype.Equipment);
        // Printed land type is REPLACED by the copy (CR 707.2 — copiable
        // values overwrite, they don't add). The land is no longer a Land.
        chars.Types.Should().NotContain(CardType.Land);
    }

    [Fact]
    public void Copy_OfCreature_CopiesCreatureTypeAndPT()
    {
        var svc = new ContinuousEffectsService();
        var land = BattlefieldLand(_alice);

        var goyf = new Creature("Hill Giant", "{3}{R}", 3, 3);

        var effect = new CopyCharacteristicsEffect(land, goyf);
        svc.Register(effect);

        var chars = svc.Compute(land);
        chars.Types.Should().Contain(CardType.Creature);
        // P/T of the copy is exposed for inspection (the manland-on-a-Land
        // Compute gap means a Land row carries no P/T fields, same as
        // CreepingTarPit). The effect records the copied P/T.
        effect.CopiedPower.Should().Be(3);
        effect.CopiedToughness.Should().Be(3);
    }

    [Fact]
    public void Copy_OntoCreature_SurfacesCopiedPTThroughCompute()
    {
        var svc = new ContinuousEffectsService();
        // Copier is itself a creature so Compute seeds a CreatureCharacteristics.
        var copier = new Creature("Clone", "{3}{U}", 0, 0)
        {
            ActiveEffects = svc,
            Zone = Majik.Core.Zones.ZoneType.Battlefield,
        };
        var original = new Creature("Bear", "{1}{G}", 2, 2);

        svc.Register(new CopyCharacteristicsEffect(copier, original));

        copier.Power.Should().Be(2);
        copier.Toughness.Should().Be(2);
    }

    [Fact]
    public void Copy_CopiesKeywordAbilities()
    {
        var svc = new ContinuousEffectsService();
        var land = BattlefieldLand(_alice);

        var flyer = new Creature("Air Elemental", "{3}{U}{U}", 4, 4) { Owner = _alice };
        flyer.AddAbility(new KeywordAbility("Flying", flyer, _alice));

        svc.Register(new CopyCharacteristicsEffect(land, flyer));

        var chars = svc.Compute(land);
        chars.Keywords.Should().Contain("Flying");
    }

    [Fact]
    public void Copy_ExposesCopiedNameAndManaCost()
    {
        var svc = new ContinuousEffectsService();
        var land = BattlefieldLand(_alice);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);

        var effect = new CopyCharacteristicsEffect(land, bear);

        // CR 707.2 — name + mana cost are copiable values. Exposed for
        // inspection (Card.Name is immutable in v1; the effect records the
        // copied identity).
        effect.CopiedName.Should().Be("Grizzly Bears");
        effect.CopiedManaCost.Should().Be("{1}{G}");
    }

    [Fact]
    public void Copy_ExpiresAtEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var land = BattlefieldLand(_alice);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);

        svc.Register(new CopyCharacteristicsEffect(land, bear, expiresAtEndOfTurn: true));
        svc.Compute(land).Types.Should().Contain(CardType.Creature);

        // CR 514.2 — cleanup step lifts the "until end of turn" copy.
        svc.ExpireEndOfTurn();

        var chars = svc.Compute(land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
    }
}
