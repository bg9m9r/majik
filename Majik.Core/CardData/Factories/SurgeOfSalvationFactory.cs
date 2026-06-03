using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Surge of Salvation (March of the Machine, {W}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-02):
///   "You and permanents you control gain hexproof until end of turn. Prevent
///    all damage that black and/or red sources would deal to creatures you
///    control this turn."
///
/// ## Implemented (v1)
/// - <b>Card shape</b> — Instant {W} built directly (no embedded JSON
///   resource; same posture as <see cref="VeilOfSummerFactory"/>).
/// - <b>Player hexproof until end of turn (CR 702.11 / CR 514.2)</b> — the
///   caster is registered into <see cref="Majik.Core.Rules.PlayerStaticAbilities"/>
///   via a <see cref="PlayerHexproofUntilEndOfTurnEffect"/> on the caster's
///   <see cref="ContinuousEffectsService"/>, so opponents can't target the
///   caster; the grant is torn down at cleanup.
///   <see cref="Majik.Core.Targeting.TargetLegality"/> /
///   <see cref="Majik.Core.Rules.ActionValidator"/> read it.
/// - <b>"Permanents you control" hexproof until end of turn (CR 702.11)</b> —
///   each creature the caster currently controls receives a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting "Hexproof".
///   (Only creatures can be spell/ability targets in practice; the keyword set
///   is creature-scoped — non-creature permanents carry no targetable surface
///   the engine models today.)
/// - <b>Prevent black/red damage to creatures you control this turn
///   (CR 615)</b> — registers a
///   <see cref="PreventAllDamageFromColoredSourcesToControlledCreaturesShield"/>
///   for {Black, Red} on the supplied <see cref="ReplacementBus"/>; it cancels
///   every damage intent from a black-or-red source against a creature the
///   caster controls and auto-drops at cleanup.
///
/// ## v1 gaps (consistent with the rest of the engine)
/// - <b>Non-creature permanent hexproof</b>: "permanents you control" includes
///   artifacts/enchantments/lands/planeswalkers; the engine's target-legality
///   surface only reads hexproof off creatures, so the grant is applied to the
///   controlled creatures. Planeswalker/other-permanent target legality from
///   hexproof is not yet modelled (no card in the pool can target those under
///   hexproof differently today).
/// </summary>
[CardName("Surge of Salvation")]
public static class SurgeOfSalvationFactory
{
    public const string CardName = "Surge of Salvation";
    public const string PrintedManaCost = "{W}";

    /// <summary>The colours whose sources are prevented (CR 615).</summary>
    public static readonly ManaColor[] PreventedColors = { ManaColor.Black, ManaColor.Red };

    /// <summary>Card shape. Instant {W}.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. No targets, no
    /// modes. The hexproof grants need <paramref name="continuousEffects"/>
    /// and the damage shield needs <paramref name="replacements"/>; either may
    /// be null in shape-only test paths (the corresponding clause is skipped).
    /// </summary>
    public static SpellDefinition BuildDefinition(
        Player caster,
        ContinuousEffectsService? continuousEffects,
        ReplacementBus? replacements) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect(
                    "Surge of Salvation — you + permanents gain hexproof; prevent black/red damage to your creatures",
                    () => Resolve(caster, continuousEffects, replacements)),
            });

    /// <summary>
    /// Apply Surge of Salvation's two clauses. Exposed for tests / bots without
    /// driving the full cast flow.
    /// </summary>
    public static void Resolve(
        Player caster,
        ContinuousEffectsService? continuousEffects,
        ReplacementBus? replacements)
    {
        if (caster == null) return;

        // 1. "You ... gain hexproof until end of turn." (CR 702.11 / CR 514.2)
        //    Player hexproof via the layers service so cleanup tears it down.
        //    "... and permanents you control" → every controlled creature gains
        //    the Hexproof keyword for the turn.
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new PlayerHexproofUntilEndOfTurnEffect(new[] { caster }));

            foreach (var creature in caster.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
            {
                continuousEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creature, "Hexproof"));
            }
        }

        // 2. "Prevent all damage that black and/or red sources would deal to
        //    creatures you control this turn." (CR 615)
        if (replacements != null)
        {
            replacements.Register(
                new PreventAllDamageFromColoredSourcesToControlledCreaturesShield(
                    caster, PreventedColors));
        }
    }
}
