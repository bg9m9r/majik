using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mirrorpool (Oath of the Gatewatch).
///
/// Land. Oracle text (verified against the embedded Modern seed 2026-06-14):
///   "This land enters tapped.
///    {T}: Add {C}.
///    {2}{C}, {T}, Sacrifice this land: Copy target instant or sorcery spell
///      you control. You may choose new targets for the copy.
///    {4}{C}, {T}, Sacrifice this land: Create a token that's a copy of target
///      creature you control."
///
/// ## Why a hand-coded factory (not a pure JSON definition)
///
/// The base shape (plain nonbasic Land + the <b>{T}: Add {C}</b> mana ability)
/// is materialised from the embedded JSON definition (<c>mirrorpool.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — exactly like
/// <see cref="MirrexFactory"/>. The enters-tapped replacement and the two
/// sac-this-land copy abilities are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses none of them.
///
/// ## Implemented (v1)
/// - <b>Land identity + {T}: Add {C}</b> — from the JSON definition. {C}
///   (colourless, CR 107.4c) has no dedicated <see cref="ManaCost"/> bucket;
///   <c>ManaCost.Parse("C")</c> folds it into Generic, exactly as Wasteland /
///   Crumbling Vestige / Mirrex do.
/// - <b>"This land enters tapped." (CR 614.1c)</b> — unconditional
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> (same posture as
///   <see cref="CrumblingVestigeFactory"/>). The shape-only path (null bus)
///   skips registration; the production load path also matches the plain
///   "This land enters tapped." clause through
///   <see cref="Majik.Core.CardData.EntersTappedBinder"/>.
/// - <b>{2}{C}, {T}, Sacrifice this land: Copy target instant or sorcery spell
///   you control. You may choose new targets for the copy.</b> — an
///   <see cref="ActivatedAbility"/> (CR 605 — not a mana ability; uses the
///   stack) with cost <see cref="ManaCostCost"/>("{2}{C}") +
///   <see cref="AdditionalCost.Tap"/> + <see cref="AdditionalCost.Sacrifice"/>,
///   and a 1..1 "target instant or sorcery spell you control" request. On
///   resolution it pushes a distinct copy of the targeted spell onto the live
///   stack via <see cref="SpellCopier.PushCopyOfTopSpellAsync"/> (CR 707.10 /
///   706.10a), controlled by Mirrorpool's controller, honouring "you may choose
///   new targets for the copy" (CR 707.10a) when a live agent + game context
///   are wired — the same copy primitive Twincast / Reverberate use.
/// - <b>{4}{C}, {T}, Sacrifice this land: Create a token that's a copy of
///   target creature you control.</b> — an <see cref="ActivatedAbility"/>
///   (CR 602) with cost {4}{C} + tap + sacrifice and a 1..1 "target creature
///   you control" request. On resolution it mints one token copying the
///   targeted creature's copiable values (CR 706.2) under Mirrorpool's
///   controller — the same token-copy primitive as
///   <see cref="KikiJikiMirrorBreakerFactory"/>, minus the haste/exile/another/
///   nonlegendary riders (Mirrorpool's token is a permanent, no restrictions).
///
/// ## Deferred (v1 gaps)
/// - <b>Choose-time legality filter</b>: "you control" + the spell/creature
///   type gating is rechecked at resolve, not enumerated at choose-time
///   (<see cref="Players.Agents.TargetRequest.LegalCandidates"/> left empty) —
///   the standard CR 608.2b posture shared with Kiki-Jiki / Heliod / Snapcaster
///   (the production agent enumerates the live battlefield / stack itself).
/// - <b>Layer-1 copy lossiness</b>: the token snapshots the original's copiable
///   values at resolution; later changes to the original aren't tracked. Same
///   v1 posture as Kiki-Jiki / Splinter Twin.
/// </summary>
[CardName("Mirrorpool")]
public static class MirrorpoolFactory
{
    public const string CardName = "Mirrorpool";
    public const string Slug = "mirrorpool";

    /// <summary>Cost of the copy-spell ability: {2}{C}, {T}, Sacrifice.</summary>
    public const string CopySpellManaCost = "{2}{C}";

    /// <summary>Cost of the token-copy ability: {4}{C}, {T}, Sacrifice.</summary>
    public const string CopyCreatureManaCost = "{4}{C}";

    /// <summary>
    /// Construct Mirrorpool with no live runtime services. The activated
    /// abilities are attached structurally for shape / identity tests; the
    /// token-copy ability mints the token via raw zone manipulation when
    /// resolved directly, and the copy-spell ability is a clean no-op without a
    /// live stack (shape path). Enters-tapped is omitted (no ReplacementBus).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, zones: null, replacements: null, eventBus: null);

    /// <summary>
    /// Construct Mirrorpool with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">Zone service so the minted token's ETB
    /// CardMovedEvent fires (Impact Tremors / Soul Warden etc.). May be null.</param>
    /// <param name="replacements">When supplied, the unconditional enters-tapped
    /// replacement (CR 614.1c) is registered against it.</param>
    /// <param name="eventBus">When supplied, the sacrifice cost publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) so aristocrat
    /// payoffs fire on the sac-cost activation paths. May be null.</param>
    public static Land Create(
        Player owner,
        ZoneService? zones,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land,
        // {T}: Add {C} mana ability). The enters-tapped replacement and the two
        // sac-this-land copy abilities are layered on below — none is
        // expressible in the current JSON AbilityDefinition schema (same
        // posture as MirrexFactory).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "This land enters tapped." — CR 614.1c. Unconditional. Shape-only
        // path (no ReplacementBus) skips registration; the production load
        // path also matches the clause via EntersTappedBinder off the oracle
        // text (same posture as CrumblingVestigeFactory).
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {2}{C}, {T}, Sacrifice this land: Copy target instant or sorcery
        //   spell you control. You may choose new targets for the copy.
        //
        // CR 605 — NOT a mana ability; uses the stack. CR 707.10 / 706.10a —
        // push a distinct copy of the targeted spell above the original; the
        // copy resolves first then ceases to exist (CR 707.10c). CR 707.10a —
        // the copier may choose new targets for the copy.
        // ----------------------------------------------------------------
        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(CopySpellManaCost),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land, eventBus),
            },
            effects: new IEffect[] { BuildCopySpellEffect(land, owner) },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target instant or sorcery spell you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Counter),
            }));

        // ----------------------------------------------------------------
        // {4}{C}, {T}, Sacrifice this land: Create a token that's a copy of
        //   target creature you control.
        //
        // CR 602 — ordinary activated ability (uses the stack). CR 706.2 —
        // snapshot the targeted creature's copiable values and mint one token
        // under Mirrorpool's controller.
        // ----------------------------------------------------------------
        ActivatedAbility? copyCreatureAbility = null;
        var copyCreatureEffect = new Effect(
            $"{CardName}: create a token that's a copy of target creature you control",
            () =>
            {
                if (copyCreatureAbility == null) return;
                CreateTokenCopyOfTarget(land, owner, copyCreatureAbility, zones);
            });

        copyCreatureAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(CopyCreatureManaCost),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land, eventBus),
            },
            effects: new IEffect[] { copyCreatureEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Token),
            });

        land.AddAbility(copyCreatureAbility);

        return land;
    }

    /// <summary>
    /// Build the resolution effect for the copy-spell ability. On the async
    /// resolution path it reads the live <see cref="GameContext.Stack"/> off the
    /// <see cref="ResolutionContext"/> and pushes a distinct copy of the chosen
    /// spell above the original via <see cref="SpellCopier.PushCopyOfTopSpellAsync"/>
    /// (CR 707.10 / 706.10a), controlled by Mirrorpool's controller and honouring
    /// "you may choose new targets for the copy" (CR 707.10a). When no live stack
    /// is wired (shape path) it is a clean no-op.
    /// </summary>
    private static Effect BuildCopySpellEffect(Land land, Player owner) =>
        new(
            $"{CardName}: copy target instant or sorcery spell you control",
            async ctx =>
            {
                var stack = ctx.Game?.Stack;
                if (stack == null) return; // shape path — no live stack

                // The chosen target is a spell on the stack (CR 115.4).
                var targets = ctx.ChosenTargets;
                if (targets.Count == 0 || targets[0].Count == 0) return;

                if (targets[0][0] is not Majik.Core.Stack.IStackObject spell) return;

                var controller = land.Controller ?? owner;

                // CR 707.10 — the copy is controlled by the player who controls
                // the copy effect (Mirrorpool's controller). CR 707.10a — the
                // copier may choose new targets for the copy.
                await SpellCopier.PushCopyOfTopSpellAsync(
                    stack,
                    spell,
                    ctx.Agent,
                    ctx.Game,
                    copyController: controller).ConfigureAwait(false);
            });

    /// <summary>
    /// CR 706.2 — mint one token copying the targeted creature's copiable values
    /// (name, P/T, subtypes, keyword names, colour) under Mirrorpool's
    /// controller. Resolve-time recheck (CR 608.2b): the target must still be a
    /// battlefield creature the controller controls. v1 lossy — later changes to
    /// the original aren't tracked (same posture as Kiki-Jiki / Splinter Twin).
    /// </summary>
    private static void CreateTokenCopyOfTarget(
        Land source,
        Player owner,
        ActivatedAbility ability,
        ZoneService? zones)
    {
        var chosen = ability.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return;

        if (chosen[0][0] is not Creature original) return;

        // CR 608.2b — resolve-time legality recheck.
        if (original.Zone != ZoneType.Battlefield) return;
        var controller = source.Controller ?? owner;
        if (!ReferenceEquals(original.Controller, controller)) return; // "you control"

        // CR 706.2 — snapshot copiable values.
        var keywords = original.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();
        var colours = CardColors.GetColors(original).ToList();

        var spec = new TokenFactory.TokenSpec(
            Name: original.Name,
            Power: original.BasePower,
            Toughness: original.BaseToughness,
            Subtypes: original.Subtypes.ToList(),
            Keywords: keywords,
            Colors: colours);

        TokenFactory.CreateOnBattlefield(spec, controller, zones);
    }
}
