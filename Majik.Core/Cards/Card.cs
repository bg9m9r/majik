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
