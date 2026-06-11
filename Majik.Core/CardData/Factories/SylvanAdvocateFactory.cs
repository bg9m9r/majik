using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sylvan Advocate (Oath of the Gatewatch, {2}{G}).
///
/// Creature — Elf Druid Ally 2/3. Oracle text (verified against Scryfall):
///   "Vigilance
///    As long as you control six or more lands, this creature and land
///    creatures you control get +2/+2."
///
/// The base shape (name, Creature, Elf/Druid/Ally subtypes, {2}{G}, 2/3) is
/// materialised from the embedded JSON definition (<c>sylvan-advocate.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Vigilance and the conditional
/// anthem are layered on top here (the JSON <see cref="CardDefinition"/>
/// schema has no keyword-marker or conditional-static ability kind yet).
///
/// ## Implemented (v1)
///
/// - <b>2/3 Creature — Elf Druid Ally</b> at {2}{G}, owner/controller wired.
/// - <b>Vigilance (CR 702.20)</b>: <see cref="KeywordAbility"/> marker;
///   <see cref="Majik.Core.Combat.CombatAbilities.HasVigilance"/> /
///   CombatValidator / Attacker consume it to suppress the attack-tap —
///   same shape as <see cref="AlpineWatchdogFactory"/>.
/// - <b>Conditional land anthem (CR 613.7c — Layer 7c)</b>: "As long as you
///   control six or more lands, this creature and land creatures you
///   control get +2/+2." Wired via <see cref="SylvanAdvocateAnthemEffect"/>
///   (private below). The generic <see cref="LordStaticEffect"/> can't
///   express either rider here:
///   <list type="bullet">
///     <item>the +2/+2 is GATED on a live land-count threshold (≥ 6 lands
///       the controller controls) — re-evaluated each
///       <see cref="ContinuousEffectsService.Compute"/> via the effect's
///       <see cref="SylvanAdvocateAnthemEffect.IsActive"/> override (CR
///       613.6 — a continuous effect from a static ability stops applying
///       the moment its condition is no longer met; this is the
///       "as long as" duration of CR 611.2b);</item>
///     <item>the affected set is "this creature OR a land creature you
///       control" — a CARD-TYPE (Land) filter plus the self-include, not a
///       creature-SUBTYPE filter the generic lord keys off.</item>
///   </list>
///   Same tailored-effect posture as <see cref="SliverLegionFactory"/>'s
///   <c>SliverLegionAnthemEffect</c> (live-count anthem the generic lord
///   can't express).
///
/// ## Land-count semantics (CR 613.6 / 611.2b)
///
/// "you control six or more lands" — counts every permanent with the Land
/// card type (CR 305) on the CONTROLLER's battlefield (CR 109.5 — "you"
/// = the source's controller). A land creature (e.g. Dryad Arbor) IS a
/// land and counts toward the six; it also receives the buff. The count is
/// read fresh on every layer recomputation, so the buff appears the instant
/// the sixth land enters and lifts the instant the count drops back to five.
///
/// ## Affected set (CR 613.7c)
///
/// "this creature and land creatures you control" — every recipient must be
/// on the battlefield and either:
///   - BE Sylvan Advocate itself ("this creature"), or
///   - be a creature the controller controls that ALSO has the Land card
///     type ("land creatures you control"; CR 109.5 — controller-scoped, so
///     an opponent's land creature is unaffected).
/// Sylvan Advocate is not itself a land, so the self-include is explicit
/// rather than a side effect of the Land filter.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape + Vigilance only. The conditional
///   anthem is NOT registered (no continuous-effects service). Suitable for
///   dispatcher / identity tests. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService)"/> — fully wired.
///   The conditional anthem registers against the layers service.
///
/// ## Deferred (v1 gaps)
///
/// - <b>LTB unregister</b>: the registered anthem stays on the
///   <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="SylvanAdvocateAnthemEffect.IsActive"/> short-circuits when
///   Sylvan Advocate isn't on the battlefield so the buff lifts correctly
///   (same posture as <see cref="SliverLegionFactory"/> /
///   <see cref="LordOfAtlantisFactory"/>).
/// </summary>
[CardName("Sylvan Advocate")]
public static class SylvanAdvocateFactory
{
    public const string CardName = "Sylvan Advocate";
    public const string Slug = "sylvan-advocate";

    /// <summary>Land threshold for the anthem — "six or more lands".</summary>
    public const int LandThreshold = 6;

    /// <summary>Per-recipient pump while the gate is on.</summary>
    public const int Pump = 2;

