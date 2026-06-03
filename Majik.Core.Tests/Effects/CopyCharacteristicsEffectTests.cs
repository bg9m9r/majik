using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
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
    public void Copy_OfLegendarySource_SurfacesCopiedLegendarySupertype()
    {
        var svc = new ContinuousEffectsService();
        var land = BattlefieldLand(_alice);

        // Source is a Legendary creature. CR 707.2 — supertypes are copiable.
        var legend = new Creature("Kytheon", "{W}", 2, 1,
            supertypes: new[] { CardSupertype.Legendary });

        svc.Register(new CopyCharacteristicsEffect(land, legend));

        // #1715 slot — copied Legendary surfaces through Compute, so the
        // legend-rule SBA (reads HasEffectiveSupertype) now counts it.
        svc.Compute(land).Supertypes.Should().Contain(CardSupertype.Legendary);
    }

    [Fact]
    public void Copy_OntoCreature_SurfacesCopiedColorAndSupertype()
    {
        var svc = new ContinuousEffectsService();
        // Copier is itself a creature so Compute seeds a CreatureCharacteristics.
        var copier = new Creature("Clone", "{3}{U}", 0, 0)
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = svc,
            Zone = Majik.Core.Zones.ZoneType.Battlefield,
        };
        // Source: a mono-green Legendary bear.
        var original = new Creature("Legendary Bear", "{1}{G}", 2, 2,
            supertypes: new[] { CardSupertype.Legendary });

        svc.Register(new CopyCharacteristicsEffect(copier, original));

        // #1681 Layer-5 colour slot — the copy now copies the source's colour.
        copier.GetEffectiveColors().Should().BeEquivalentTo(new[] { ManaColor.Green });
        copier.GetEffectiveColors().Should().NotContain(ManaColor.Blue,
            "the clone's printed blue is overwritten by the copied green");
        // #1715 supertype slot — and copies Legendary.
        copier.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    [Fact]
    public void Copy_OfColorlessSource_ClearsTargetPrintedColor()
    {
        var svc = new ContinuousEffectsService();
        var copier = new Creature("Clone", "{3}{U}", 0, 0)
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = svc,
            Zone = Majik.Core.Zones.ZoneType.Battlefield,
        };
        // CR 707.2 — copying a colourless artifact creature makes the copy
        // colourless (the printed blue is overwritten, not unioned).
        var golem = new Creature("Colorless Golem", "{4}", 3, 3);

        svc.Register(new CopyCharacteristicsEffect(copier, golem));

        copier.GetEffectiveColors().Should().BeEmpty(
            "a copy of a colourless source is colourless");
    }

    // -----------------------------------------------------------------------
    // Arbitrary printed activated / triggered abilities (deferral pay-down)
    // CR 707.2 — a copy gets the source's printed abilities, re-instantiated
    // bound to the copy (not just the source's keyword markers).
    // -----------------------------------------------------------------------

    [Fact]
    public void RegisterCopy_DefaultRebind_MirrorsSourcesPrintedActivatedAbility_BoundToTarget()
    {
        var svc = new ContinuousEffectsService();
        var land = BattlefieldLand(_alice);
        land.ActiveEffects = svc;

        // Source: a creature card whose printed text includes a non-keyword
        // activated ability. The DEFAULT rebind (the production path) re-creates
        // it bound to the TARGET via ActivatedAbility.RebindTo (CR 707.2).
        var source = new Creature("Prodigal Sorcerer", "{2}{U}", 1, 1) { Owner = _alice };
        var printed = new ActivatedAbility(
            source: source, controller: _alice,
            effects: System.Array.Empty<IEffect>());
        source.AddAbility(printed);

        CopyCharacteristicsEffect.RegisterCopy(
            svc, land, source,
            abilityRebind: CopyCharacteristicsEffect.DefaultAbilityRebind);

        svc.Compute(land); // drives the GrantAbilityEffect sync

        var granted = land.Abilities.OfType<ActivatedAbility>().Single();
        granted.Source.Should().BeSameAs(land,
            "the copied activated ability is re-instantiated bound to the copy");
        granted.Should().NotBeSameAs(printed,
            "a fresh instance is built — the source's own ability instance is untouched");
        source.Abilities.OfType<ActivatedAbility>().Single().Source.Should().BeSameAs(source,
            "the SOURCE keeps its own ability bound to itself");
    }

    [Fact]
    public void RegisterCopy_DefaultRebind_MirrorsSourcesPrintedTriggeredAbility_BoundToTarget()
    {
        var svc = new ContinuousEffectsService();
        var land = BattlefieldLand(_alice);
        land.ActiveEffects = svc;

        var source = new Creature("Trigger Bear", "{1}{G}", 2, 2) { Owner = _alice };
        source.AddAbility(new TriggeredAbility(
            source: source, controller: _alice,
            condition: Triggers.OnEnterBattlefieldSelf(source)));

        CopyCharacteristicsEffect.RegisterCopy(
            svc, land, source,
            abilityRebind: CopyCharacteristicsEffect.DefaultAbilityRebind);

        svc.Compute(land);

        var granted = land.Abilities.OfType<TriggeredAbility>().Single();
        granted.Source.Should().BeSameAs(land,
            "the copied triggered ability is re-instantiated bound to the copy");
    }

    [Fact]
    public void RegisterCopy_DefaultRebind_SkipsKeywordAndManaAbilities()
    {
        var svc = new ContinuousEffectsService();
        var land = BattlefieldLand(_alice);
        land.ActiveEffects = svc;

        var source = new Creature("Mixed", "{1}{G}", 2, 2) { Owner = _alice };
        // Keyword markers go through the layer pass (not the ability grant);
        // mana abilities are not "printed activated abilities" for this purpose.
        source.AddAbility(new KeywordAbility("Flying", source, _alice));
        source.AddAbility(new ActivatedAbility(
            source: source, controller: _alice, effects: System.Array.Empty<IEffect>()));

        CopyCharacteristicsEffect.RegisterCopy(
            svc, land, source,
            abilityRebind: CopyCharacteristicsEffect.DefaultAbilityRebind);

        svc.Compute(land);

        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "only the non-keyword activated ability is mirrored as a grant");
        land.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "keyword markers surface through the characteristic keyword set, not as granted abilities");
        // The copied Flying keyword surfaces through Compute's keyword set.
        svc.Compute(land).Keywords.Should().Contain("Flying");
    }

    [Fact]
    public void RegisterCopy_DefaultRebind_MirroredAbilities_RevokedAtEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var land = BattlefieldLand(_alice);
        land.ActiveEffects = svc;

        var source = new Creature("Prodigal Sorcerer", "{2}{U}", 1, 1) { Owner = _alice };
        source.AddAbility(new ActivatedAbility(
            source: source, controller: _alice,
            effects: System.Array.Empty<IEffect>()));

        CopyCharacteristicsEffect.RegisterCopy(
            svc, land, source, expiresAtEndOfTurn: true,
            abilityRebind: CopyCharacteristicsEffect.DefaultAbilityRebind);

        svc.Compute(land);
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);

        // CR 514.2 — cleanup lifts the until-EOT copy AND its mirrored abilities.
        svc.ExpireEndOfTurn();
        svc.Compute(land);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the mirrored ability is revoked when the until-EOT copy expires");
    }

    [Fact]
    public void Copy_OntoCreature_SurfacesCopiedNameThroughEffectiveName()
    {
        var svc = new ContinuousEffectsService();
        var copier = new Creature("Clone", "{3}{U}", 0, 0)
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = svc,
            Zone = Majik.Core.Zones.ZoneType.Battlefield,
        };
        var original = new Creature("Grizzly Bears", "{1}{G}", 2, 2);

        svc.Register(new CopyCharacteristicsEffect(copier, original));

        // CR 707.2 — name is a copiable value. The printed Card.Name stays
        // immutable ("Clone"), but the EFFECTIVE name surfaces the copy so
        // "another permanent named X" / same-name matching counts the clone.
        copier.Name.Should().Be("Clone", "the printed name is immutable (CR 707.2 records the copy in the layer system, not on the card)");
        copier.GetEffectiveName().Should().Be("Grizzly Bears");
    }

    [Fact]
    public void Copy_OntoCreature_SurfacesCopiedManaCostThroughEffectiveManaCost()
    {
        var svc = new ContinuousEffectsService();
        var copier = new Creature("Clone", "{3}{U}", 0, 0)
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = svc,
            Zone = Majik.Core.Zones.ZoneType.Battlefield,
        };
        var original = new Creature("Grizzly Bears", "{1}{G}", 2, 2);

        svc.Register(new CopyCharacteristicsEffect(copier, original));

        // CR 707.2 — mana cost is copiable; the EFFECTIVE mana cost surfaces
        // the source's "{1}{G}" (mana value 2) for X-cost / mana-value reads.
        copier.GetEffectiveManaCost().Should().Be("{1}{G}");
    }

    [Fact]
    public void EffectiveName_WithoutCopy_FallsBackToPrintedName()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = svc,
            Zone = Majik.Core.Zones.ZoneType.Battlefield,
        };

        // No copy effect → effective reads fall through to the printed values.
        bear.GetEffectiveName().Should().Be("Grizzly Bears");
        bear.GetEffectiveManaCost().Should().Be("{1}{G}");
    }

    [Fact]
    public void EffectiveName_WithNullActiveEffects_FallsBackToPrintedName()
    {
        // No ActiveEffects wired at all → printed/static fallback path.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.GetEffectiveName().Should().Be("Grizzly Bears");
        bear.GetEffectiveManaCost().Should().Be("{1}{G}");
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
