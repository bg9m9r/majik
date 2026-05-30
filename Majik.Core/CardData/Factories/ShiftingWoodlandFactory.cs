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
/// Named-card factory for Shifting Woodland (Modern Horizons 3).
///
/// Land. Oracle text (verified against Scryfall 2026-05-30):
///   "This land enters tapped unless you control a Forest.
///    {T}: Add {G}.
///    Delirium — {2}{G}{G}: This land becomes a copy of target permanent card
///    in your graveyard until end of turn. Activate only if there are four or
///    more card types among cards in your graveyard."
///
/// Scryfall-confirmed type line: Land (no basic supertype, no subtypes).
/// Shifting Woodland is NOT itself a Forest.
///
/// ## Shapes reused
/// <list type="bullet">
///   <item><b>ETB tapped unless you control a Forest (CR 614.1c)</b> —
///   <see cref="ConditionalEntersTappedReplacement"/>, exactly as
///   <see cref="CastleGarenbrigFactory"/> (the same green "unless you control
///   a Forest" predicate; reference-equality self-exclusion, though Shifting
///   Woodland has no Forest subtype anyway so it can never satisfy its own
///   predicate).</item>
///   <item><b>{T}: Add {G}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1),
///   materialised from the embedded JSON definition
///   (<c>shifting-woodland.json</c>).</item>
///   <item><b>{2}{G}{G}: becomes a copy of target permanent card in your
///   graveyard until end of turn</b> — an <see cref="ActivatedAbility"/>
///   (CR 602, uses the stack) whose resolution registers a
///   <see cref="CopyCharacteristicsEffect"/> with
///   <c>expiresAtEndOfTurn: true</c> (the generalized full-copiable-
///   characteristics copy infra; CR 707.2 / 613.2 Layer 1, dropped at the
///   cleanup step CR 514.2 by
///   <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>). The copy source
///   is the chosen permanent card in the controller's graveyard.</item>
///   <item><b>Delirium activation gate (CR 702.105 + 602.5b)</b> — "Activate
///   only if there are four or more card types among cards in your graveyard".
///   Reuses <see cref="UnholyHeatFactory.IsDeliriumActive"/> (which counts
///   distinct <see cref="CardType"/> values in the controller's graveyard via
///   <see cref="TarmogoyfFactory.CountDistinctCardTypes"/>).</item>
/// </list>
///
/// ## Delirium activation gating posture
/// The "Activate only if there are four or more card types among cards in
/// your graveyard" restriction (CR 602.5c) is enforced authoritatively via
/// the ability's <c>canActivateCheck</c> — the general "Activate only if
/// &lt;condition&gt;" gate (consulted by <see cref="Rules.ActionValidator"/>
/// and <see cref="Services.AbilityActivator"/>), re-evaluated live against
/// the controller's graveyard. As belt-and-braces (CR 117.x — should the
/// condition lapse between activation and resolution, after the {2}{G}{G}
/// cost is already paid), the resolution closure re-checks delirium and
/// short-circuits cleanly. The delirium count itself reuses the existing
/// <see cref="UnholyHeatFactory.IsDeliriumActive"/> predicate.
///
/// ## Graveyard targeting
/// "Target permanent card in your graveyard" — a 1..1
/// <see cref="TargetRequest"/> whose candidates are the controller's
/// graveyard cards that are permanent cards (<see cref="Permanent"/> runtime
/// instances — creature / artifact / enchantment / land / planeswalker;
/// CR 110.4a). Instants and sorceries are excluded (not permanents). The
/// candidate pool is gathered live via
/// <see cref="TargetRequest.CandidateGatherer"/> so a card milled / discarded
/// after the land entered is still selectable. The resolution closure reads
/// <see cref="ActivatedAbility.ChosenTargets"/> and copies the chosen
/// permanent's full copiable characteristics onto the land.
///
/// ## v1 posture (inherited from the copy infra)
/// The copied characteristics surface through
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> with the same
/// known gaps documented on <see cref="CopyCharacteristicsEffect"/> — name /
/// mana cost / supertypes / colour and non-keyword abilities are recorded but
/// not fully surfaced; a Land row carries no P/T fields (the copied P/T is
/// recorded on the effect for inspection). Type line, subtypes, and keyword
/// abilities DO apply through Compute.
/// </summary>
[CardName("Shifting Woodland")]
public static class ShiftingWoodlandFactory
{
    public const string CardName = "Shifting Woodland";
    public const string Slug = "shifting-woodland";

    /// <summary>The Delirium copy ability's {2}{G}{G} activation cost.</summary>
    public const string CopyAbilityCost = "{2}{G}{G}";

