using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Giver of Runes (Modern Horizons, {W}).
///
/// Creature — Kor Cleric 1/2. Oracle text (verified against the embedded
/// seed):
///   "{T}: Another target creature you control gains protection from
///    colorless or from the color of your choice until end of turn."
///
/// The card's base shape (name, Creature, Kor + Cleric subtypes, {W}, 1/2)
/// is materialised from the embedded JSON definition
/// (<c>giver-of-runes.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The {T} protection-grant
/// activated ability is layered on top here (the JSON
/// <c>AbilityDefinition</c> schema doesn't express tap-cost activated
/// abilities with a free-text protection quality — same posture as the
/// other JSON-backed cards whose behaviour outgrows the schema, e.g.
/// <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Activated ability (CR 602.1)</b>:
///   <c>{T}: Another target creature you control gains protection from
///   colorless or from the color of your choice until end of turn.</c>
///   Cost = <see cref="AdditionalCost.Tap"/> (the printed {T}). The chosen
///   target is a 1..1 "another target creature you control"
///   <see cref="TargetRequest"/>; the <c>CandidateGatherer</c> scopes to
///   controller-side creatures EXCLUDING Giver of Runes itself (CR 602.1 —
///   "another target creature"). Same {T}+target-request shape as
///   <see cref="ArborElfFactory"/>.
/// - <b>Resolution</b> mirrors
///   <see cref="ApostlesBlessingFactory.Resolve"/>: CR 608.2b re-validates
///   the target (still a battlefield Creature, still NOT the source), then
///   registers a self-sourced <see cref="GrantAbilityEffect"/> on the
///   target's <see cref="Creature.ActiveEffects"/> that adds a
///   <see cref="ProtectionAbility"/> with the chosen quality until end of
///   turn (CR 514.2 / CR 613.1f). The quality is one of "colorless" /
///   "white" / "blue" / "black" / "red" / "green", selected by an
///   injectable <see cref="QualityPicker"/> (defaults to "colorless" — the
///   printed-text first half).
///
/// ## v1 gaps (consistent with the rest of the engine)
///
/// - <b>Agent-side quality prompt</b>: CR 601.2c — "of your choice" is
///   chosen as the ability is put on the stack. v1 uses an injectable
///   <see cref="QualityPicker"/> Func; the dispatcher path defaults to
///   "colorless". Same posture as
///   <see cref="ApostlesBlessingFactory"/>'s deferred quality prompt.
/// - <b>"Protection from colorless" enforcement</b>:
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/> maps
///   only WUBRG to quality strings today; the "colorless" marker is stored
///   on the bearer and is inspectable, but the DEBT-A consequences
///   (CR 702.16e) of protection-from-colorless are not yet honoured by the
///   protection helpers engine-wide. This is an existing engine gap, not a
///   Giver-specific one — the WUBRG path is fully enforced.
/// - <b>Target-on-activation honouring</b>: the activated ability attaches
///   structurally; the chosen target is honoured on resolution via
///   <see cref="ActivatedAbility.ChosenTargets"/> (set by the production
///   agent or directly by tests), same as <see cref="ArborElfFactory"/>.
/// </summary>
[CardName("Giver of Runes")]
public static class GiverOfRunesFactory
{
    public const string CardName = "Giver of Runes";
    public const string Slug = "giver-of-runes";

    /// <summary>
    /// Resolve-time picker for the protection quality. Receives the
    /// controller and the chosen target; returns one of
    /// <see cref="QualityColorless"/>, <see cref="QualityWhite"/>,
    /// <see cref="QualityBlue"/>, <see cref="QualityBlack"/>,
    /// <see cref="QualityRed"/>, <see cref="QualityGreen"/>. Default (when
    /// no picker is supplied) returns <see cref="QualityColorless"/> — the
    /// printed-text first half.
    /// </summary>
    public delegate string QualityPicker(Player controller, ICard target);

    public const string QualityColorless = "colorless";
    public const string QualityWhite = "white";
    public const string QualityBlue = "blue";
    public const string QualityBlack = "black";
    public const string QualityRed = "red";
    public const string QualityGreen = "green";

    /// <summary>Default picker — "protection from colorless".</summary>
    public static readonly QualityPicker ColorlessPicker = (_, _) => QualityColorless;

