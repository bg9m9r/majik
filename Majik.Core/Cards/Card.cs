using Majik.Core.Abilities;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Base card implementation.
/// Encapsulates card state and uses value objects.
/// </summary>
public class Card : ICard
{
    private ZoneType _zone;
    private Player? _controller;
    private readonly List<CardType> _cardTypes = new();
    private readonly List<CardSupertype> _supertypes = new();
    private readonly List<CardSubtype> _subtypes = new();
    private readonly List<IAbility> _abilities = new();

    public Guid InstanceId { get; } = Guid.NewGuid();
    public string Name { get; }
    public string ManaCost { get; }

    /// <summary>
    /// See <see cref="ICard.IsVanillaShell"/>. Stamped by
    /// <see cref="Majik.Core.CardData.ScryfallCardFactory.Create"/> after
    /// the binder chain runs, when the card carries printed oracle text
    /// but the engine produced no abilities to enforce it. Mutated through
    /// <see cref="MarkAsVanillaShell"/> so the factory has a public seam
    /// without exposing the setter for general use.
    /// </summary>
    public bool IsVanillaShell { get; private set; }

    /// <summary>
    /// Flip this card to the vanilla-shell state. Called by
    /// <see cref="Majik.Core.CardData.ScryfallCardFactory.Create"/> once it
    /// has run the full binder chain and observed that no abilities were
    /// attached for a card whose oracle text demanded them. Idempotent;
    /// re-flipping is a no-op.
    /// </summary>
    public void MarkAsVanillaShell()
    {
        IsVanillaShell = true;
    }
    
    /// <summary>
    /// The mana cost as a value object.
    /// </summary>
    public ValueObjects.ManaCost ManaCostValue { get; }

    /// <summary>
    /// The card types (cards can have multiple types).
    /// </summary>
    public IReadOnlyList<CardType> CardTypes => _cardTypes.AsReadOnly();

    /// <summary>
    /// The card supertypes.
    /// </summary>
    public IReadOnlyList<CardSupertype> Supertypes => _supertypes.AsReadOnly();

    /// <summary>
    /// The card subtypes.
    /// </summary>
    public IReadOnlyList<CardSubtype> Subtypes => _subtypes.AsReadOnly();
    
    public Player? Owner { get; internal set; }

    public Player? Controller
    {
        get => _controller;
        internal set => _controller = value;
    }

    public ZoneType Zone
    {
        get => _zone;
        internal set => _zone = value;
    }

    /// <summary>
    /// CR 110.2 — change the controller of this card. Public seam for
    /// external code (effects, commands) instead of touching the setter.
    /// </summary>
    public void ChangeController(Player? newController)
    {
        _controller = newController;
    }

    /// <summary>
    /// Set the owner of this card. Typically called once at card creation
    /// (CR 108.3); ownership only changes via specific effects.
    /// </summary>
    public void ChangeOwner(Player? newOwner)
    {
        Owner = newOwner;
    }

    /// <summary>
    /// CR 702.33 — runtime flashback grant ("target … gains flashback until
    /// end of turn. The flashback cost is equal to its mana cost.").
    /// When non-null, the card may be cast from the graveyard via a
    /// <see cref="Majik.Core.Costs.FlashbackAlternativeCost"/> built from
    /// this mana cost, in addition to any printed flashback. Cleared at
    /// end of turn by the granting effect's bus subscription.
    /// </summary>
    public ValueObjects.ManaCost? RuntimeFlashbackCost { get; private set; }

    /// <summary>
    /// Stamp a flashback grant on this card. Used by Snapcaster Mage and
    /// any future "until end of turn, gains flashback" effect. Idempotent —
    /// later grants overwrite earlier ones; clearing happens at EOT via the
    /// granting effect's own bookkeeping.
    /// </summary>
    public void GrantRuntimeFlashback(ValueObjects.ManaCost cost)
    {
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        RuntimeFlashbackCost = cost;
    }

    /// <summary>Clear any runtime flashback grant on this card.</summary>
    public void ClearRuntimeFlashback()
    {
        RuntimeFlashbackCost = null;
    }

