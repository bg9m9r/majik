using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Trash for Treasure (Fifth Dawn / Modern Horizons 2,
/// {2}{R}).
///
/// Sorcery. Oracle text:
///   "As an additional cost to cast this spell, sacrifice an artifact.
///    Return target artifact card from your graveyard to the battlefield."
///
/// ## Why it gets its own factory
/// Trash for Treasure is the canonical "sac-an-artifact → reanimate-an-
/// artifact" engine card and the lynchpin of every Modern artifact
/// combo shell that wants to upgrade cheap-fodder (Mishra's Bauble,
/// Chromatic Star, Ichor Wellspring) into Wurmcoil Engine / Karn,
/// Liberated / Sundering Titan / Triplicate Titan. Three-mana
/// sorcery-speed artifact reanimation pairs particularly well with
/// Goblin Welder + Daretti recursion, and the Welder pillar already
/// ships in this engine. The sacrifice-target distinction matters:
/// unlike Reanimate (creature, life-loss rider) or Goryo's Vengeance
/// (legendary creature, EOT exile), Trash for Treasure reanimates any
/// artifact without a downside or sticker rider — making it a clean
/// "trade cheap artifact for expensive artifact" upgrade.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{R}.
/// - Additional cost (CR 601.2f):
///   <see cref="SacrificeAnArtifactAdditionalCost"/> — the cast flow
///   pre-check (<see cref="SpellCastFlow"/>) rejects the cast when the
///   caster controls no artifact (CR 601.2g — additional cost that
///   can't be paid → cast is illegal). Same posture as
///   <see cref="BoneSplintersFactory"/> for the creature-sac variant.
/// - <b>Return target artifact card from your graveyard to the
///   battlefield</b> — <see cref="BuildSpellDefinition"/> /
///   <see cref="BuildResolveEffect"/> picks an artifact card from the
///   caster's graveyard (v1 deterministic first-match — same shape as
///   <see cref="ReanimateFactory"/> and Priest of Fell Rites) and
///   routes the move through <see cref="Fx.ReturnFromGraveyardToBattlefield"/>
///   so ETB triggers on the reanimated artifact (Wurmcoil Engine's
///   Lifelink + Deathtouch markers, Cranial Plating's static, etc.)
///   fire correctly (CR 603.6a) when a live
///   <see cref="ZoneService"/> is supplied. Empty graveyard / no
///   artifact = clean no-op (CR 117.x — "target" effect with no legal
///   target).
///
/// ## Deferred (v1 gaps)
/// - <b>Real target prompt</b>: "target artifact card from your
///   graveyard" needs an agent-driven choose-from-graveyard prompt.
///   v1 picks deterministically — same shape as
///   <see cref="ReanimateFactory"/> /
///   <see cref="GoryosVengeanceFactory"/>.
/// - <b>Sacrifice target prompt</b>: the
///   <see cref="SacrificeAnArtifactAdditionalCost"/> picker chooses the
///   first artifact on the caster's battlefield deterministically.
///   Real agent-driven sacrifice prompting awaits the
///   ITarget / TargetResolver pipeline (same v1 posture as
///   Bone Splinters).
/// - <b>Multi-graveyard scan</b>: <see cref="BuildResolveEffect"/>
///   only scans the caster's graveyard. Real Trash for Treasure says
///   "from your graveyard" — exactly correct — so multi-graveyard
///   scan is intentionally NOT exposed (unlike
///   <see cref="ReanimateFactory"/>, which carries an optional
///   all-players-resolver because Reanimate's printed text is "from a
///   graveyard").
/// </summary>
[CardName("Trash for Treasure")]
public static class TrashForTreasureFactory
{
    public const string CardName = "Trash for Treasure";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>Printed oracle text. Kept here so the data-driven
    /// import path can cross-check the named factory against
    /// Scryfall.</summary>
    public const string OracleText =
        "As an additional cost to cast this spell, sacrifice an artifact. " +
        "Return target artifact card from your graveyard to the battlefield.";

    /// <summary>
    /// Build a Trash for Treasure sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time effect is built on demand via
    /// <see cref="BuildSpellDefinition"/> /
    /// <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Trash for Treasure uses on
    /// resolution. Declares the sacrifice-an-artifact additional cost
    /// (CR 601.2f) and a target-artifact-card-from-your-graveyard request
    /// (CR 117.1 — "target"); on resolution the targeted artifact card
    /// is returned from the caster's graveyard to the battlefield under
    /// the caster's control (CR 701.20).
    /// </summary>
    /// <param name="caster">Spell controller — graveyard source +
    /// battlefield destination.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard
    /// → battlefield move routes through
    /// <see cref="ZoneService.MoveCard"/> so ETB triggers on the
    /// reanimated artifact fire (CR 603.6a).</param>
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
            // (same v1 posture as ReanimateFactory). The CR 117.1
            // "target" requirement is documented above; the agent-prompt
            // shape lands behind the choose-from-graveyard queue with
            // Reanimate / Goryo's Vengeance.
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, zoneService),
            AdditionalCosts: new IAdditionalCost[]
            {
                new SacrificeAnArtifactAdditionalCost(),
            });
    }

    /// <summary>
    /// Build Trash for Treasure's resolve effect — reanimate an artifact
    /// card from the caster's graveyard (deterministic first-match v1).
    /// </summary>
    /// <param name="caster">Spell controller — graveyard source +
    /// battlefield destination.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard
    /// → battlefield move routes through
    /// <see cref="ZoneService.MoveCard"/> so ETB triggers fire
    /// (CR 603.6a).</param>
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
        // Tokens never end up in the graveyard (CR 110.5g), so HasType
        // alone is sufficient — no extra "not a token" filter required.
        var pick = caster.Zones.Graveyard.GetCards()
            .FirstOrDefault(c => c.HasType(CardType.Artifact));
        if (pick == null) return;

        // CR 701.20 — graveyard → battlefield. Fx routes through
        // ZoneService when supplied so ETB triggers (Wurmcoil Engine's
        // markers, Solemn Simulacrum, etc.) fire on the reanimated
        // artifact (CR 603.6a). Raw-zone fallback sets controller too,
        // matching ReanimateFactory's shape.
        Fx.ReturnFromGraveyardToBattlefield(pick, caster, zoneService);
    }
}