    /// <summary>
    /// Construct Shifting Woodland with no runtime services wired. The
    /// {T}: Add {G} mana ability (from JSON) + the Delirium copy ability
    /// shape are attached so the card surface is complete; the ETB-tapped
    /// replacement is omitted and the copy ability resolves to a no-op (no
    /// <see cref="ContinuousEffectsService"/> to register the effect on).
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Shifting Woodland with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service the
    /// "becomes a copy until end of turn" <see cref="CopyCharacteristicsEffect"/>
    /// is registered on. May be null — the ability still resolves but no
    /// copy effect is recorded.</param>
    /// <param name="replacements">Replacement bus for the
    /// "enters tapped unless you control a Forest" rider (CR 614.1c). May be
    /// null — the ETB predicate is omitted (shape-only posture).</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {G} mana ability). The ETB-tapped rider + the Delirium
        // copy ability are layered on below — neither is expressible in the
        // current JSON AbilityDefinition schema (same posture as
        // RestlessSpireFactory).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB tapped unless you control a Forest (CR 614.1c).
        //
        // Predicate: enters untapped ⟺ the controller controls at least one
        // permanent (other than this card) with the Forest subtype. Same
        // shape as CastleGarenbrigFactory.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    controller.Zones.Battlefield.GetCards()
                        .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Forest))));
        }

        // ----------------------------------------------------------------
        // Delirium — {2}{G}{G}: This land becomes a copy of target permanent
        // card in your graveyard until end of turn. Activate only if there
        // are four or more card types among cards in your graveyard.
        //
        // CR 602 — ordinary activated ability (uses the stack). The chosen
        // permanent card in the controller's graveyard is copied in place
        // for the rest of the turn via CopyCharacteristicsEffect
        // (expiresAtEndOfTurn: true, CR 707.2 / 514.2).
        // ----------------------------------------------------------------
        ActivatedAbility? copyAbility = null;
        var copyEffect = new Effect(
            $"{CardName}: becomes a copy of target permanent card in your graveyard until EOT (delirium)",
            () =>
            {
                if (copyAbility == null) return;

                var controller = land.Controller ?? owner;

                // CR 602.5b delirium activation gate — defensive resolve-time
                // re-check. The gate is authoritatively enforced at activation
                // time by the ability's canActivateCheck (CR 602.5c — see the
                // ActivatedAbility construction below, consulted by
                // ActionValidator / AbilityActivator). This resolve-time
                // re-check is belt-and-braces: fail-closed if delirium lapsed
                // between activation and resolution (the {2}{G}{G} cost was
                // already paid).
                if (!UnholyHeatFactory.IsDeliriumActive(controller)) return;

                // No service wired — shape-only path (NamedCardFactory.Create).
                if (effects == null) return;

                // CR 608.2b — read the chosen target; copy nothing if it's
                // gone / illegal. Must be a permanent CARD still in the
                // controller's graveyard (CR 110.4a — instants/sorceries are
                // not permanents).
                if (copyAbility.ChosenTargets.Count == 0) return;
                if (copyAbility.ChosenTargets[0].Count == 0) return;
                if (copyAbility.ChosenTargets[0][0] is not Permanent source) return;
                if (source.Zone != ZoneType.Graveyard) return;
                if (!controller.Zones.Graveyard.GetCards().Contains(source)) return;

                // CR 707.2 / 613.2 Layer 1 — becomes a copy in place until
                // end of turn (dropped at the cleanup step by ExpireEndOfTurn).
                effects.Register(new CopyCharacteristicsEffect(
                    land, source, expiresAtEndOfTurn: true));
            });

        copyAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(CopyAbilityCost) },
            effects: new IEffect[] { copyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.None,
                    CandidateGatherer: _ => GraveyardPermanentCards(land.Controller ?? owner)),
            },
            // CR 602.5c — "Activate only if there are four or more card types
            // among cards in your graveyard" (Delirium). Authoritative
            // activation gate, consulted by ActionValidator / AbilityActivator
            // (merged via the general canActivateCheck seam). Re-evaluated live
            // against the controller's graveyard on each check.
            canActivateCheck: () =>
                UnholyHeatFactory.IsDeliriumActive(land.Controller ?? owner));

        land.AddAbility(copyAbility);

        return land;
    }

    /// <summary>
    /// CR 110.4a — the permanent cards in <paramref name="controller"/>'s
    /// graveyard: the runtime <see cref="Permanent"/> instances (creature /
    /// artifact / enchantment / land / planeswalker). Instants and sorceries
    /// are excluded. Exposed for the target-candidate gatherer and tests.
    /// </summary>
    public static IReadOnlyList<object> GraveyardPermanentCards(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Graveyard.GetCards()
            .OfType<Permanent>()
            .Cast<object>()
            .ToList();
    }
}