    /// <summary>
    /// CR 118.9 — runtime "may cast from graveyard" grant. Used by static
    /// abilities such as Lurrus of the Dream-Den that allow casting a
    /// permanent card from the graveyard for its printed mana cost. When
    /// non-null, the card may be cast from the graveyard via a
    /// <see cref="Majik.Core.Costs.GraveyardCastAlternativeCost"/> built
    /// from this cost. Unlike <see cref="RuntimeFlashbackCost"/>, the
    /// resolved card returns to its default destination (battlefield for
    /// permanents) — there is no post-resolution exile.
    /// </summary>
    public ValueObjects.ManaCost? RuntimeGraveyardCastCost { get; private set; }

    /// <summary>
    /// Stamp a graveyard-cast grant on this card. Used by Lurrus of the
    /// Dream-Den's static ability while it is on the battlefield, scoped
    /// to permanent cards in its controller's graveyard with mana value
    /// 2 or less. Idempotent — later grants overwrite earlier ones.
    /// </summary>
    public void GrantRuntimeGraveyardCast(ValueObjects.ManaCost cost)
    {
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        RuntimeGraveyardCastCost = cost;
    }

    /// <summary>Clear any runtime graveyard-cast grant on this card.</summary>
    public void ClearRuntimeGraveyardCast()
    {
        RuntimeGraveyardCastCost = null;
    }

    /// <summary>
    /// CR 118.9 — runtime "you may cast this card from exile until end of
    /// turn" grant. Stamped by effects such as Ragavan, Nimble Pilferer's
    /// combat-damage trigger ("Until end of turn, you may cast that card").
    /// When non-null, the named player may cast the card from the Exile
    /// zone via a <see cref="Majik.Core.Costs.ExileCastAlternativeCost"/>
    /// built from the printed mana cost; unlike Suspend / Cascade, the
    /// allowed caster is NOT the card's owner (Ragavan exiles from the
    /// damaged player's library — the granted permission lets the
    /// Ragavan controller cast the opponent's card). Cleared at end of
    /// turn by the granting effect's bus subscription.
    /// </summary>
    public Player? RuntimeExileCastAllowedCaster { get; private set; }

    /// <summary>
    /// The mana cost the <see cref="RuntimeExileCastAllowedCaster"/> pays
    /// to cast this card from exile under the runtime grant. Typically the
    /// card's printed mana cost (per Ragavan's "you may cast that card").
    /// </summary>
    public ValueObjects.ManaCost? RuntimeExileCastCost { get; private set; }

    /// <summary>
    /// Stamp an exile-cast grant on this card. <paramref name="allowedCaster"/>
    /// is the player who may cast (need not be the owner); <paramref name="cost"/>
    /// is the mana cost they pay (typically the card's printed cost).
    /// Idempotent — later grants overwrite earlier ones. Cleared at EOT by
    /// the granting effect's bookkeeping.
    /// </summary>
    public void GrantRuntimeExileCast(Player allowedCaster, ValueObjects.ManaCost cost)
    {
        if (allowedCaster == null) throw new ArgumentNullException(nameof(allowedCaster));
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        RuntimeExileCastAllowedCaster = allowedCaster;
        RuntimeExileCastCost = cost;
    }

    /// <summary>Clear any runtime exile-cast grant on this card.</summary>
    public void ClearRuntimeExileCast()
    {
        RuntimeExileCastAllowedCaster = null;
        RuntimeExileCastCost = null;
    }

    /// <summary>
    /// CR 702.143 — runtime Escape grant. When non-null, the card has
    /// Escape while in its owner's graveyard, with this mana cost as the
    /// alt-cost mana payment and <see cref="RuntimeEscapeExileCount"/> as
    /// the "exile N other cards from your graveyard" rider. Stamped by
    /// effects such as Underworld Breach ("Each nonland card in your
    /// graveyard has escape and 'Escape—[printed mana cost], exile three
    /// other cards from your graveyard.'"). The
    /// <see cref="Majik.Core.Players.Agents.EscapeAltCostProbe.DefaultLookup"/>
    /// consults this field so a granted escape surfaces to the bot's
    /// alt-cost enumeration alongside the printed-escape ship list.
    /// </summary>
    public ValueObjects.ManaCost? RuntimeEscapeCost { get; private set; }

    /// <summary>
    /// The "exile N other cards from your graveyard" rider count tied to
    /// <see cref="RuntimeEscapeCost"/>. Underworld Breach stamps 3; future
    /// runtime-grant escape effects can pick their own count. Null when
    /// no runtime escape grant is in place.
    /// </summary>
    public int? RuntimeEscapeExileCount { get; private set; }

