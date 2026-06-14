using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pernicious Deed (Apocalypse, reprinted in
/// Time Spiral / Commander products). Enchantment — {1}{B}{G}.
/// Oracle text:
///
///   "{X}, Sacrifice Pernicious Deed: Destroy each artifact, creature,
///    and enchantment with mana value X or less."
///
/// ## Implemented (v1)
/// - Enchantment {1}{B}{G} with owner/controller wired.
/// - <b>Activated ability (CR 602.1)</b>: {X}, Sacrifice this:
///   destroy each artifact, creature, and enchantment with mana value
///   X or less across every player's battlefield. Mirrors Engineered
///   Explosives' shape:
///   <list type="bullet">
///     <item>Mana cost: <see cref="ManaCostCost"/>{X} declared on the
///       activation; the actual paid X is sampled at resolution via the
///       caller-supplied <c>xValueProvider</c> (engine has no per-
///       activation X ledger yet — same v1 approximation as Engineered
///       Explosives' Sunburst).</item>
///     <item>Sacrifice cost: <see cref="AdditionalCost.Sacrifice(Permanent)"/>.
///       Payment is a no-op stub at the engine level — the effect
///       closure performs the zone move so visible state matches CR
///       701.16 (same trick used by Engineered Explosives and Mishra's
///       Bauble).</item>
///     <item>Effect: iterate every battlefield (resolver-supplied;
///       falls back to controller-only when no resolver), destroy each
///       artifact, creature, or enchantment whose mana value is
///       <c>≤ X</c>. Lands are excluded by the type predicate; other
///       permanent types (Planeswalker, Battle) are likewise excluded
///       — only the three printed types are swept.</item>
///   </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: same stub status as
///   Engineered Explosives — the effect closure performs the zone
///   move so behavior is observable. Once <see cref="AdditionalCost.Pay"/>
///   actually performs the sacrifice the explicit move-to-graveyard
///   can be removed.
/// - <b>"Can't be regenerated" rider</b>: implicit at v1 since the
///   engine has no regenerate prompt.
/// - <b>X value provenance</b>: the engine has no live X-payment
///   ledger; callers wire <paramref name="xValueProvider"/> to whatever
///   signal they have. The single-arg dispatcher path returns 0
///   (everything mv-0-or-less destroyed — i.e. 0-mana artifacts /
///   creature tokens with 0 printed cost / enchantments with mv 0).
/// </summary>
[CardName("Pernicious Deed")]
public static class PerniciousDeedFactory
{
    /// <summary>
    /// Construct Pernicious Deed. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to on the production routed
    /// build; the sweep reads every player's battlefield from the LIVE
    /// resolution context (<c>ctx.Game.AllPlayers</c>) at resolution, so it is
    /// correct in real games. Activated ability resolves with X = 0 (single-arg
    /// path can't know X — see <paramref name="xValueProvider"/> deferral).
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, xValueProvider: null, eventBus: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload — Festival-Crasher pattern). Threads <c>effects.EventBus</c>
    /// into the self-sacrifice cost so paying it publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) crediting the
    /// cost-payer — the seam aristocrat payoffs read.
    /// </summary>
    public static Enchantment Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, xValueProvider: null, eventBus: effects?.EventBus);

    /// <summary>
    /// Construct Pernicious Deed. When <paramref name="xValueProvider"/>
    /// is supplied, the activated ability uses that as the mv ceiling
    /// at resolution time. The sweep scans every player's battlefield read
    /// from the live resolution context (<c>ctx.Game.AllPlayers</c>) at
    /// resolution — no captured player resolver, so it is correct on both the
    /// shape build and the routed prod build (mirrors #2551 / Engineered
    /// Explosives). With no live game context the sweep falls back to the
    /// controller's battlefield (shape-only paths).
    /// </summary>
    public static Enchantment Create(
        Player owner,
        Func<int>? xValueProvider) =>
        Create(owner, xValueProvider, eventBus: null);

    /// <summary>
    /// Canonical builder. <paramref name="eventBus"/> (when non-null) is
    /// threaded into the self-sacrifice <see cref="AdditionalCost"/> + the
    /// resolve-path sweep closure so the sacrifice publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a). Null preserves the
    /// legacy publish-nothing posture.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        Func<int>? xValueProvider,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment("Pernicious Deed", "{1}{B}{G}");
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Activated ability (CR 602.1): {X}, Sacrifice this: destroy
        // each artifact, creature, and enchantment with mv ≤ X.
        // - Mana cost: {X} → ManaCostCost. Engine has no live X-payment
        //   ledger; X at resolution comes from xValueProvider (defaults
        //   to 0 in the shape-only path).
        // - Sacrifice cost: AdditionalCost.Sacrifice(card). Payment is a
        //   no-op stub at the engine level (see Engineered Explosives /
        //   Mishra's Bauble); the effect closure performs the zone move
        //   so visible state matches CR 701.16.
        // - Effect: iterate every battlefield (read from the live resolution
        //   context — ctx.Game.AllPlayers — at resolution; falls back to
        //   controller-only when no live game). For each card on
        //   the battlefield: if it is an Artifact, Creature, or
        //   Enchantment, and its mana value is ≤ X, destroy it
        //   (move to its owner's graveyard). Lands and Planeswalkers
        //   are excluded by the type predicate.
        // ----------------------------------------------------------------
        var sweepEffect = new Effect(
            "Pernicious Deed: destroy each artifact, creature, and enchantment with mv ≤ X",
            ctx =>
            {
                var x = xValueProvider?.Invoke() ?? 0;

                // Sacrifice payment is a no-op stub — move Pernicious
                // Deed to its owner's graveyard here so SBAs + visible
                // state line up with CR 701.16. When a bus is wired (prod
                // effects-aware build) route through Fx.Sacrifice so the
                // resolve-only dispatcher/test path publishes
                // PermanentSacrificedEvent (CR 701.16a); the live activation
                // path already moved + published via the sac cost, so this
                // no-ops there (single publish either way).
                if (card.Zone == ZoneType.Battlefield)
                {
                    if (eventBus != null)
                    {
                        Fx.Sacrifice(card, card.Controller ?? owner, eventBus);
                    }
                    else
                    {
                        owner.Zones.Battlefield.RemoveCard(card);
                        owner.Zones.Graveyard.AddCard(card);
                        card.SetZone(ZoneType.Graveyard);
                    }
                }

                // "Each artifact, creature, and enchantment" — across every
                // player's battlefield, read from the LIVE game at resolution
                // (ctx.Game.AllPlayers). No captured resolver, so the sweep is
                // correct on the routed prod build. Shape-only resolves (no
                // game) fall back to the controller's battlefield.
                var players = ctx.Game?.AllPlayers
                    ?? (IReadOnlyList<Player>)new[] { owner };

                foreach (var p in players)
                {
                    // Snapshot — we mutate the battlefield list inside
                    // the loop. ICard exposes HasType + Zone, but
                    // ManaCostValue lives on the concrete Card; cast
                    // through OfType<Card> (every battlefield resident
                    // in practice derives from Card — same approach
                    // Engineered Explosives takes).
                    var victims = p.Zones.Battlefield.GetCards()
                        .OfType<Card>()
                        .Where(c =>
                            c.HasType(CardType.Artifact)
                            || c.HasType(CardType.Creature)
                            || c.HasType(CardType.Enchantment))
                        .Where(c => c.ManaCostValue.TotalValue <= x)
                        .ToList();

                    foreach (var v in victims)
                    {
                        // Owner-routed graveyard move (CR 701.7b — destroyed
                        // permanents go to their owner's graveyard). Falls
                        // back to the iterated player when Owner is null so
                        // shape-only tests with untyped controllers still
                        // surface the destruction visibly.
                        var victimOwner = v.Owner ?? p;
                        p.Zones.Battlefield.RemoveCard(v);
                        victimOwner.Zones.Graveyard.AddCard(v);
                        v.SetZone(ZoneType.Graveyard);
                    }
                }

                return ValueTask.CompletedTask;
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{X}"),
                AdditionalCost.Sacrifice(card, eventBus),
            },
            effects: new IEffect[] { sweepEffect });

        card.AddAbility(ability);

        return card;
    }
}
