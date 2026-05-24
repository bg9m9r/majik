using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Inkmoth Nexus (Mirrodin Besieged).
///
/// Land. Oracle text:
///   "{T}: Add {C}.
///    {1}: Until end of turn, Inkmoth Nexus becomes a 1/1 Phyrexian Insect
///    artifact creature with flying and infect. It's still a land."
///
/// (Wizards Oracle uses the Insect creature type for the animated body;
/// older printings said "Blinkmoth" but the current Oracle is Phyrexian
/// Insect.)
///
/// ## Implemented (v1)
/// - Land identity (no printed subtypes).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> generating one
///   colorless. {C} is bucketed as +1 generic in <see cref="ValueObjects.ManaCost"/>
///   today (see comment in <see cref="ValueObjects.ManaCost.Parse"/>).
/// - <b>{1}: Until EOT becomes a 1/1 Phyrexian Insect artifact creature with
///   flying and infect; still a land</b> — wired as an
///   <see cref="ActivatedAbility"/> whose resolution effect registers an
///   <see cref="InkmothAnimateLandEffect"/> on the supplied
///   <see cref="ContinuousEffectsService"/>. The effect:
///     * adds <see cref="CardType.Artifact"/> and <see cref="CardType.Creature"/>
///       to the land's <see cref="PermanentCharacteristics.Types"/> (Layer 4,
///       CR 613.1d). The printed Land type stays — "It's still a land."
///     * adds <see cref="CardSubtype.Phyrexian"/> and <see cref="CardSubtype.Insect"/>
///       to the subtypes set.
///     * grants Flying + Infect keyword markers to the keyword set.
///     * carries P/T = 1/1 (inspectable via <see cref="InkmothAnimateLandEffect.NewPower"/>
///       / <see cref="InkmothAnimateLandEffect.NewToughness"/>; see the
///       Karn-shim precedent at <see cref="KarnAnimatedShimPTEffect"/>).
///   The effect flags <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   (CR 514.2 — "until end of turn" effects end during cleanup), so a
///   call to <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> reverts
///   the land to its printed shape.
///
/// ## Deferred (v1 gaps)
/// - <b>Infect mechanic</b>: the keyword is registered as a marker only.
///   The combat-damage replacement (CR 702.90 — damage from infect sources
///   is poison counters to players and -1/-1 counters on creatures) is NOT
///   wired. A future pass that lands the Infect primitive (poison counter
///   tracking on <see cref="Player"/> + a Layer-style combat replacement
///   over the damage pipeline) will pick up Inkmoth as a free consumer of
///   the marker.
/// - <b>Land-becomes-creature P/T pipeline</b>: <see cref="ContinuousEffectsService.Compute(Permanent)"/>
///   builds a <see cref="PermanentCharacteristics"/> (no P/T fields) for
///   non-Creature C# instances; <see cref="Land"/> is not a
///   <see cref="Creature"/>, so the layer-7b values on
///   <see cref="InkmothAnimateLandEffect"/> are inspectable for tests but
///   don't surface through Compute. Same v1 deviation Karn carries — see
///   <see cref="KarnAnimateArtifactEffect"/> xmldoc.
/// - <b>Activation gate / sorcery-speed</b>: none — the animate ability is
///   instant-speed per Oracle, no restriction needed. The {T} mana ability
///   shares no tap-cost with the animate (the animate costs {1} only — the
///   land stays untapped after animating).
/// - <b>"Becomes" trigger semantics</b>: nothing in the engine currently
///   fires "whenever a permanent becomes a creature" — Mutavault / Inkmoth
///   activations would otherwise interact with Master of Cruelties etc.
/// </summary>
[CardName("Inkmoth Nexus")]
public static class InkmothNexusFactory
{
    public const string CardName = "Inkmoth Nexus";