    /// <summary>
    /// Stamp a runtime Escape grant on this card. Used by Underworld
    /// Breach's static-while-on-battlefield grant (CR 702.143) on every
    /// nonland card in the controller's graveyard. Idempotent — later
    /// grants overwrite earlier ones; cleared by the granting effect's
    /// bookkeeping (typically when the granter leaves the battlefield).
    /// </summary>
    public void GrantRuntimeEscape(ValueObjects.ManaCost cost, int exileCount)
    {
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        if (exileCount <= 0) throw new ArgumentOutOfRangeException(nameof(exileCount));
        RuntimeEscapeCost = cost;
        RuntimeEscapeExileCount = exileCount;
    }

    /// <summary>Clear any runtime Escape grant on this card.</summary>
    public void ClearRuntimeEscape()
    {
        RuntimeEscapeCost = null;
        RuntimeEscapeExileCount = null;
    }

    /// <summary>
    /// CR 702.66 — when this card was cast paying delve, the number of cards
    /// that were exiled from its caster's graveyard as part of the delve
    /// payment. Set by <see cref="Majik.Core.Game.SpellCastFlow"/> right after
    /// <see cref="Majik.Core.Costs.DelveCost.Pay"/> succeeds; read by
    /// Murktide-Regent-style ETB effects that need to know "how many cards
    /// were exiled with me" without otherwise plumbing the delve cost
    /// through to the resolving permanent. Null when the card was not cast
    /// via delve, or has already been consumed/cleared.
    /// </summary>
    public int? PendingDelveExiledCount { get; private set; }

    /// <summary>Stamp the delve-exile count on this card. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> immediately after the
    /// delve cost is paid.</summary>
    public void SetPendingDelveExiledCount(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        PendingDelveExiledCount = count;
    }

    /// <summary>Clear the stamped delve-exile count. Called by any ETB
    /// consumer (e.g. Murktide Regent) once it has used the value.</summary>
    public void ClearPendingDelveExiledCount()
    {
        PendingDelveExiledCount = null;
    }

    /// <summary>
    /// CR 202.3b — when this card was cast as a spell with a variable {X}
    /// cost, the value chosen for X. Stamped by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> right after the caster
    /// chooses X (and before the spell hits the stack); read by ETB
    /// effects that need to know "what was X" without otherwise plumbing
    /// <c>ChosenSpellParams.X</c> through to the resolving permanent
    /// (Chalice of the Void's "enters with X charge counters", etc.).
    /// Null when the card was not cast via the X-prompt path, or has
    /// already been consumed/cleared.
    /// </summary>
    public int? PendingCastX { get; private set; }

    /// <summary>Stamp the chosen X on this card. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> right after the agent
    /// chooses X for a variable-X spell.</summary>
    public void SetPendingCastX(int x)
    {
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
        PendingCastX = x;
    }

    /// <summary>Clear the stamped X value. Called by any ETB consumer
    /// (e.g. Chalice of the Void) once it has used the value, so a later
    /// non-cast battlefield entry (blink, token copy) doesn't reuse it.</summary>
    public void ClearPendingCastX()
    {
        PendingCastX = null;
    }

    /// <summary>
    /// CR 702.44 — distinct colors of mana actually spent to pay this
    /// card's cast cost (colored pips + colored mana used to satisfy
    /// generic). Stamped by
    /// <see cref="Majik.Core.Game.TurnDriver"/> at cast time right after
    /// mana payment commits (parallels <see cref="PendingCastX"/>);
    /// read by Sunburst ETB effects ("enters with a +1/+1 counter / charge
    /// counter for each color of mana spent to cast it"). The set is
    /// authoritative for "colors spent" — it diffs the player's mana pool
    /// across the spend (see
    /// <see cref="Majik.Core.Costs.ManaPaymentResolver"/>),
    /// so generic-mana paid with colored mana counts toward Sunburst's
    /// color-count (CR 702.44b — "if one or more colored mana was spent
    /// on its costs"). Null when the card was not cast via a colored-mana
    /// payment path, or has already been consumed/cleared. Cleared post-
    /// resolve so a later non-cast battlefield entry (blink, token copy)
    /// doesn't reuse the previous cast's colors.
    /// </summary>
    public IReadOnlyList<ManaColor>? PendingCastColors { get; private set; }

