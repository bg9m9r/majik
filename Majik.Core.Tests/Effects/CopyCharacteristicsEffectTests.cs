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

    // -----------------------------------------------------------------------
    // Copy of an EFFECTIVE planeswalker (deferral pay-down:
    // copy-of-effective-planeswalker-reclass) — CR 707.2 / 712.4 / 711.
    //
    // The copy SOURCE is a creature-front transform DFC currently FLIPPED to
    // its planeswalker BACK face. Such a source is a Creature C# instance
    // carrying a transient loyalty body (IsEffectivePlaneswalker() == true),
    // NOT a Planeswalker instance. CR 712.4: the copiable values of a
    // transformed permanent are derived from its currently-up (back) face, so
    // a clone of it must become a copy of the planeswalker back — Planeswalker
    // TYPE + back-face loyalty body + back-face loyalty abilities — without
    // re-instancing the copy as a Planeswalker (Option-B parallel surface).
    // -----------------------------------------------------------------------

    /// <summary>Build a creature-front transform DFC FLIPPED to its
    /// planeswalker back face, on the battlefield, as a deterministic copy
    /// SOURCE. The back face carries a loyalty body + one [+1]/[−2]-style
    /// loyalty ability (oracle text) so the copy machinery has both a loyalty
    /// value and abilities to mirror.</summary>
    private Creature FlippedEffectivePlaneswalkerSource(ContinuousEffectsService svc)
    {
        var src = new Creature("Hero Front", "{1}{U}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = svc,
            Zone = Majik.Core.Zones.ZoneType.Battlefield,
        };
        src.MdfcState = new Majik.Core.CardData.MDFCs.MdfcState(
            "Hero Front",
            "Hero Walker",
            new Majik.Core.CardData.MDFCs.BackFaceCharacteristics(
                name: "Hero Walker",
                isCreature: false,
                power: 0,
                toughness: 0,
                types: new[] { CardType.Planeswalker },
                subtypes: new[] { CardSubtype.Ral },
                supertypes: new[] { CardSupertype.Legendary },
                keywords: null,
                colors: new[] { ManaColor.Blue },
                loyalty: 4,
                oracleText: "+1: Draw a card."));
        src.MdfcState.Transform(); // flip to the planeswalker back face
        return src;
    }

    [Fact]
    public void Copy_OfFlippedEffectivePlaneswalker_CopierBecomesEffectivePlaneswalker()
    {
        var svc = new ContinuousEffectsService();
        var source = FlippedEffectivePlaneswalkerSource(svc);
        source.IsEffectivePlaneswalker().Should().BeTrue("the source is flipped to its PW back");

        // Copier (a Spark-Double-style 0/0 Illusion) is itself a Creature.
        var copier = new Creature("Spark Double", "{3}{U}", 0, 0)
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = svc,
            Zone = Majik.Core.Zones.ZoneType.Battlefield,
        };

        CopyCharacteristicsEffect.RegisterCopy(
            svc, copier, source,
            abilityRebind: CopyCharacteristicsEffect.DefaultAbilityRebind);

        // CR 712.4 / 707.2 — copiable values come from the back (up) face: the
        // copy takes the Planeswalker type, the Ral subtype, and the colour.
        var chars = svc.Compute((Permanent)copier);
        chars.Types.Should().Contain(CardType.Planeswalker,
            "the copy of a flipped DFC takes the back face's PW type (CR 712.4)");
        chars.Types.Should().NotContain(CardType.Creature,
            "copiable values overwrite — the copy is no longer a creature");
        chars.Subtypes.Should().Contain(CardSubtype.Ral);

        // CR 711 / 306.5b — the copy gains a working loyalty body (Option-B
        // transient surface) seeded from the back face's starting loyalty.
        copier.IsEffectivePlaneswalker().Should().BeTrue(
            "the copy of an effective planeswalker is itself an effective planeswalker");
        copier.GetEffectiveLoyalty().Should().Be(4,
            "the copy enters with the back face's starting loyalty (CR 712.4)");
    }

    [Fact]
    public void Copy_OfFlippedEffectivePlaneswalker_MirrorsBackFaceLoyaltyAbilities()
    {
        var svc = new ContinuousEffectsService();
        var source = FlippedEffectivePlaneswalkerSource(svc);

        var copier = new Creature("Spark Double", "{3}{U}", 0, 0)
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = svc,
            Zone = Majik.Core.Zones.ZoneType.Battlefield,
        };

        CopyCharacteristicsEffect.RegisterCopy(
            svc, copier, source,
            abilityRebind: CopyCharacteristicsEffect.DefaultAbilityRebind);

        svc.Compute((Permanent)copier); // drives the GrantAbilityEffect sync

        // CR 707.2 / 606 — the copy gets the back face's loyalty abilities,
        // bound to the copy's own Permanent-typed loyalty surface (4A) so
        // activating "[+1]" raises the COPY's loyalty, not the source's.
        var loyaltyAbilities = copier.Abilities
            .OfType<Majik.Core.Abilities.LoyaltyAbility>().ToList();
        loyaltyAbilities.Should().ContainSingle(
            "the back face's single [+1] ability is mirrored onto the copy");
        loyaltyAbilities[0].Source.Should().BeSameAs(copier,
            "the mirrored loyalty ability is bound to the copy, not the source");
    }

    [Fact]
    public void Copy_OfRealPlaneswalkerSource_DoesNotStampTransientBody()
    {
        // A REAL Planeswalker instance already holds authoritative loyalty;
        // the printed-type copy path covers it and the transient-body stamp
        // must stay inert (Planeswalker overrides the transient accessors).
        var svc = new ContinuousEffectsService();
        var pw = new Planeswalker("Jace", "{2}{U}{U}", 5)
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = svc,
            Zone = Majik.Core.Zones.ZoneType.Battlefield,
        };
        var copier = new Creature("Spark Double", "{3}{U}", 0, 0)
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = svc,
            Zone = Majik.Core.Zones.ZoneType.Battlefield,
        };

        CopyCharacteristicsEffect.RegisterCopy(
            svc, copier, pw,
            abilityRebind: CopyCharacteristicsEffect.DefaultAbilityRebind);

        copier.GetEffectiveLoyalty().Should().BeNull(
            "a real-PW source has no transient back-face body to stamp; the " +
            "copy's loyalty body is the (separately-handled) printed-PW path");
    }
}
