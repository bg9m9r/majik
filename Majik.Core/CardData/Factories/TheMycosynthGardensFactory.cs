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
/// Named-card factory for The Mycosynth Gardens (Phyrexia: All Will Be One,
/// commander deck).
///
/// Land — Sphere. Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add {C}.
///    {1}, {T}: Add one mana of any color.
///    {X}, {T}: This land becomes a copy of target nontoken artifact you
///    control with mana value X."
///
/// Scryfall-confirmed type line: Land — Sphere (no basic / legendary
/// supertype). CR 205.3i — the Sphere land subtype.
///
/// ## Shapes reused
/// <list type="bullet">
///   <item><b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1),
///   materialised from the embedded JSON definition
///   (<c>the-mycosynth-gardens.json</c>). {C} buckets into the Generic slot in
///   <c>ManaCost.Parse</c> today (same posture as Thespian's Stage / Strip
///   Mine — no dedicated colorless slot yet).</item>
///   <item><b>{1}, {T}: Add one mana of any color</b> — five
///   <see cref="ManaAbility"/> instances (one per WUBRG) each with a {1}
///   activation cost, the same shape <see cref="PropheticPrismFactory"/> /
///   <see cref="OrnithopterOfParadiseFactory"/> use to model "add one mana of
///   any color"; the mana picker can satisfy any single colour pip. Carried in
///   the JSON definition with <c>"cost": "1"</c>.</item>
///   <item><b>{X}, {T}: becomes a copy of target nontoken artifact you control
///   with mana value X</b> — an <see cref="ActivatedAbility"/> (CR 602, uses
///   the stack) whose resolution registers a
///   <see cref="CopyCharacteristicsEffect"/> with
///   <c>expiresAtEndOfTurn: false</c> — the copy is PERMANENT (CR 707.2 /
///   613.2 Layer 1; it lasts as long as this land stays on the battlefield,
///   the same posture as <see cref="ThespiansStageFactory"/>). The copy source
///   is the chosen target artifact on the controller's battlefield.</item>
/// </list>
///
/// ## {X} cost + "with mana value X" target gating (CR 107.3 / 601.2b)
/// The {X} cost is modeled with a <see cref="ManaCostCost"/> of <c>{X}</c>;
/// the chosen X value is sampled at resolution from an injected
/// <c>xValueProvider</c> closure (the engine has no per-activation X ledger —
/// the same posture as <see cref="SteelHellkiteFactory"/> /
/// <see cref="BlastZoneFactory"/>). The target restriction "nontoken artifact
/// you control with mana value X" depends on the same X; the candidate
/// gatherer reads the X provider so the candidate pool is the controller's
/// nontoken artifacts whose total mana value equals X (CR 109.2 / 601.2c). The
/// resolution closure re-checks the target is a still-on-battlefield, nontoken
/// artifact the controller owns with the right mana value (CR 608.2b — copy
/// nothing if the target became illegal).
///
/// In the no-Game shape-only posture (no <c>xValueProvider</c> / no effects
/// service), the copy ability resolves to a no-op (X defaults to 0, no service
/// to register the effect on) — the overload <see cref="NamedCardFactory"/>
/// dispatches to.
///
/// ## v1 posture (inherited from the copy infra)
/// The copied characteristics surface through
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> with the same
/// known gaps documented on <see cref="CopyCharacteristicsEffect"/> — name /
/// mana cost / supertypes / colour and non-keyword abilities are recorded but
/// not fully surfaced. Type line, subtypes, and keyword abilities DO apply
/// through Compute. Because the copy effect rewrites only the characteristics
/// row (never <see cref="Land.Abilities"/>), this land keeps its own
/// {T}: Add {C}, the any-color, and the copy ability instances after copying.
/// </summary>
[CardName("The Mycosynth Gardens")]
public static class TheMycosynthGardensFactory
{
    public const string CardName = "The Mycosynth Gardens";
    public const string Slug = "the-mycosynth-gardens";

    /// <summary>The copy ability's {X} activation cost (plus the {T} tap).</summary>
    public const string CopyAbilityCost = "{X}";

    /// <summary>
    /// Construct The Mycosynth Gardens with no runtime services wired. The
    /// {T}: Add {C} mana ability + the five "any color" mana abilities (from
    /// JSON) + the copy ability shape are attached so the card surface is
    /// complete; the copy ability resolves to a no-op (X defaults to 0 and
    /// there is no <see cref="ContinuousEffectsService"/> to register the
    /// effect on). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, xValueProvider: null);