    /// <summary>Stamp the distinct colors paid for this card's cast.
    /// Called by <see cref="Majik.Core.Game.TurnDriver"/> right after
    /// the mana resolver commits payment. Empty list = no colored mana
    /// was spent (CR 702.44b — Sunburst yields zero counters in that
    /// case). Null is reserved for "no cast has happened yet"; pass an
    /// empty list to explicitly record "cast but no colors paid".</summary>
    public void SetPendingCastColors(IReadOnlyList<ManaColor> colors)
    {
        PendingCastColors = colors ?? throw new ArgumentNullException(nameof(colors));
    }

    /// <summary>Clear the stamped colors-paid ledger. Called by any ETB
    /// consumer (e.g. SunburstFactory's ETB effect) once it has used the
    /// value, so a later non-cast battlefield entry doesn't reuse it.</summary>
    public void ClearPendingCastColors()
    {
        PendingCastColors = null;
    }

    /// <summary>
    /// CR 113.5 / CR 400.7 — persistent "this permanent was cast"
    /// marker. Stamped <c>true</c> by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> at the moment the
    /// spell is pushed onto the stack (any cost — printed, alternative,
    /// reduced, additional). Survives resolution so ETB-resident
    /// triggers ("when ~ enters, if you cast it, ...") and
    /// battlefield-entry replacements ("if a nontoken permanent would
    /// enter the battlefield and it wasn't cast, exile it instead" —
    /// Containment Priest, CR 614) can both read it.
    ///
    /// <para>Cleared when the permanent leaves the battlefield to any
    /// other zone (CR 400.7 — the card becomes a "new object" on each
    /// zone change). The flag is NOT cleared while transitioning Stack
    /// → Battlefield, so ETB triggers resolving immediately after the
    /// move still see <c>true</c>. Non-cast battlefield entries
    /// (reanimation, Show and Tell, Sneak Attack, Through the Breach,
    /// blink reappearance, token-copy ETB, Aether Vial put, …) leave
    /// the flag <c>false</c>.</para>
    ///
    /// <para>Mirrors <see cref="Majik.Core.Effects.ZoneMoveIntent.WasCast"/>,
    /// which is the in-flight intent-side mirror used by
    /// <see cref="ReplacementBus"/> consumers. The
    /// <see cref="Majik.Core.Services.ZoneService"/> populates that
    /// field from this property when building the intent for the
    /// Stack → Battlefield move, so any consumer that prefers the
    /// intent record (ContainmentPriest's replacement) and any
    /// consumer that prefers the live card field (TheOneRing's ETB
    /// gate) both see the same truth.</para>
    ///
    /// <para>Defaults to <c>false</c> so hand-built test cards without
    /// an explicit stamp are treated as non-cast.</para>
    /// </summary>
    public bool WasCast { get; private set; }

    /// <summary>Stamp the cast marker. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> immediately before
    /// the resolving spell is pushed onto the stack (CR 601.2i).</summary>
    public void SetWasCast(bool value)
    {
        WasCast = value;
    }

    /// <summary>Clear the cast marker. Called by
    /// <see cref="Majik.Core.Services.ZoneService"/> when a permanent
    /// leaves the battlefield to any other zone (CR 400.7 — new
    /// object on each zone change). Idempotent.</summary>
    public void ClearWasCast()
    {
        WasCast = false;
    }

    /// <summary>
    /// CR 702.138b — "escaped" runtime sentinel propagated from the
    /// resolving <see cref="Majik.Core.Spells.Spell.WasCastForEscape"/>
    /// onto the card itself, so battlefield-resident triggers
    /// (Uro's "sacrifice it unless it escaped" trigger; future
    /// <em>escapes with [counters]</em> riders per CR 702.138c) can
    /// read the flag from the source card without plumbing the spell
    /// reference across the spell → permanent boundary. Stamped at
    /// cast time by <see cref="Majik.Core.Game.SpellCastFlow"/>; the
    /// flag persists across the card's current battlefield stint and
    /// is cleared when the card leaves the battlefield (any
    /// destination), mirroring CR 400.7's "new object" rule so a
    /// re-cast / blink / token copy doesn't inherit the prior
    /// escape posture.
    /// </summary>
    public bool WasCastForEscape { get; private set; }