    /// <summary>
    /// Construct Giver of Runes owned and controlled by
    /// <paramref name="owner"/>. The {T} protection-grant ability is
    /// attached structurally; the chosen target is honoured on resolution
    /// via <see cref="ActivatedAbility.ChosenTargets"/>. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Kor + Cleric subtypes, {W}, 1/2). The JSON carries no abilities —
        // the {T} grant is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {T}: Another target creature you control gains protection from
        // colorless or from the color of your choice until end of turn.
        // CR 602.1 — activated ability with a tap cost. The chosen target
        // is honoured on resolution (CR 608.2b re-validates).
        // ----------------------------------------------------------------
        ActivatedAbility? grantAbility = null;
        var grantEffect = new Effect(
            $"{CardName}: another target creature you control gains protection (colorless or chosen colour) EOT",
            () =>
            {
                if (grantAbility == null) return;
                var chosen = grantAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                // Default quality is "colorless"; agents / tests supply a
                // colour via the chosen target's own removal context. Here
                // the picker defaults to colorless on the dispatcher path
                // (no agent quality prompt yet — see class xmldoc).
                Resolve(owner, chosen[0][0], ColorlessPicker, source: card);
            });

        grantAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { grantEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "another target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    CandidateGatherer: _ => owner.Zones.Battlefield.GetCards()
                        // CR 602.1 — "another" excludes the source itself.
                        .Where(c => c.HasType(CardType.Creature)
                                    && !ReferenceEquals(c, card))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(grantAbility);

        return card;
    }

    /// <summary>
    /// Resolve Giver of Runes' grant against <paramref name="rawTarget"/>.
    /// Exposed for direct invocation by tests / bots without driving the
    /// full activation flow. <paramref name="source"/> is the Giver of
    /// Runes permanent used to enforce the "another" gate (CR 602.1); when
    /// null the gate is skipped (caller-supplied target was already
    /// validated).
    /// </summary>
    public static GiverOfRunesResolution Resolve(
        Player controller,
        object? rawTarget,
        QualityPicker? qualityPicker = null,
        Creature? source = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        qualityPicker ??= ColorlessPicker;

        // CR 608.2b — target must still be a Creature on the battlefield.
        if (rawTarget is not Creature target)
        {
            return new GiverOfRunesResolution(null, null);
        }
        if (target.Zone != ZoneType.Battlefield)
        {
            return new GiverOfRunesResolution(null, null);
        }

        // CR 602.1 — "another target creature": the source cannot target
        // itself.
        if (source != null && ReferenceEquals(target, source))
        {
            return new GiverOfRunesResolution(null, null);
        }

        var quality = qualityPicker(controller, target);
        if (string.IsNullOrWhiteSpace(quality)) quality = QualityColorless;

        // Register a self-sourced GrantAbilityEffect on the target's
        // ActiveEffects so EOT cleanup runs through the continuous-effects
        // layer (CR 514.2 / CR 613.1f). Mirrors ApostlesBlessingFactory.
        if (target.ActiveEffects is not null)
        {
            var grant = new GrantAbilityEffect(
                source: target,
                target: target,
                ability: new ProtectionAbility(quality),
                expiresAtEndOfTurn: true);
            target.ActiveEffects.Register(grant);
            // Sync immediately so target legality reads the grant on the
            // same priority window (CR 117.5 / CR 700.2a).
            grant.Sync();
        }
        else
        {
            // No layers service wired (shape-only path): attach the
            // ProtectionAbility directly so the marker is inspectable.
            // EOT cleanup for this path is unavailable without a service —
            // same posture as ApostlesBlessingFactory's no-service path.
            target.AddAbility(new ProtectionAbility(quality));
        }

        return new GiverOfRunesResolution(target, quality);
    }

    /// <summary>
    /// Resolution result hook for tests / bots. <c>Target</c> is the
    /// creature that received protection; <c>Quality</c> is the chosen
    /// quality string. Both null when the target was illegal at resolve
    /// (CR 608.2b / CR 602.1 — clean no-op).
    /// </summary>
    public sealed record GiverOfRunesResolution(
        Creature? Target,
        string? Quality);

    /// <summary>
    /// Build a <see cref="QualityPicker"/> that selects the matching
    /// colour-from quality (WUBRG); any other <see cref="ManaColor"/>
    /// (including <see cref="ManaColor.Colorless"/>) falls back to
    /// <see cref="QualityColorless"/>.
    /// </summary>
    public static QualityPicker QualityFromColor(ManaColor color) =>
        (_, _) => color switch
        {
            ManaColor.White => QualityWhite,
            ManaColor.Blue => QualityBlue,
            ManaColor.Black => QualityBlack,
            ManaColor.Red => QualityRed,
            ManaColor.Green => QualityGreen,
            _ => QualityColorless,
        };
}