    /// <summary>
    /// Construct Inkmoth Nexus with no live wiring. The mana ability and
    /// the animate ActivatedAbility are both attached so the card shape is
    /// complete; the animate effect-registration step is gated on a non-null
    /// <c>effects</c> service, so it no-ops here (legal — the animate's
    /// {1} payment still resolves, the continuous effect just isn't tracked).
    /// </summary>
    public static Land Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct a fully-wired Inkmoth Nexus.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service. May be null — the
    /// animate ability still resolves and pays {1}, but no
    /// <see cref="InkmothAnimateLandEffect"/> is registered (no Layer 4
    /// type/subtype/keyword grant, no inspectable P/T).</param>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // CR 605.1 — mana abilities don't use the stack. {C} is bucketed
        // as +1 generic in ManaCost.Parse today (see ValueObjects.ManaCost).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("{C}")));

        // ----------------------------------------------------------------
        // {1}: Until EOT, becomes 1/1 Phyrexian Insect artifact creature
        // with flying and infect. It's still a land.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: become a 1/1 Phyrexian Insect artifact creature " +
            "with flying and infect until end of turn",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path
                effects.Register(new InkmothAnimateLandEffect(land));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }
}

/// <summary>
/// Inkmoth Nexus animate effect — until EOT the land also counts as a
/// 1/1 Phyrexian Insect artifact creature with flying and infect.
///
/// Layer 4 (CR 613.1d) — adds <see cref="CardType.Artifact"/> +
/// <see cref="CardType.Creature"/> to the permanent's effective types
/// (printed Land stays — "still a land"), plus
/// <see cref="CardSubtype.Phyrexian"/> + <see cref="CardSubtype.Insect"/>
/// subtypes, plus Flying + Infect keyword markers.
///
/// Layer 7b P/T (1/1) is recorded on <see cref="NewPower"/> /
/// <see cref="NewToughness"/> for inspection but does NOT surface through
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> while the
/// land remains a non-<see cref="Creature"/> C# instance. Same v1
/// limitation Karn's animate carries (see
/// <see cref="KarnAnimatedShimPTEffect"/>) — Compute(Permanent) builds a
/// <see cref="PermanentCharacteristics"/> with no P/T fields for non-
/// Creature permanents, so layer-7b on a Land is a tracked-but-not-applied
/// intent. Tests that need the numeric P/T read the properties directly.
///
/// "Until end of turn" (CR 514.2) — <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
/// is true; <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> drops
/// the effect during cleanup, reverting the land.
/// </summary>
public sealed class InkmothAnimateLandEffect : ContinuousEffect
{
    private readonly Land _target;

    /// <summary>P/T the land's body should read as while animated (1/1).</summary>
    public int NewPower => 1;

    /// <summary>P/T the land's body should read as while animated (1/1).</summary>
    public int NewToughness => 1;

    public InkmothAnimateLandEffect(Land target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>The animated land.</summary>
    public Land Target => _target;

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _target;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool ExpiresAtEndOfTurn => true;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars)
    {
        Apply((PermanentCharacteristics)chars);
        // If the engine later upgrades Compute(Permanent) to return a
        // CreatureCharacteristics for type-changed-to-Creature permanents,
        // this branch sets the layer-7b base P/T to 1/1 in the same pass.
        chars.Power = NewPower;
        chars.Toughness = NewToughness;
    }

    public override void Apply(PermanentCharacteristics chars)
    {
        // Layer 4 — additive type-add. Printed Land stays in chars.Types.
        chars.Types.Add(CardType.Artifact);
        chars.Types.Add(CardType.Creature);

        // Layer 4 — subtype additions.
        chars.Subtypes.Add(CardSubtype.Phyrexian);
        chars.Subtypes.Add(CardSubtype.Insect);

        // Keyword grants — the Infect marker is a no-op on the combat
        // pipeline today (mechanic deferred), but Flying gates blocking
        // legality and both will light up correctly once the Infect
        // damage-replacement primitive lands.
        chars.Keywords.Add("Flying");
        chars.Keywords.Add("Infect");
    }
}