    /// <summary>Stamp the escape sentinel. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the cast used
    /// an <see cref="Majik.Core.Costs.EscapeAlternativeCost"/>.</summary>
    public void SetWasCastForEscape(bool value)
    {
        WasCastForEscape = value;
    }

    /// <summary>
    /// CR 702.62d / 702.62g — "cast via suspend" runtime sentinel.
    /// Stamped <c>true</c> by <see cref="Majik.Core.Game.SpellCastFlow"/>
    /// when the cast used a <see cref="Majik.Core.Costs.CastFromExileAlternativeCost"/>
    /// with its <see cref="Majik.Core.Costs.CastFromExileAlternativeCost.IsSuspendCast"/>
    /// flag set (i.e. the "cast for free" payoff fired by
    /// <see cref="Majik.Core.Costs.SuspendedCardRegistry"/> when the last
    /// time counter is removed). Read by:
    ///   - The creature-haste rider in <see cref="Majik.Core.Game.SpellCastFlow"/>,
    ///     which registers a <see cref="Majik.Core.Effects.SuspendHasteEffect"/>
    ///     on the resolving permanent for as long as it stays on the
    ///     battlefield (CR 702.62g — "gains haste until you lose control of
    ///     the spell or the permanent it becomes").
    ///   - Future "if [card] was cast via suspend" triggers / replacements.
    ///
    /// <para>Mirrors <see cref="Majik.Core.Spells.Spell.WasCastFromSuspend"/>
    /// on the resolving stack object for resolve-body reads that don't
    /// have the spell reference handy.</para>
    ///
    /// <para>Defaults to <c>false</c>. The flag is NOT cleared on zone
    /// change — the haste-grant continuous effect carries its own
    /// LTB-revoke lifecycle via <see cref="Majik.Core.Effects.SuspendHasteEffect.IsActive"/>,
    /// and the underlying card object is replaced on the next cast (CR
    /// 400.7 — new object on each zone change) so the stale stamp can't
    /// survive a real re-cast.</para>
    /// </summary>
    public bool WasCastFromSuspend { get; private set; }

    /// <summary>Stamp the cast-from-suspend sentinel. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the cast used a
    /// <see cref="Majik.Core.Costs.CastFromExileAlternativeCost"/> with
    /// <see cref="Majik.Core.Costs.CastFromExileAlternativeCost.IsSuspendCast"/>
    /// set.</summary>
    public void SetWasCastFromSuspend(bool value)
    {
        WasCastFromSuspend = value;
    }

    /// <summary>
    /// CR 702.33b — "kicked" runtime sentinel. Stamped <c>true</c> by
    /// <see cref="Majik.Core.Costs.KickerAdditionalCost.Pay"/> at cast
    /// announcement when the caster pays the optional kicker mana
    /// (CR 601.2b — kicker decision is locked in when the spell is
    /// cast). Read by the resolving spell's <c>EffectFactory</c> to
    /// branch the printed body on the "if [spell] was kicked" rider
    /// (Burst Lightning's deals-4-instead-of-2 toggle is the canonical
    /// consumer).
    ///
    /// <para>Cleared by <see cref="Majik.Core.Game.SpellCastFlow"/>
    /// once the spell resolves so a later re-cast / blink / token
    /// copy / on-battlefield permanent doesn't inherit the prior
    /// kicker posture (CR 400.7 — new object on each zone change).
    /// Mirrors the <see cref="Majik.Core.Spells.Spell.WasKicked"/>
    /// stamping on the resolving stack object.</para>
    ///
    /// <para>Defaults to <c>false</c> so hand-built test cards
    /// without an explicit stamp are treated as non-kicked casts.</para>
    /// </summary>
    public bool WasKicked { get; private set; }

    /// <summary>Stamp the kicker sentinel. Called by
    /// <see cref="Majik.Core.Costs.KickerAdditionalCost.Pay"/> after
    /// the kicker mana is successfully paid.</summary>
    public void SetWasKicked(bool value)
    {
        WasKicked = value;
    }

    /// <summary>Clear the kicker sentinel. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> via a cleanup
    /// effect appended after the spell's printed body so the flag
    /// doesn't leak past resolution (CR 400.7).</summary>
    public void ClearWasKicked()
    {
        WasKicked = false;
    }

