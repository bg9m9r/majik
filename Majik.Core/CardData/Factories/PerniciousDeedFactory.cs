using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
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
public static class PerniciousDeedFactory
{
    /// <summary>
    /// Construct Pernicious Deed with no live runtime wiring.
    /// Activated ability resolves with X = 0 (single-arg path can't
    /// know X); the sweep scans only the controller's battlefield.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, xValueProvider: null, allPlayersResolver: null);

    /// <summary>
    /// Construct Pernicious Deed. When <paramref name="xValueProvider"/>
    /// is supplied, the activated ability uses that as the mv ceiling
    /// at resolution time. When <paramref name="allPlayersResolver"/>
    /// is supplied, the sweep scans every player's battlefield;
    /// otherwise only the controller's battlefield is scanned.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        Func<int>? xValueProvider,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
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
        // - Effect: iterate every battlefield (resolver-supplied; falls
        //   back to controller-only when no resolver). For each card on
        //   the battlefield: if it is an Artifact, Creature, or
        //   Enchantment, and its mana value is ≤ X, destroy it
        //   (move to its owner's graveyard). Lands and Planeswalkers
        //   are excluded by the type predicate.
        // ----------------------------------------------------------------
        var sweepEffect = new Effect(
            "Pernicious Deed: destroy each artifact, creature, and enchantment with mv ≤ X",
            () =>
            {
                var x = xValueProvider?.Invoke() ?? 0;

                // Sacrifice payment is a no-op stub — move Pernicious
                // Deed to its owner's graveyard here so SBAs + visible
                // state line up with CR 701.16.
                if (card.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(card);
                    owner.Zones.Graveyard.AddCard(card);
                    card.SetZone(ZoneType.Graveyard);
                }

                var players = allPlayersResolver?.Invoke()
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
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{X}"),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { sweepEffect });

        card.AddAbility(ability);

        return card;
    }
}
