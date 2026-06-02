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
/// Named-card factory for Mother of Runes (Urza's Saga, {W}).
///
/// Creature — Human Cleric 1/1. Oracle text (verified against Scryfall
/// 2026-06-01):
///   "{T}: Target creature you control gains protection from the color of
///    your choice until end of turn."
///
/// Near-functional twin of <see cref="GiverOfRunesFactory"/>. Two
/// differences:
///   1. The target is "target creature you control" — NOT "another", so
///      Mother of Runes CAN target itself (CR 602.1 has no self-exclusion
///      here).
///   2. The chosen quality is one of the five colours (WUBRG); the printed
///      text offers no "colorless" option, unlike Giver of Runes.
///
/// The card's base shape (name, Creature, Human + Cleric subtypes, {W}, 1/1)
/// is materialised from the embedded JSON definition
/// (<c>mother-of-runes.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The {T} protection-grant
/// activated ability is layered on top here (the JSON
/// <c>AbilityDefinition</c> schema doesn't express tap-cost activated
/// abilities with a free-text protection quality — same posture as
/// <see cref="GiverOfRunesFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Activated ability (CR 602.1)</b>:
///   <c>{T}: Target creature you control gains protection from the color of
///   your choice until end of turn.</c> Cost = <see cref="AdditionalCost.Tap"/>
///   (the printed {T}). The chosen target is a 1..1 "target creature you
///   control" <see cref="TargetRequest"/>; the <c>CandidateGatherer</c>
///   scopes to controller-side creatures INCLUDING Mother of Runes itself.
/// - <b>Resolution</b> mirrors <see cref="GiverOfRunesFactory.Resolve"/>:
///   CR 608.2b re-validates the target (still a battlefield Creature you
///   control), then registers a self-sourced
///   <see cref="GrantAbilityEffect"/> on the target's
///   <see cref="Creature.ActiveEffects"/> that adds a
///   <see cref="ProtectionAbility"/> with the chosen colour quality until
///   end of turn (CR 514.2 / CR 613.1f). The quality is one of "white" /
///   "blue" / "black" / "red" / "green", selected by an injectable
///   <see cref="QualityPicker"/> (defaults to "white" — the first colour in
///   WUBRG).
///
/// ## v1 gaps (consistent with the rest of the engine)
///
/// - <b>Agent-side colour prompt</b>: CR 601.2c — "of your choice" is chosen
///   as the ability is put on the stack. v1 uses an injectable
///   <see cref="QualityPicker"/> Func; the dispatcher path defaults to
///   "white". Same posture as <see cref="GiverOfRunesFactory"/>.
/// - <b>Target-on-activation honouring</b>: the activated ability attaches
///   structurally; the chosen target is honoured on resolution via
///   <see cref="ActivatedAbility.ChosenTargets"/> (set by the production
///   agent or directly by tests), same as <see cref="GiverOfRunesFactory"/>.
/// </summary>
[CardName("Mother of Runes")]
public static class MotherOfRunesFactory
{
    public const string CardName = "Mother of Runes";
    public const string Slug = "mother-of-runes";

    /// <summary>
    /// Resolve-time picker for the protection colour quality. Receives the
    /// controller and the chosen target; returns one of
    /// <see cref="QualityWhite"/>, <see cref="QualityBlue"/>,
    /// <see cref="QualityBlack"/>, <see cref="QualityRed"/>,
    /// <see cref="QualityGreen"/>. Default (when no picker is supplied)
    /// returns <see cref="QualityWhite"/> — the first colour in WUBRG.
    /// </summary>
    public delegate string QualityPicker(Player controller, ICard target);

    public const string QualityWhite = "white";
    public const string QualityBlue = "blue";
    public const string QualityBlack = "black";
    public const string QualityRed = "red";
    public const string QualityGreen = "green";

    /// <summary>Default picker — "protection from white".</summary>
    public static readonly QualityPicker WhitePicker = (_, _) => QualityWhite;

    /// <summary>
    /// Construct Mother of Runes owned and controlled by
    /// <paramref name="owner"/>. The {T} protection-grant ability is
    /// attached structurally; the chosen target is honoured on resolution
    /// via <see cref="ActivatedAbility.ChosenTargets"/>. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human + Cleric subtypes, {W}, 1/1). The JSON carries no abilities —
        // the {T} grant is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {T}: Target creature you control gains protection from the color
        // of your choice until end of turn. CR 602.1 — activated ability
        // with a tap cost. Unlike Giver of Runes this targets ANY creature
        // you control (including this one). The chosen target is honoured on
        // resolution (CR 608.2b re-validates).
        // ----------------------------------------------------------------
        ActivatedAbility? grantAbility = null;
        var grantEffect = new Effect(
            $"{CardName}: target creature you control gains protection (chosen colour) EOT",
            () =>
            {
                if (grantAbility == null) return;
                var chosen = grantAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                // Default quality is "white"; agents / tests supply a colour
                // via an injectable picker. Here the picker defaults to white
                // on the dispatcher path (no agent colour prompt yet — see
                // class xmldoc).
                Resolve(owner, chosen[0][0], WhitePicker);
            });

        grantAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { grantEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    // CR 602.1 — "target creature you control" (no "another"
                    // exclusion); Mother of Runes is itself a legal target.
                    CandidateGatherer: _ => owner.Zones.Battlefield.GetCards()
                        .Where(c => c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(grantAbility);

        return card;
    }

    /// <summary>
    /// Resolve Mother of Runes' grant against <paramref name="rawTarget"/>.
    /// Exposed for direct invocation by tests / bots without driving the
    /// full activation flow.
    /// </summary>
    public static MotherOfRunesResolution Resolve(
        Player controller,
        object? rawTarget,
        QualityPicker? qualityPicker = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        qualityPicker ??= WhitePicker;

        // CR 608.2b — target must still be a Creature on the battlefield.
        if (rawTarget is not Creature target)
        {
            return new MotherOfRunesResolution(null, null);
        }
        if (target.Zone != ZoneType.Battlefield)
        {
            return new MotherOfRunesResolution(null, null);
        }

        var quality = qualityPicker(controller, target);
        if (string.IsNullOrWhiteSpace(quality)) quality = QualityWhite;

        // Register a self-sourced GrantAbilityEffect on the target's
        // ActiveEffects so EOT cleanup runs through the continuous-effects
        // layer (CR 514.2 / CR 613.1f). Mirrors GiverOfRunesFactory.
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
            target.AddAbility(new ProtectionAbility(quality));
        }

        return new MotherOfRunesResolution(target, quality);
    }

    /// <summary>
    /// Resolution result hook for tests / bots. <c>Target</c> is the
    /// creature that received protection; <c>Quality</c> is the chosen
    /// colour quality string. Both null when the target was illegal at
    /// resolve (CR 608.2b — clean no-op).
    /// </summary>
    public sealed record MotherOfRunesResolution(
        Creature? Target,
        string? Quality);

    /// <summary>
    /// Build a <see cref="QualityPicker"/> that selects the matching
    /// colour-from quality (WUBRG); any non-WUBRG <see cref="ManaColor"/>
    /// (including <see cref="ManaColor.Colorless"/>) falls back to
    /// <see cref="QualityWhite"/> — Mother of Runes only offers colours.
    /// </summary>
    public static QualityPicker QualityFromColor(ManaColor color) =>
        (_, _) => color switch
        {
            ManaColor.White => QualityWhite,
            ManaColor.Blue => QualityBlue,
            ManaColor.Black => QualityBlack,
            ManaColor.Red => QualityRed,
            ManaColor.Green => QualityGreen,
            _ => QualityWhite,
        };
}