    /// <summary>
    /// CR 601.2f — when this card is currently being cast as a spell, the
    /// set of targets the agent picked during target selection (one inner
    /// list per <see cref="Majik.Core.Game.TargetRequest"/>, mirroring
    /// <see cref="Majik.Core.Game.ChosenSpellParams.Targets"/>). Stamped by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> immediately after target
    /// collection so cost-calculation rules that depend on the chosen
    /// targets ("This spell costs {2} less to cast if it targets a blue
    /// spell." — Mystical Dispute) can read them. Null when the card is
    /// not actively being cast.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<object>>? PendingCastTargets { get; private set; }

    /// <summary>Stamp the chosen targets on this card. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> right after target
    /// collection and before cost calculation, so a
    /// <see cref="Majik.Core.Costs.CostReductionAbility"/> on the card can
    /// reference the targets that have just been picked (e.g. Mystical
    /// Dispute's "costs {2} less if it targets a blue spell").</summary>
    public void SetPendingCastTargets(IReadOnlyList<IReadOnlyList<object>> targets)
    {
        PendingCastTargets = targets ?? throw new ArgumentNullException(nameof(targets));
    }

    /// <summary>Clear the stamped pending targets. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> once the spell has been
    /// pushed onto the stack, so a later re-cast doesn't see stale
    /// targets.</summary>
    public void ClearPendingCastTargets()
    {
        PendingCastTargets = null;
    }

    /// <summary>
    /// CR 701.59 — "Gift" cast-time sentinel (Bloomburrow). Stamped
    /// <c>true</c> by <see cref="Majik.Core.Game.SpellCastFlow"/> when
    /// the caster opts into a gift promise during the cast flow.
    /// Resolve bodies of <see cref="Majik.Core.Spells.IGiftClause"/>
    /// implementors (e.g.
    /// <see cref="Majik.Core.CardData.Factories.IntoTheFloodMawFactory"/>)
    /// branch on this flag to apply the upgraded printed effect (Flood
    /// Maw's "instead return target nonland permanent"). Mirrors
    /// <see cref="Majik.Core.Spells.Spell.GiftRecipient"/> on the
    /// resolving spell for resolve-body reads that don't have the
    /// spell reference handy.
    ///
    /// <para>Cleared by <see cref="Majik.Core.Game.SpellCastFlow"/> via
    /// a cleanup effect appended after the spell's printed body so the
    /// flag doesn't leak past resolution (CR 400.7 — new object on
    /// each zone change). Defaults to <c>false</c> so hand-built test
    /// cards without an explicit stamp are treated as non-gifted casts.</para>
    /// </summary>
    public bool HasGiftPromised { get; private set; }

    /// <summary>Stamp the gift-promise sentinel. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the caster
    /// opts into a cast-time gift promise.</summary>
    public void SetHasGiftPromised(bool value)
    {
        HasGiftPromised = value;
    }

    /// <summary>Clear the gift-promise sentinel. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> via a cleanup
    /// effect appended after the spell's printed body so the flag
    /// doesn't leak past resolution (CR 400.7).</summary>
    public void ClearHasGiftPromised()
    {
        HasGiftPromised = false;
    }

    /// <summary>
    /// CR 711 — double-faced / transform card face tracker. Non-null on
    /// DFC cards; tracks which face (front / back) is currently active and
    /// exposes <see cref="Majik.Core.CardData.MDFCs.MdfcState.Transform"/>
    /// to flip between them. Null on single-faced cards.
    ///
    /// v1 is a thin attachment — flipping the state does not yet swap the
    /// runtime characteristics on the underlying Card object (full Layer 0
    /// per-face characteristic-replacement is deferred). It is the
    /// canonical observation surface for "is this card transformed?".
    /// </summary>
    public Majik.Core.CardData.MDFCs.MdfcState? MdfcState { get; set; }

    /// <summary>
    /// CR 715 — Adventure half descriptor. Non-null on adventurer cards
    /// (Bonecrusher Giant, Murderous Rider, Embereth Shieldbreaker, …);
    /// null on single-face cards. Attached by the card's factory at build
    /// time and read by <see cref="Majik.Core.Costs.AdventureAlternativeCost"/>
    /// when the caster picks the Adventure cast path.
    ///
    /// While exiled by a resolved Adventure (CR 715.3d), the printed-side
    /// "may cast from exile" permission is stamped on the card via the
    /// existing <see cref="GrantRuntimeExileCast"/> surface — same probe
    /// surface as Ragavan / Cascade — so no Adventure-specific cast-from-
    /// exile alt-cost is required.
    /// </summary>
    public Majik.Core.CardData.Adventures.AdventureSpec? AdventureSpec { get; set; }

