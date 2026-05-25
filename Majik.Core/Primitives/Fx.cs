using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.Primitives;

/// <summary>
/// Shared effects-primitive facade. One canonical entry point for the
/// verbs that show up in instant / sorcery / ability resolve bodies —
/// re-exporting the existing helpers (<see cref="OracleSpellBinder"/>,
/// <see cref="MillAction"/>, <see cref="ScryAction"/>,
/// <see cref="SurveilAction"/>, <see cref="TokenFactory"/>,
/// <see cref="ZoneService"/>, <see cref="Player"/> verbs,
/// <see cref="CounterCollection"/>) plus a small number of new
/// primitives (<see cref="Cards.DrawCards"/>, <see cref="Hand.DiscardN"/>,
/// <see cref="Library.LookAtTopN"/>) that didn't have a single canonical
/// home.
///
/// The audit (<c>docs/EFFECTS_AUDIT.md</c>) shows the verb fan-out across
/// the 288 named factories is already mostly absorbed by existing
/// helpers — the leverage here is consolidating the import surface so a
/// new factory can write <c>Fx.DealDamage(target, 3)</c> instead of
/// hunting through six namespaces.
///
/// Class named <see cref="Fx"/> (not <c>Effects</c>) deliberately: the
/// <c>Majik.Core.Effects</c> namespace already exists as a sibling of
/// <c>Majik.Core.CardData</c>, so an unqualified <c>Effects</c>
/// identifier inside a factory file resolves to that namespace first
/// (C# name lookup walks enclosing namespaces before <c>using</c>
/// directives) and shadows the class. <see cref="Fx"/> sidesteps the
/// collision entirely.
///
/// All entry points are static and free of hidden state. Effects that
/// need event-bus / zone-service / replacement-bus plumbing accept those
/// as optional parameters — when omitted the operation falls back to the
/// raw-zone path the existing helpers already use.
/// </summary>
public static class Fx
{
    // ------------------------------------------------------------------
    // Damage (CR 119 / 120) — re-exports OracleSpellBinder.DealDamage
    // and SearingBlazeFactory.DealDamageWithPlaneswalker so callers no
    // longer have to know about the Searing Blaze hop for the
    // planeswalker case.
    // ------------------------------------------------------------------

    /// <summary>
    /// CR 119 — deal <paramref name="amount"/> damage to <paramref name="target"/>.
    /// Routes Player → <see cref="Player.LoseLife"/>, Creature →
    /// <see cref="Creature.TakeDamage"/>. Planeswalker targets are NOT
    /// routed — use <see cref="DealDamageAny"/> for any-target resolution.
    /// No-op when <paramref name="amount"/> ≤ 0 or <paramref name="target"/>
    /// doesn't match a damage-receiving shape.
    /// </summary>
    public static void DealDamage(object target, int amount)
    {
        if (amount <= 0) return;
        OracleSpellBinder.DealDamage(target, amount);
    }

    /// <summary>
    /// CR 119 + CR 306.7 — deal <paramref name="amount"/> damage to
    /// <paramref name="target"/>, routing Planeswalker targets to loyalty
    /// removal. Matches the "any target" resolution shape (Player /
    /// Creature / Planeswalker) used by Lightning Bolt, Lightning Helix,
    /// Searing Blaze, Tribal Flames, etc.
    /// </summary>
    public static void DealDamageAny(object target, int amount)
    {
        if (amount <= 0) return;
        switch (target)
        {
            case Planeswalker pw: pw.RemoveLoyalty(amount); break;
            default: OracleSpellBinder.DealDamage(target, amount); break;
        }
    }

    // ------------------------------------------------------------------
    // Life (CR 119.3 / 119.4) — passthroughs to Player.
    // ------------------------------------------------------------------

    /// <summary>CR 119.3 — <paramref name="player"/> gains
    /// <paramref name="amount"/> life. Negative amounts no-op (matching
    /// <see cref="Player.GainLife"/>'s contract).</summary>
    public static void GainLife(Player player, int amount)
    {
        if (player is null) throw new ArgumentNullException(nameof(player));
        if (amount <= 0) return;
        player.GainLife(amount);
    }

    /// <summary>CR 119.3 — <paramref name="player"/> loses
    /// <paramref name="amount"/> life. Negative amounts no-op.</summary>
    public static void LoseLife(Player player, int amount)
    {
        if (player is null) throw new ArgumentNullException(nameof(player));
        if (amount <= 0) return;
        player.LoseLife(amount);
    }

