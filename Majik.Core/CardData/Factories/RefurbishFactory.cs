using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Refurbish (Aether Revolt / Dominaria, {3}{W}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-05-29):
///   "Return target artifact card from your graveyard to the battlefield."
///
/// ## Relationship to the analogues
/// Refurbish is the rider-free sibling of
/// <see cref="TrashForTreasureFactory"/>: same "reanimate an artifact card
/// from YOUR graveyard to the battlefield" effect (CR 701.20), but with no
/// sacrifice additional cost and no life-loss tail. It narrows the
/// graveyard-reanimation target filter from creature (as in
/// <see cref="ReanimateFactory"/> / <see cref="ExhumeFactory"/>) to
/// <see cref="CardType.Artifact"/>.
///
/// The base card shape (name / Sorcery type / {3}{W} cost) is materialised
/// from the embedded JSON definition (<c>refurbish.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the resolve-time reanimation is
/// layered on here because the JSON <c>AbilityDefinition</c> schema does not
/// (yet) express "return target artifact card from your graveyard" — same
/// posture as <see cref="TrashForTreasureFactory"/>.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {3}{W}.
/// - <b>Return target artifact card from your graveyard to the
///   battlefield</b> — <see cref="BuildSpellDefinition"/> /
///   <see cref="BuildResolveEffect"/> picks an artifact card from the
///   caster's graveyard (v1 deterministic first-match — same shape as
///   <see cref="TrashForTreasureFactory"/> and <see cref="ReanimateFactory"/>)
///   and routes the move through
///   <see cref="Fx.ReturnFromGraveyardToBattlefield"/> so ETB triggers on the
///   reanimated artifact fire (CR 603.6a) when a live
///   <see cref="ZoneService"/> is supplied. Empty graveyard / no artifact =
///   clean no-op (CR 117.x — "target" effect with no legal target).
///
/// ## Deferred (v1 gaps)
/// - <b>Real target prompt</b>: "target artifact card from your graveyard"
///   needs an agent-driven choose-from-graveyard prompt. v1 picks
///   deterministically — same shape as
///   <see cref="TrashForTreasureFactory"/> / <see cref="ReanimateFactory"/>.
/// - <b>Multi-graveyard scan</b>: Refurbish prints "from your graveyard", so
///   the resolve body intentionally scans only the caster's graveyard — no
///   all-players resolver is exposed (matches
///   <see cref="TrashForTreasureFactory"/>; unlike
///   <see cref="ReanimateFactory"/>, whose printed text is "from a graveyard").
/// </summary>
[CardName("Refurbish")]
public static class RefurbishFactory
{
    public const string CardName = "Refurbish";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "refurbish";

    /// <summary>Printed mana cost — kept here so the data-driven import path
    /// can cross-check the named factory against Scryfall.</summary>
    public const string PrintedManaCost = "{3}{W}";

    /// <summary>Printed oracle text — cross-checked at import time against
    /// Scryfall.</summary>
    public const string OracleText =
        "Return target artifact card from your graveyard to the battlefield.";

    /// <summary>
    /// Build a Refurbish sorcery owned by <paramref name="owner"/>. Base
    /// shape (name / Sorcery / {3}{W}) is materialised from the embedded
    /// JSON; the resolve-time effect is built on demand via
    /// <see cref="BuildSpellDefinition"/> / <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Refurbish uses on resolution.
    /// Declares a target-artifact-card-from-your-graveyard request
    /// (CR 117.1 — "target"); on resolution the targeted artifact card is
    /// returned from the caster's graveyard to the battlefield under the
    /// caster's control (CR 701.20). No additional costs, no life-loss rider.
    /// </summary>
    /// <param name="caster">Spell controller — graveyard source +
    /// battlefield destination.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard →
    /// battlefield move routes through <see cref="ZoneService.MoveCard"/> so
    /// ETB triggers on the reanimated artifact fire (CR 603.6a).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            // No target prompt in v1 — the resolve body picks the first
            // artifact card in the caster's graveyard deterministically
            // (same posture as TrashForTreasureFactory). The CR 117.1
            // "target" requirement is documented above; the agent-prompt
            // shape lands behind the choose-from-graveyard queue.
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, zoneService));
    }

    /// <summary>
    /// Build Refurbish's resolve effect — reanimate an artifact card from the
    /// caster's graveyard (deterministic first-match v1).
    /// </summary>
    /// <param name="caster">Spell controller — graveyard source +
    /// battlefield destination.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard →
    /// battlefield move routes through <see cref="ZoneService.MoveCard"/> so
    /// ETB triggers fire (CR 603.6a).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            Fx.Inline(
                $"{CardName}: reanimate artifact card from your graveyard",
                () => Resolve(caster, zoneService)),
        };
    }

    /// <summary>
    /// Shared resolution helper — picks the first artifact card in the
    /// caster's graveyard and moves it to the caster's battlefield via
    /// <see cref="Fx.ReturnFromGraveyardToBattlefield"/>. CR 117.x —
    /// "target" effect with no legal target is a clean no-op.
    /// </summary>
    private static void Resolve(Player caster, ZoneService? zoneService)
    {
        // v1 deterministic pick: first artifact card in caster's graveyard.
        // Tokens never end up in the graveyard (CR 110.5g), so HasType alone
        // is sufficient — no extra "not a token" filter required.
        var pick = caster.Zones.Graveyard.GetCards()
            .FirstOrDefault(c => c.HasType(CardType.Artifact));
        if (pick == null) return;

        // CR 701.20 — graveyard → battlefield. Fx routes through ZoneService
        // when supplied so ETB triggers (Wurmcoil Engine's markers, Solemn
        // Simulacrum, etc.) fire on the reanimated artifact (CR 603.6a).
        // Raw-zone fallback sets controller too (CR 110.2).
        Fx.ReturnFromGraveyardToBattlefield(pick, caster, zoneService);
    }
}
