using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Toxic Deluge (Commander 2013, {2}{B}).
///
/// Sorcery. Oracle text (Scryfall):
///   "As an additional cost to cast this spell, pay X life.
///    All creatures get -X/-X until end of turn."
///
/// ## Cast-time pipeline (current)
///
/// <see cref="BuildSpellDefinition"/> returns the
/// <see cref="SpellDefinition"/> that <see cref="SpellCastFlow"/> drives:
///
/// 1. <b>HasVariableX = true</b> — the cast flow prompts the agent via
///    <c>ChooseXAsync</c> and stamps the chosen X on the card via
///    <see cref="Card.SetPendingCastX"/> BEFORE the CR 601.2f
///    additional-cost loop runs.
/// 2. <b>AdditionalCosts = [<see cref="PayLifeAdditionalCost"/>(card,
///    variableX: true)]</b> — the additional cost reads
///    <see cref="Card.PendingCastX"/> and deducts the chosen X from the
///    caster's life total at cast time (CR 118.8 / CR 601.2f). The pre-
///    check rejects the cast cleanly when the caster lacks the life
///    (CR 119.4 / CR 601.2g — illegal cast, no zone mutation).
/// 3. <b>EffectFactory</b> reads X off
///    <see cref="ChosenSpellParams.X"/> (the cast flow's authoritative
///    source) and builds the -X/-X EOT sweep — same shape every -N/-N
///    sweep uses (mirrors <see cref="LanguishFactory.BuildResolveEffect"/>).
///    No life payment in the resolve body; that already fired at cast time.
///
/// ## Why a named factory (over the existing template)
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Counters.AllCreaturesPumpTemplate"/>
/// only binds fixed -N/-N text (e.g. Languish, Infest); the printed
/// Toxic Deluge text references "X" twice (additional cost + sweep
/// magnitude) which the regex template doesn't model. The named factory
/// carries both halves — pay-X-life additional cost AND magnitude-driven
/// sweep — in one <see cref="SpellDefinition"/> with X plumbed via
/// <see cref="Card.PendingCastX"/>.
///
/// ## Pay-life ordering (resolved)
/// Earlier v1 folded the life payment into the resolve effect because
/// the engine didn't expose a spell-time pay-life additional-cost hook
/// for sorceries. <see cref="PayLifeAdditionalCost"/> closes that gap:
/// the payment now happens at cast time (CR 601.2f), so Sanguine Bond /
/// Vito / Vizkopa Guildmage-style life-loss triggers fire when the
/// spell is cast — not when it resolves — and a counter on the resolving
/// Toxic Deluge correctly leaves the paid X life lost (matching paper).
///
/// ## Indestructible / protection
/// Not handled here — the layer system applies -X/-X uniformly; SBA
/// death is the standard toughness-0 check (CR 704.5f). Indestructible
/// creatures with positive base toughness don't die from -X/-X going to
/// ≤ 0 unless X ≥ base toughness AND not indestructible (Rule 704.5f
/// — checks toughness, not damage). Standard layer-system semantics.
///
/// CR rule references: 107.1b (X in costs / effects), 109.5 (symmetric
/// sweep), 117.5 (mana cost), 118.8 (life payment), 119.4 (can't pay
/// life you don't have), 514.2 (EOT cleanup), 601.2f (additional costs
/// at cast time), 601.2g (illegal cast rewind), 613.4 (continuous-effects
/// layer 7c), 704.5f (toughness 0 creature-death SBA).
/// </summary>
[CardName("Toxic Deluge")]
public static class ToxicDelugeFactory
{
    public const string CardName = "Toxic Deluge";
    public const string PrintedManaCost = "{2}{B}";

    /// <summary>
    /// Default X used by <see cref="BuildResolveEffect"/>'s shape-only
    /// helper (tests / call sites that bypass <see cref="SpellCastFlow"/>).
    /// Midrange wrath value — wipes most aggro / midrange creatures
    /// (toughness ≤ 5) without burning the caster down. The real cast
    /// pipeline drives X via <see cref="SpellDefinition.HasVariableX"/>
    /// + <see cref="Card.PendingCastX"/>.
    /// </summary>
    public const int DefaultX = 5;