    // ------------------------------------------------------------------
    // Card draw / hand (CR 120 / CR 701.16).
    // ------------------------------------------------------------------

    /// <summary>
    /// CR 120 — <paramref name="player"/> draws <paramref name="count"/>
    /// cards (top of library → hand, one at a time). Empty-library
    /// stops the loop and stamps the loss condition via
    /// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> (CR 120.3 /
    /// 704.5b). Returns the cards actually drawn in draw order.
    /// Returns empty for <paramref name="count"/> ≤ 0 (no-op).
    ///
    /// <para>
    /// CR 614 — when <paramref name="player"/> has a
    /// <see cref="Majik.Core.Effects.ReplacementBus"/> attached, each
    /// individual draw is routed through it via a
    /// <see cref="Majik.Core.Effects.DrawCardIntent"/>. Replacement
    /// effects (Dredge — CR 702.52, future "instead reveal" replacements)
    /// can cancel the draw outright by returning null; cancelled draws
    /// are NOT included in the returned list. The remaining draws in the
    /// loop continue to fire — a Dredge return that consumes draw #1
    /// does not short-circuit a "draw 3" effect.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ICard> DrawCards(Player player, int count)
    {
        if (player is null) throw new ArgumentNullException(nameof(player));
        if (count <= 0) return Array.Empty<ICard>();

        var drawn = new List<ICard>(count);
        for (var i = 0; i < count; i++)
        {
            // CR 614 — route the would-draw through the replacement bus
            // when one is attached. A null result means a replacement
            // (e.g. Dredge) consumed the draw; skip the library→hand
            // move for THIS draw only — the loop continues for the next.
            if (player.Replacements != null)
            {
                var intent = new Majik.Core.Effects.DrawCardIntent(player);
                var replaced = player.Replacements.Apply(intent);
                if (replaced is null)
                {
                    continue;
                }
            }

            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top is null)
            {
                player.MarkTriedToDrawFromEmptyLibrary();
                break;
            }
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
            drawn.Add(top);
        }
        return drawn;
    }

    /// <summary>
    /// CR 701.16 — <paramref name="player"/> discards up to
    /// <paramref name="count"/> cards from hand. v1 deterministic
    /// first-card-in-hand pick (agent-driven choice is deferred — same
    /// gap as Faithless Looting / Liliana of the Veil). Empty-hand halts
    /// the loop cleanly. Returns the cards actually discarded in discard
    /// order.
    /// </summary>
    public static IReadOnlyList<ICard> Discard(Player player, int count)
    {
        if (player is null) throw new ArgumentNullException(nameof(player));
        if (count <= 0) return Array.Empty<ICard>();

        var discarded = new List<ICard>(count);
        for (var i = 0; i < count; i++)
        {
            var pick = player.Zones.Hand.GetCards().FirstOrDefault();
            if (pick is null) break;
            player.Zones.Hand.RemoveCard(pick);
            player.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);
            discarded.Add(pick);
        }
        return discarded;
    }

    // ------------------------------------------------------------------
    // Library — re-exports MillAction / ScryAction / SurveilAction, plus
    // a small LookAtTopN peek.
    // ------------------------------------------------------------------

    /// <summary>CR 701.13 — see <see cref="MillAction.Apply"/>. Returns
    /// the cards milled in milled order.</summary>
    public static IReadOnlyList<ICard> Mill(Player player, int count)
        => MillAction.Apply(player, count);

    /// <summary>CR 701.20 / 702.130 — read-only peek of the top
    /// <paramref name="n"/> cards of <paramref name="player"/>'s library
    /// (Scry / Surveil / Brainstorm-style "look at" prompts). Identical
    /// to <see cref="ScryAction.Peek"/> / <see cref="SurveilAction.Peek"/>
    /// — provided here so factories don't have to pick between the two
    /// keyword namespaces for a plain look.</summary>
    public static IReadOnlyList<ICard> LookAtTopN(Player player, int n)
    {
        if (player is null) throw new ArgumentNullException(nameof(player));
        if (n <= 0) return Array.Empty<ICard>();
        return player.Zones.Library.GetCards().Take(n).ToList();
    }

    /// <summary>CR 701.20a — see <see cref="ScryAction.Apply"/>. The
    /// agent's <see cref="ScryAction.ScryDecision"/> partitions the
    /// peeked top N into bottom-bound + top-ordered cards; engine
    /// reorders the library accordingly.</summary>
    public static void Scry(Player player, int n, ScryAction.ScryDecision decision)
        => ScryAction.Apply(player, n, decision);

    /// <summary>
    /// CR 701.42 — surveil <paramref name="n"/> for <paramref name="player"/>
    /// using the pre-resolved <paramref name="decision"/>. Captures the
    /// peeked cards before the surveil applies, runs
    /// <see cref="SurveilAction.Apply"/>, then publishes a
    /// <see cref="SurveilEvent"/> against the player's registered bus
    /// (looked up via <see cref="EventBusRegistry.Get(Player?)"/>) so
    /// "Whenever you surveil" / "Whenever ~ surveils" triggers fire.
    /// Bus is best-effort — no publish if none is registered.
    /// </summary>
    public static void Surveil(Player player, int n, SurveilAction.SurveilDecision decision)
        => Surveil(player, n, decision, eventBus: null);

    /// <summary>
    /// CR 701.42 — surveil <paramref name="n"/> with an explicit
    /// <paramref name="eventBus"/>. When supplied, the bus receives the
    /// <see cref="SurveilEvent"/> directly; otherwise the player's
    /// registered bus (via <see cref="EventBusRegistry"/>) is used.
    /// Pass-through overload for factories that already own a bus
    /// reference and don't want to depend on the registry being primed.
    /// </summary>
    public static void Surveil(Player player, int n, SurveilAction.SurveilDecision decision, IEventBus? eventBus)
    {
        if (player is null) throw new ArgumentNullException(nameof(player));

        // Snapshot the peeked cards BEFORE Apply mutates the library
        // ordering, so the SurveilEvent carries the cards the player
        // actually saw (matches "look at the top N cards" wording).
        var peeked = SurveilAction.Peek(player, n);
        SurveilAction.Apply(player, n, decision);

        var bus = eventBus ?? EventBusRegistry.Get(player);
        bus?.Publish(new SurveilEvent(player, n, peeked));
    }

    // ------------------------------------------------------------------
    // Zone moves (CR 400.7 / 701.20) — aliases for the helpers that
    // factories actually reach for today.
    // ------------------------------------------------------------------

    /// <summary>CR 701.7 — move <paramref name="card"/> from the
    /// battlefield to its owner's graveyard, treated as a "destroy"
    /// effect (Indestructible / regeneration gates apply). Aliases
    /// <see cref="OracleSpellBinder.MoveToGraveyard(ICard)"/>.</summary>
    public static void MoveToGraveyard(ICard card)
        => OracleSpellBinder.MoveToGraveyard(card);

    /// <summary>
    /// Move <paramref name="card"/> from the battlefield to its owner's
    /// graveyard with an explicit <paramref name="reason"/>. Routes
    /// through the binder's reason-gated path so destroy effects honour
    /// Indestructible (CR 702.12) and regeneration (CR 701.15), while
    /// sacrifice / SBA / mill paths bypass.
    /// </summary>
    public static void MoveToGraveyard(ICard card, ZoneMoveReason reason)
        => OracleSpellBinder.MoveToGraveyard(card, reason);

    /// <summary>CR 701.20 — move <paramref name="card"/> from its current
    /// zone to its owner's exile zone. Aliases the binder's existing
    /// helper.</summary>
    public static void MoveToExile(ICard card)
        => OracleSpellBinder.MoveToExile(card);

    /// <summary>
    /// CR 701.20 — bounce <paramref name="permanent"/> from the
    /// battlefield to its owner's hand. Routes through
    /// <see cref="ZoneService.MoveCard"/> when <paramref name="zones"/>
    /// is supplied so LTB / ETB events fire and replacement effects can
    /// rewrite the move; otherwise uses raw-zone fallback.
    /// </summary>
    public static void BounceToHand(ICard permanent, ZoneService? zones = null)
    {
        if (permanent is null) throw new ArgumentNullException(nameof(permanent));
        var owner = permanent.Owner;
        if (owner is null) return;

        if (zones is not null)
        {
            zones.MoveCard(permanent, permanent.Zone, ZoneType.Hand, owner);
            return;
        }

        if (permanent.Zone == ZoneType.Battlefield) owner.Zones.Battlefield.RemoveCard(permanent);
        else if (permanent.Zone == ZoneType.Graveyard) owner.Zones.Graveyard.RemoveCard(permanent);
        else if (permanent.Zone == ZoneType.Exile) owner.Zones.Exile.RemoveCard(permanent);
        owner.Zones.Hand.AddCard(permanent);
        permanent.SetZone(ZoneType.Hand);
    }

    /// <summary>
    /// CR 701.20 — put <paramref name="permanent"/> on top of its
    /// owner's library. Like <see cref="BounceToHand"/> this prefers
    /// <see cref="ZoneService.MoveCard"/> when supplied.
    /// </summary>
    public static void BounceToTopOfLibrary(ICard permanent, ZoneService? zones = null)
    {
        if (permanent is null) throw new ArgumentNullException(nameof(permanent));
        var owner = permanent.Owner;
        if (owner is null) return;

        if (zones is not null)
        {
            zones.MoveCard(permanent, permanent.Zone, ZoneType.Library, owner);
            // ZoneService appends; for "on top" the caller must rotate.
            // Default ZoneManager.AddCard already puts at the top of the
            // library (Library.AddCard adds to position 0) — leave as-is.
            return;
        }

        if (permanent.Zone == ZoneType.Battlefield) owner.Zones.Battlefield.RemoveCard(permanent);
        else if (permanent.Zone == ZoneType.Graveyard) owner.Zones.Graveyard.RemoveCard(permanent);
        else if (permanent.Zone == ZoneType.Hand) owner.Zones.Hand.RemoveCard(permanent);
        else if (permanent.Zone == ZoneType.Exile) owner.Zones.Exile.RemoveCard(permanent);
        owner.Zones.Library.AddCard(permanent);
        permanent.SetZone(ZoneType.Library);
    }

    /// <summary>
    /// CR 701.20 — move a card from a graveyard to its owner's
    /// battlefield (reanimate). Routes through ZoneService when
    /// supplied so ETB triggers fire (CR 603.6a); otherwise applies the
    /// raw-zone fallback Reanimate / Animate Dead use today.
    /// </summary>
    public static void ReturnFromGraveyardToBattlefield(
        ICard card,
        Player newController,
        ZoneService? zones = null)
    {
        if (card is null) throw new ArgumentNullException(nameof(card));
        if (newController is null) throw new ArgumentNullException(nameof(newController));

        if (zones is not null)
        {
            zones.MoveCard(card, ZoneType.Graveyard, ZoneType.Battlefield, newController);
            return;
        }

        var owner = card.Owner ?? newController;
        owner.Zones.Graveyard.RemoveCard(card);
        newController.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        if (card is Permanent perm) perm.SetController(newController);
    }

    /// <summary>
    /// CR 701.20 — return a card from <paramref name="owner"/>'s
    /// graveyard to that player's hand. ZoneService-routed when
    /// supplied; raw-zone fallback otherwise.
    /// </summary>
    public static void ReturnFromGraveyardToHand(
        ICard card,
        ZoneService? zones = null)
    {
        if (card is null) throw new ArgumentNullException(nameof(card));
        var owner = card.Owner;
        if (owner is null) return;

        if (zones is not null)
        {
            zones.MoveCard(card, ZoneType.Graveyard, ZoneType.Hand, owner);
            return;
        }

        owner.Zones.Graveyard.RemoveCard(card);
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
    }

    /// <summary>
    /// CR 701.16 — sacrifice <paramref name="permanent"/>. Owner-routed
    /// move from battlefield to graveyard. Routes through
    /// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
    /// with <see cref="ZoneMoveReason.Sacrifice"/> so the binder skips
    /// the Indestructible (CR 702.12b) / regeneration (CR 701.15c)
    /// gates — sacrifice is not a "destroy" effect.
    /// </summary>
    public static void Sacrifice(ICard permanent)
        => OracleSpellBinder.MoveToGraveyard(permanent, ZoneMoveReason.Sacrifice);

    // ------------------------------------------------------------------
    // Stack (CR 701.5) — counter target spell/ability.
    // ------------------------------------------------------------------

    /// <summary>
    /// CR 701.5 — remove <paramref name="spell"/> from
    /// <paramref name="stack"/> and place the card in its owner's
    /// graveyard. Aliases <see cref="OracleSpellBinder.RemoveFromStack"/>
    /// + the canonical "card to graveyard" tail. Caller is responsible
    /// for the targeting / illegal-target check (CR 608.2b).
    /// </summary>
    public static void Counter(Majik.Core.Stack.Stack stack, Majik.Core.Spells.ISpell spell)
    {
        if (stack is null) throw new ArgumentNullException(nameof(stack));
        if (spell is null) throw new ArgumentNullException(nameof(spell));

        // CR 701.5b — uncounterable spells stay on the stack. RemoveFromStack
        // signals the veto by returning false; skip the graveyard tail so the
        // spell's card tracking zone stays aligned with the live stack
        // (the spell will resolve normally).
        if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
        spell.Card.SetZone(ZoneType.Graveyard);
    }

    // ------------------------------------------------------------------
    // Counters (CR 122) — passthrough to CounterCollection.Add/Remove.
    // ------------------------------------------------------------------

    /// <summary>CR 122 — place <paramref name="amount"/> counters of
    /// <paramref name="type"/> on <paramref name="permanent"/>.
    /// Negative amounts no-op (use <see cref="RemoveCounter"/> instead).
    /// </summary>
    public static void PlaceCounter(Permanent permanent, CounterType type, int amount = 1)
    {
        if (permanent is null) throw new ArgumentNullException(nameof(permanent));
        if (amount <= 0) return;
        permanent.Counters.Add(type, amount);
    }

    /// <summary>CR 122 — remove <paramref name="amount"/> counters of
    /// <paramref name="type"/> from <paramref name="permanent"/>.
    /// Negative amounts no-op.</summary>
    public static void RemoveCounter(Permanent permanent, CounterType type, int amount = 1)
    {
        if (permanent is null) throw new ArgumentNullException(nameof(permanent));
        if (amount <= 0) return;
        permanent.Counters.Remove(type, amount);
    }

    // ------------------------------------------------------------------
    // Tap / Untap (CR 701.21 / 701.27).
    // ------------------------------------------------------------------

    /// <summary>CR 701.21 — tap <paramref name="permanent"/>. No-op if
    /// already tapped.</summary>
    public static void Tap(Permanent permanent)
    {
        if (permanent is null) throw new ArgumentNullException(nameof(permanent));
        permanent.Tap();
    }

    /// <summary>CR 701.27 — untap <paramref name="permanent"/>. No-op if
    /// already untapped.</summary>
    public static void Untap(Permanent permanent)
    {
        if (permanent is null) throw new ArgumentNullException(nameof(permanent));
        permanent.Untap();
    }

    // ------------------------------------------------------------------
    // Tokens (CR 111) — passthroughs to TokenFactory.
    // ------------------------------------------------------------------

    /// <summary>CR 111 — create a Treasure token under
    /// <paramref name="controller"/>. Aliases
    /// <see cref="TokenFactory.CreateTreasure"/>.</summary>
    public static Artifact CreateTreasure(Player controller, ZoneService? zones = null)
        => TokenFactory.CreateTreasure(controller, zones);

    /// <summary>CR 111 — create a Clue token. Aliases
    /// <see cref="TokenFactory.CreateClue"/>. Modelled as Investigate
    /// (CR 701.18) for callers that just need a one-shot Investigate.</summary>
    public static Artifact Investigate(Player controller, ZoneService? zones = null)
        => TokenFactory.CreateClue(controller, zones);

    /// <summary>CR 111 — create a Food token. Aliases
    /// <see cref="TokenFactory.CreateFood"/>.</summary>
    public static Artifact CreateFood(Player controller, ZoneService? zones = null)
        => TokenFactory.CreateFood(controller, zones);

    /// <summary>CR 111 — create a Blood token. Aliases
    /// <see cref="TokenFactory.CreateBlood"/> (Crimson Vow looting
    /// artifact: "{1}, {T}, Discard a card, Sacrifice this artifact:
    /// Draw a card.").</summary>
    public static Artifact CreateBlood(Player controller, ZoneService? zones = null)
        => TokenFactory.CreateBlood(controller, zones);

    /// <summary>CR 111 — create an Eldrazi Spawn token. Aliases
    /// <see cref="TokenFactory.CreateEldraziSpawn"/>.</summary>
    public static Creature CreateEldraziSpawn(Player controller, ZoneService? zones = null)
        => TokenFactory.CreateEldraziSpawn(controller, zones);

    // ------------------------------------------------------------------
    // Effect wrappers — convenience helpers so factories can build a
    // resolve body as `new Effect("...", () => Effects.DealDamageAny(t, 3))`
    // without having to import the Abilities namespace just for `Effect`.
    // ------------------------------------------------------------------

    /// <summary>
    /// Build a one-shot <see cref="IEffect"/> wrapping
    /// <paramref name="body"/>. Convenience for resolve-time inline
    /// effects — factories used to import <c>Majik.Core.Abilities</c>
    /// just to get the <see cref="Effect"/> ctor; this lets them stay in
    /// <c>Majik.Core.Primitives</c>.
    /// </summary>
    public static IEffect Inline(string description, Action body)
        => new Effect(description, body);
}