    /// <summary>
    /// Construct The Mycosynth Gardens with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service the
    /// "becomes a copy of target artifact" <see cref="CopyCharacteristicsEffect"/>
    /// is registered on. May be null — the ability still resolves but no copy
    /// effect is recorded.</param>
    /// <param name="xValueProvider">Sampled at copy-ability resolution + by the
    /// target-candidate gatherer to determine X (the chosen {X} payment). May
    /// be null — X defaults to 0 (shape-only posture). Same shape as
    /// <see cref="SteelHellkiteFactory"/>.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        Func<int>? xValueProvider)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land — Sphere
        // type, {T}: Add {C} + the five {1} any-color mana abilities). The
        // {X} copy ability is layered on below — it is not expressible in the
        // current JSON AbilityDefinition schema (same posture as
        // ThespiansStageFactory).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {X}, {T}: This land becomes a copy of target nontoken artifact you
        // control with mana value X.
        //
        // CR 602 — ordinary activated ability (uses the stack). The chosen
        // target artifact on the controller's battlefield is copied in place
        // PERMANENTLY via CopyCharacteristicsEffect (expiresAtEndOfTurn:
        // false, CR 707.2 / 613.2 Layer 1).
        // ----------------------------------------------------------------
        ActivatedAbility? copyAbility = null;
        var copyEffect = new Effect(
            $"{CardName}: becomes a copy of target nontoken artifact you control with mv X",
            () =>
            {
                if (copyAbility == null) return;

                var controller = land.Controller ?? owner;
                var x = xValueProvider?.Invoke() ?? 0;

                // No service wired — shape-only path (NamedCardFactory.Create).
                if (effects == null) return;

                // CR 608.2b — read the chosen target; copy nothing if it's
                // gone / illegal. Must be a nontoken artifact the controller
                // controls, still on the battlefield, with mana value == X
                // (CR 109.2 / 202.3 — total mana value).
                if (copyAbility.ChosenTargets.Count == 0) return;
                if (copyAbility.ChosenTargets[0].Count == 0) return;
                if (copyAbility.ChosenTargets[0][0] is not Permanent source) return;
                if (!IsLegalCopyTarget(source, controller, x)) return;

                // CR 707.2 / 613.2 Layer 1 — becomes a copy in place. NOT
                // until end of turn: the copy persists while this land is on
                // the battlefield (expiresAtEndOfTurn: false). The copy effect
                // rewrites only the characteristics row, never land.Abilities,
                // so this land keeps its own mana + copy ability instances.
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
                    Description: "target nontoken artifact you control with mana value X",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.None,
                    CandidateGatherer: _ => TargetArtifacts(
                        land.Controller ?? owner, xValueProvider?.Invoke() ?? 0)),
            });

        land.AddAbility(copyAbility);

        return land;
    }

    /// <summary>
    /// CR 109.2 / 202.3 — the nontoken artifacts <paramref name="controller"/>
    /// controls whose total mana value equals <paramref name="x"/> (legal
    /// targets for the {X} copy ability). Exposed for the target-candidate
    /// gatherer and tests.
    /// </summary>
    public static IReadOnlyList<object> TargetArtifacts(Player controller, int x)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => IsLegalCopyTarget(p, controller, x))
            .Cast<object>()
            .ToList();
    }

    /// <summary>
    /// CR 109.2 / 111.1 / 202.3 — <paramref name="candidate"/> is a legal copy
    /// target iff it is a nontoken artifact, on <paramref name="controller"/>'s
    /// battlefield, controlled by them, with total mana value ==
    /// <paramref name="x"/>.
    /// </summary>
    private static bool IsLegalCopyTarget(Permanent candidate, Player controller, int x)
    {
        if (candidate.IsToken) return false;                     // CR 111.1 — nontoken
        if (!candidate.HasType(CardType.Artifact)) return false; // artifact
        if (candidate.Zone != ZoneType.Battlefield) return false;
        if (!ReferenceEquals(candidate.Controller, controller)) return false; // you control
        return candidate.ManaCostValue.TotalValue == x;          // CR 202.3 — mana value X
    }
}