    /// <summary>
    /// Build a Toxic Deluge sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect (pay X life + -X/-X sweep)
    /// is built on demand via <see cref="BuildResolveEffect"/> or via
    /// <see cref="BuildSpellDefinition"/> for the full cast pipeline.
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
    /// Build the cast-time <see cref="SpellDefinition"/> for Toxic Deluge
    /// — the entry point <see cref="SpellCastFlow"/> drives.
    ///
    /// <list type="number">
    ///   <item><b>HasVariableX = true</b> — the cast flow prompts
    ///     <c>ChooseXAsync</c> and stamps X on the card via
    ///     <see cref="Card.SetPendingCastX"/>.</item>
    ///   <item><b>AdditionalCosts</b> carries the
    ///     <see cref="PayLifeAdditionalCost"/>(<paramref name="card"/>,
    ///     variableX: true) rider; the cast flow pre-checks legality
    ///     (CR 119.4) and pays X life at cast time (CR 601.2f /
    ///     CR 118.8).</item>
    ///   <item><b>EffectFactory</b> reads X off
    ///     <see cref="ChosenSpellParams.X"/> and registers a
    ///     <see cref="PumpUntilEndOfTurnEffect"/>(c, -X, -X) per creature
    ///     on every supplied player's battlefield (CR 109.5 — symmetric
    ///     sweep; CR 613.4 layer 7c modify with EOT expiry).</item>
    /// </list>
    /// </summary>
    /// <param name="card">The Toxic Deluge card being cast. Must match
    /// the card the cast flow will <see cref="Card.SetPendingCastX"/>
    /// against (same reference).</param>
    public static SpellDefinition BuildSpellDefinition(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: chosen =>
            {
                var x = chosen.X ?? 0;
                var players = chosen.AllPlayers ?? Array.Empty<Player>();
                return BuildResolveEffect(players, caster: null, x: x);
            },
            AdditionalCosts: new IAdditionalCost[]
            {
                new PayLifeAdditionalCost(card, variableX: true),
            });
    }

    /// <summary>
    /// Build Toxic Deluge's resolve effect — the -X/-X EOT sweep only.
    ///
    /// <para>
    /// The life payment is paid at <em>cast</em> time by
    /// <see cref="PayLifeAdditionalCost"/> (see class doc), not here.
    /// </para>
    ///
    /// <para>
    /// Back-compat note: prior to the
    /// <see cref="PayLifeAdditionalCost"/> primitive landing, this
    /// method paid X life on resolve when <paramref name="caster"/> was
    /// non-null. To preserve that surface for tests / direct callers
    /// that aren't going through <see cref="SpellCastFlow"/> (where the
    /// additional-cost loop already deducted the life),
    /// <paramref name="caster"/> still triggers a one-shot life payment
    /// here. Cast-pipeline callers pass <c>caster: null</c>.
    /// </para>
    ///
    /// Register a <see cref="PumpUntilEndOfTurnEffect"/>(c, -X, -X) per
    /// creature on every supplied player's battlefield (CR 109.5 —
    /// symmetric sweep). EOT cleanup is handled by the shared layer-
    /// system expiry (CR 514.2).
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>.</param>
    /// <param name="caster">When non-null, this caller is responsible
    /// for the life payment (back-compat path for callers that bypass
    /// the cast pipeline). Cast-pipeline callers pass null so the life
    /// payment isn't double-charged.</param>
    /// <param name="x">The chosen X — magnitude of the -X/-X sweep
    /// (and of the back-compat life payment when caster is set). Must
    /// be non-negative; defaults to <see cref="DefaultX"/>.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers,
        Player? caster = null,
        int x = DefaultX)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);
        if (x < 0)
            throw new ArgumentOutOfRangeException(nameof(x),
                "Toxic Deluge X must be non-negative (CR 107.1b — X in costs).");

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: all creatures -{x}/-{x} EOT",
                () =>
                {
                    // ---------------------------------------------------------
                    // Back-compat: legacy direct callers that pass `caster`
                    // still get the life payment folded into resolve. New cast
                    // pipeline callers pass `caster: null` because
                    // PayLifeAdditionalCost already deducted the life at cast
                    // time (CR 601.2f). Clamp to LifeTotal so a too-large X
                    // doesn't ArgumentOutOfRange on Player.LoseLife.
                    // ---------------------------------------------------------
                    if (caster != null && x > 0)
                    {
                        var paid = Math.Min(x, caster.LifeTotal);
                        if (paid > 0) caster.LoseLife(paid);
                    }

                    // ---------------------------------------------------------
                    // Symmetric -X/-X sweep (CR 109.5 + CR 613.4). Same shape
                    // every -N/-N sweep uses; EOT cleanup runs through the
                    // shared layer-system expiry (CR 514.2).
                    // ---------------------------------------------------------
                    foreach (var pl in allPlayers)
                    {
                        foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                        {
                            if (c.ActiveEffects != null)
                            {
                                c.ActiveEffects.Register(
                                    new PumpUntilEndOfTurnEffect(c, -x, -x));
                            }
                        }
                    }
                }),
        };
    }
}
