using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thespian's Stage (Dark Ascension).
///
/// Land. Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add {C}.
///    {2}, {T}: This land becomes a copy of target land, except it has this
///    ability."
///
/// Scryfall-confirmed type line: Land (no basic supertype, no subtypes).
///
/// ## Shapes reused
/// <list type="bullet">
///   <item><b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1),
///   materialised from the embedded JSON definition
///   (<c>thespians-stage.json</c>). {C} buckets into the Generic slot in
///   <c>ManaCost.Parse</c> today (same posture as Strip Mine / Karn's
///   Bastion — no dedicated colorless slot yet).</item>
///   <item><b>{2}, {T}: becomes a copy of target land</b> — an
///   <see cref="ActivatedAbility"/> (CR 602, uses the stack) whose resolution
///   registers a <see cref="CopyCharacteristicsEffect"/> with
///   <c>expiresAtEndOfTurn: false</c> — the copy is PERMANENT (CR 707.2 /
///   613.2 Layer 1; it lasts as long as Thespian's Stage stays on the
///   battlefield, unlike Shifting Woodland's "until end of turn" copy). The
///   copy source is the chosen target land on the battlefield.</item>
/// </list>
///
/// ## "except it has this ability" (CR 707.2)
/// Thespian's Stage's copy is full copiable characteristics EXCEPT it retains
/// "this ability" (the {2},{T} copy ability), so it can copy again. In this
/// engine that exclusion falls out for free: <see cref="CopyCharacteristicsEffect"/>
/// rewrites only the permanent's characteristics row (types / subtypes /
/// supertypes / colour / keyword set) via
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/>; it NEVER strips
/// the runtime <see cref="Land.Abilities"/> collection. The {T}: Add {C} mana
/// ability and the copy ability are concrete runtime ability instances on the
/// Land and survive the copy untouched. (Per the rules the copied land's OWN
/// printed abilities are NOT gained — they aren't re-instantiated here either;
/// only keyword markers from the source are mirrored into the characteristics
/// keyword set, the same boundary documented on CopyCharacteristicsEffect.)
///
/// ## Target gathering
/// "Target land" — a 1..1 <see cref="TargetRequest"/> whose candidates are the
/// lands on the battlefield (CR 109.2 — any land, including Thespian's Stage
/// itself and an opponent's lands). The candidate pool is gathered live via
/// <see cref="TargetRequest.CandidateGatherer"/>. In the no-Game shape-only
/// posture the gatherer sees the controller's own battlefield (no Game
/// reference is threaded through <see cref="NamedCardFactory"/>); cross-player
/// land candidates are a known boundary shared with the other land-copy
/// factories.
///
/// ## v1 posture (inherited from the copy infra)
/// The copied characteristics surface through
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> with the same
/// known gaps documented on <see cref="CopyCharacteristicsEffect"/> — name /
/// mana cost / supertypes / colour and non-keyword abilities are recorded but
/// not fully surfaced. Type line, subtypes, and keyword abilities DO apply
/// through Compute.
/// </summary>
[CardName("Thespian's Stage")]
public static class ThespiansStageFactory
{
    public const string CardName = "Thespian's Stage";
    public const string Slug = "thespians-stage";

    /// <summary>The copy ability's {2} activation cost (plus the {T} tap).</summary>
    public const string CopyAbilityCost = "{2}";

    /// <summary>
    /// Construct Thespian's Stage with no runtime services wired. The
    /// {T}: Add {C} mana ability (from JSON) + the copy ability shape are
    /// attached so the card surface is complete; the copy ability resolves to
    /// a no-op (no <see cref="ContinuousEffectsService"/> to register the
    /// effect on). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null);

    /// <summary>
    /// Construct Thespian's Stage with an optional continuous-effects service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service the
    /// "becomes a copy of target land" <see cref="CopyCharacteristicsEffect"/>
    /// is registered on. May be null — the ability still resolves but no copy
    /// effect is recorded.</param>
    public static Land Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {C} mana ability). The copy ability is layered on below —
        // it is not expressible in the current JSON AbilityDefinition schema
        // (same posture as ShiftingWoodlandFactory).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {2}, {T}: This land becomes a copy of target land, except it has
        // this ability.
        //
        // CR 602 — ordinary activated ability (uses the stack). The chosen
        // target land on the battlefield is copied in place PERMANENTLY via
        // CopyCharacteristicsEffect (expiresAtEndOfTurn: false, CR 707.2).
        // ----------------------------------------------------------------
        ActivatedAbility? copyAbility = null;
        var copyEffect = new Effect(
            $"{CardName}: becomes a copy of target land",
            () =>
            {
                if (copyAbility == null) return;

                var controller = land.Controller ?? owner;

                // No service wired — shape-only path (NamedCardFactory.Create).
                if (effects == null) return;

                // CR 608.2b — read the chosen target; copy nothing if it's
                // gone / illegal. Must be a permanent still on the battlefield
                // that is a land (CR 109.2).
                if (copyAbility.ChosenTargets.Count == 0) return;
                if (copyAbility.ChosenTargets[0].Count == 0) return;
                if (copyAbility.ChosenTargets[0][0] is not Permanent source) return;
                if (source.Zone != ZoneType.Battlefield) return;
                if (!source.HasType(CardType.Land)) return;

                // CR 707.2 / 613.2 Layer 1 — becomes a copy in place. NOT
                // until end of turn: the copy persists while Thespian's Stage
                // is on the battlefield (expiresAtEndOfTurn: false). "Except it
                // has this ability" needs no special handling — the copy effect
                // rewrites only the characteristics row, never land.Abilities,
                // so the copy ability instance survives.
                effects.Register(new CopyCharacteristicsEffect(
                    land, source, expiresAtEndOfTurn: false));
            });

        copyAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(CopyAbilityCost) },
            effects: new IEffect[] { copyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.None,
                    CandidateGatherer: _ => TargetLands(land.Controller ?? owner)),
            });

        land.AddAbility(copyAbility);

        return land;
    }

    /// <summary>
    /// CR 109.2 — the lands on <paramref name="controller"/>'s battlefield
    /// (any land is a legal target). Exposed for the target-candidate gatherer
    /// and tests. Cross-player land candidates are a known boundary in the
    /// no-Game shape-only posture (no Game reference is threaded through the
    /// factory).
    /// </summary>
    public static IReadOnlyList<object> TargetLands(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.HasType(CardType.Land))
            .Cast<object>()
            .ToList();
    }
}