    /// <summary>
    /// CR 105 / CR 111.4 / CR 903.4 — explicit colour identity for tokens
    /// (and any future card whose colour cannot be derived from its mana
    /// cost). Tokens have no printed mana cost so their colour is set by
    /// the effect that created them: "create a 2/2 green Cat creature
    /// token" stamps <c>{Green}</c> here. Null on normal cards (colour
    /// derives from <see cref="ManaCost"/> via
    /// <see cref="Majik.Core.Cards.CardColors.GetColors"/>); empty list is
    /// explicit "colourless" (CR 105.2c — Wurmcoil's Phyrexian Wurm
    /// tokens, Karn's Construct tokens). <see cref="CardColors.GetColors"/>
    /// honours this override when non-null; otherwise it falls back to the
    /// mana-cost pip scan.
    /// </summary>
    public IReadOnlyList<ValueObjects.ManaColor>? TokenColorsOverride { get; private set; }

    /// <summary>
    /// Stamp an explicit colour set on this card. Used by
    /// <see cref="Majik.Core.Tokens.TokenFactory.CreateOnBattlefield"/> to
    /// honour the printed colour of token-creation effects (Esika's Chariot's
    /// "green" Cats, Ocelot Pride's "white" Cats, Pact of the Titan's "red"
    /// Giant, Wurmcoil's "colourless" Wurms — empty list). Idempotent;
    /// later calls overwrite earlier ones. Should generally only be set
    /// once at token construction.
    /// </summary>
    public void SetTokenColors(IReadOnlyList<ValueObjects.ManaColor> colors)
    {
        if (colors == null) throw new ArgumentNullException(nameof(colors));
        TokenColorsOverride = colors;
    }

    public Card(string name, string manaCost = "", IEnumerable<CardType>? cardTypes = null, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Card name cannot be null or empty", nameof(name));
        }

        Name = name;
        ManaCost = manaCost;
        ManaCostValue = ValueObjects.ManaCost.Parse(manaCost);
        _zone = ZoneType.Library;
        _controller = null;

        if (cardTypes != null)
        {
            _cardTypes.AddRange(cardTypes);
        }

        if (supertypes != null)
        {
            _supertypes.AddRange(supertypes);
        }

        if (subtypes != null)
        {
            _subtypes.AddRange(subtypes);
        }
    }

    /// <summary>
    /// Abilities attached to this card (activated, triggered, static, mana).
    /// </summary>
    public IReadOnlyList<IAbility> Abilities => _abilities.AsReadOnly();

    /// <summary>
    /// Attach an ability to this card. Used during card construction or by effects
    /// that grant abilities.
    /// </summary>
    public void AddAbility(IAbility ability)
    {
        if (ability == null)
        {
            throw new ArgumentNullException(nameof(ability));
        }

        _abilities.Add(ability);
    }

    /// <summary>
    /// Remove a previously-added ability from this card. Returns true if the
    /// ability was attached and is now removed. Used by aura / equipment
    /// lifecycle binders that grant an ability on attach and revoke it when
    /// the aura leaves the battlefield or is detached (CR 303.4 / 613.1f).
    /// </summary>
    public bool RemoveAbility(IAbility ability)
    {
        if (ability == null)
        {
            throw new ArgumentNullException(nameof(ability));
        }

        return _abilities.Remove(ability);
    }

    /// <summary>
    /// Add an extra card type after construction. Used by named-card factories
    /// that build multi-type permanents (e.g. Artifact Creature) where the
    /// primary concrete subclass only registers its own type.
    /// </summary>
    internal void AddCardType(CardType type)
    {
        if (!_cardTypes.Contains(type))
            _cardTypes.Add(type);
    }

    public bool HasType(CardType type)
    {
        return _cardTypes.Contains(type);
    }

    public bool HasSupertype(CardSupertype supertype)
    {
        return _supertypes.Contains(supertype);
    }

    public bool HasSubtype(CardSubtype subtype)
    {
        return _subtypes.Contains(subtype);
    }

    public override string ToString()
    {
        return Name;
    }
}
