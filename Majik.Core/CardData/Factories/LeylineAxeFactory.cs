using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leyline Axe (Duskmourn: House of Horror, {4}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-24):
///   "If this card is in your opening hand, you may begin the game with
///    it on the battlefield."
///   "Equipped creature gets +1/+1 and has double strike and trample."
///   "Equip {3}"
///
/// ## Why a hand-rolled C# factory over the JSON CardDefinition path
///
/// The base shape (name, Artifact, Equipment subtype, {4}) is materialised
/// from the embedded JSON definition (<c>leyline-axe.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — the same identity-from-JSON,
/// behaviour-in-C# split used by <see cref="DanithaCapashenParagonFactory"/>.
/// The equipment statics + Equip ability + Leyline opening-hand marker are
/// layered on here because the data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> has
/// no equip ability, no dynamic attached-boost effect, no attached
/// keyword-grant, and no Leyline-keyword marker shape (a JSON def alone
/// produces only a vanilla Artifact shell — same posture as
/// <see cref="ShadowspearFactory"/> / <see cref="LavaspurBootsFactory"/>).
///
/// ## Implementation
///
/// - <b>Opening-hand alt-cost</b> (CR 702.95) — a marker
///   <see cref="KeywordAbility"/>
///   (<see cref="OpeningHandLeylineAlternativeCost.LeylineKeyword"/>) so the
///   shared <see cref="OpeningHandLeylineAlternativeCost"/> subscriber picks
///   Leyline Axe up from
///   <see cref="Majik.Core.Events.OpeningHandCheckEvent"/> and prompts the
///   player to begin the game with it on the battlefield. Same wiring as the
///   rest of the Leyline cycle (Sanctity, Void, Combustion, …).
/// - <b>Static "equipped creature gets +1/+1 and has double strike and
///   trample"</b> — two <see cref="AttachedBoostEffect"/> registrations
///   (when a <see cref="ContinuousEffectsService"/> is supplied): the +1/+1
///   at Layer 7c (CR 613 Layer 7c) and the granted Double strike (CR 702.4)
///   + Trample (CR 702.19) keywords at Layer 6 (CR 613.1f). Both read the
///   source's live <see cref="Permanent.AttachedTo"/>, so re-equipping
///   transfers the bonus and detach/LTB revoke it.
///   <see cref="Majik.Core.Combat.CombatAbilities.HasDoubleStrike"/> /
///   <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> read the
///   granted markers off the equipped creature's working set. Same
///   paired-effect shape as <see cref="ShadowspearFactory"/>'s
///   "+1/+1 and has trample and lifelink".
/// - <b>Equip {3}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive, threading the
///   Puresteel-Paladin zero-equip cost-provider hook for cycle parity.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (factory-shape / dispatch
/// tests). The +1/+1 boost and Double strike / Trample grants are not
/// registered against any <see cref="ContinuousEffectsService"/> on that
/// path. Use the two-arg overload to wire the continuous effects. The
/// Leyline opening-hand marker and the Equip ability are present on both
/// paths.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (same gap as the rest of
///   the equipment cycle).
/// </summary>
[CardName("Leyline Axe")]
public static class LeylineAxeFactory
{
    public const string CardName = "Leyline Axe";
    public const string Slug = "leyline-axe";
    public const string EquipCost = "{3}";

    /// <summary>CR 702.4 — granted keyword (canonical layer-system string).</summary>
    public const string DoubleStrike = "Double strike";

    /// <summary>CR 702.19 — granted keyword (canonical layer-system string).</summary>
    public const string Trample = "Trample";

    /// <summary>
    /// Constructs Leyline Axe with no live continuous-effects wiring (the
    /// shape / dispatcher path). Neither the +1/+1 boost nor the Double
    /// strike / Trample grants are registered against any service; the
    /// Leyline opening-hand marker and the Equip {3} ability are present.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Leyline Axe. When <paramref name="continuousEffects"/> is
    /// supplied the static +1/+1 boost (Layer 7c) and the Double strike /
    /// Trample grants (Layer 6) are registered against it; each gates on the
    /// Axe being on the battlefield AND attached to a battlefield permanent.
    /// When null, both are skipped.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact,
        // Equipment, {4}). No abilities in the JSON — the Leyline marker,
        // equipment statics, and Equip ability are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // CR 702.95 — Leyline keyword marker. The shared
        // OpeningHandLeylineAlternativeCost subscriber scans hands for this
        // keyword on OpeningHandCheckEvent and prompts the agent to begin the
        // game with the Axe on the battlefield.
        card.AddAbility(new KeywordAbility(
            OpeningHandLeylineAlternativeCost.LeylineKeyword, card, owner));

        // --------------------------------------------------------------
        // Static — "Equipped creature gets +1/+1 and has double strike and
        // trample." Two AttachedBoostEffects: Layer 7c for the +1/+1, Layer
        // 6 for the granted keywords (CR 613.7c + CR 613.1f). Same
        // paired-effect shape as Shadowspear's "+1/+1 and has trample and
        // lifelink". Both gate on the source being on the battlefield AND
        // attached (see AttachedBoostEffect.IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 1, toughness: 1));

            continuousEffects.Register(
                new AttachedBoostEffect(
                    source: card,
                    power: 0,
                    toughness: 0,
                    grantedKeywords: new[] { DoubleStrike, Trample },
                    layer: Layer.Abilities));
        }

        // --------------------------------------------------------------
        // Equip {3} — activated ability (CR 702.6) via the shared
        // EquipActivatedAbility primitive, threading the Puresteel zero-cost
        // provider hook for cycle parity.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