    /// <summary>
    /// Construct Sylvan Advocate with Vigilance but no live anthem wiring
    /// (no continuous-effects service). Suitable for shape / dispatcher
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Sylvan Advocate. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="SylvanAdvocateAnthemEffect"/> granting +2/+2 to Sylvan
    /// Advocate itself and to land creatures the controller controls —
    /// gated on the controller controlling six or more lands — is
    /// registered against the layers service. Vigilance is always wired.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// conditional anthem against. May be null — no live anthem.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elf/Druid/Ally subtypes, {2}{G}, 2/3). The JSON carries no
        // abilities — Vigilance + the conditional anthem are layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.20 — Vigilance marker. Attacking does not tap Sylvan
        // Advocate; consumed by CombatAbilities.HasVigilance / CombatValidator.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // CR 613.7c (Layer 7c) + CR 611.2b ("as long as" duration) — the
        // conditional anthem. Registered only when a continuous-effects
        // service is supplied (matches Sliver Legion's posture).
        if (continuousEffects != null)
        {
            continuousEffects.Register(new SylvanAdvocateAnthemEffect(card));
        }

        return card;
    }

    /// <summary>
    /// Count the lands the source's controller controls (CR 305 — Land card
    /// type; CR 109.5 — "you control" = controller's battlefield). Land
    /// creatures (Dryad Arbor) count. Pure helper exposed for tests; mirrors
    /// the tally baked into the live anthem effect.
    /// </summary>
    public static int CountControlledLands(Permanent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var controller = source.Controller;
        if (controller == null) return 0;

        var count = 0;
        foreach (var c in controller.Zones.Battlefield.GetCards())
        {
            if (c is Permanent p && p.HasType(CardType.Land)) count++;
        }
        return count;
    }
}

/// <summary>
/// Sylvan Advocate's "As long as you control six or more lands, this
/// creature and land creatures you control get +2/+2" static (CR 613.7c —
/// Layer 7c; CR 611.2b — "as long as" duration; CR 613.6 — the effect stops
/// applying when the land-count condition lapses).
///
/// The generic <see cref="LordStaticEffect"/> applies a FIXED ±P/±T with no
/// condition gate and keys membership off a creature SUBTYPE; Sylvan
/// Advocate needs (a) a live land-count THRESHOLD gate and (b) a CARD-TYPE
/// (Land) + self membership filter. A tailored variant is shipped here (same
/// posture as <c>SliverLegionAnthemEffect</c>).
///
/// Gate (CR 613.6 / 611.2b): <see cref="IsActive"/> returns true only while
/// the source is on the battlefield AND its controller controls six or more
/// lands. The service re-reads this each <c>Compute</c>, so the buff appears
/// the instant the sixth land enters and lifts the instant it drops to five.
///
/// Filter (CR 613.7c — continuous effects apply only to on-battlefield
/// permanents):
///   - Target is on the battlefield.
///   - Target IS the source ("this creature"), OR
///   - Target is controlled by the source's controller (CR 109.5) AND has
///     the Land card type ("land creatures you control").
///
/// Pump: +2/+2 to each recipient.
/// </summary>
public sealed class SylvanAdvocateAnthemEffect : ContinuousEffect
{
    private readonly Permanent _source;

    public SylvanAdvocateAnthemEffect(Permanent source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override Layer Layer => Layer.PT_Modify;

    /// <summary>CR 613.1g — the permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    /// <summary>
    /// CR 611.2b / 613.6 — active only while the source is on the
    /// battlefield AND its controller controls six or more lands. The
    /// land-count is re-read here on every recomputation, so the anthem is a
    /// live "as long as" condition rather than a one-shot snapshot.
    /// </summary>
    public override bool IsActive() =>
        _source.Zone == ZoneType.Battlefield
        && SylvanAdvocateFactory.CountControlledLands(_source)
           >= SylvanAdvocateFactory.LandThreshold;

    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != ZoneType.Battlefield) return false;

        // "this creature" — Sylvan Advocate itself (it is not a land, so the
        // self-include is explicit).
        if (ReferenceEquals(creature, _source)) return true;

        // "land creatures you control" — CR 109.5 controller-scoped + the
        // Land card type (CR 305). An opponent's land creature is unaffected.
        if (!ReferenceEquals(creature.Controller, _source.Controller)) return false;
        return creature.HasType(CardType.Land);
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += SylvanAdvocateFactory.Pump;
        chars.Toughness += SylvanAdvocateFactory.Pump;
    }

    /// <summary>
    /// Sim-only: reconstruct an identical <see cref="SylvanAdvocateAnthemEffect"/>
    /// bound to <paramref name="clonedSource"/> for the search-sandbox clone.
    /// IsActive reads CountControlledLands from clonedSource.Controller live (remapped).
    /// preserves: nothing scalar; source → clonedSource.
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        => new SylvanAdvocateAnthemEffect(clonedSource);
}
