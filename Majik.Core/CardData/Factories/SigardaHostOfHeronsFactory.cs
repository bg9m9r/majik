using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sigarda, Host of Herons (Avacyn Restored,
/// {2}{G}{W}).
///
/// Legendary Creature — Angel. 5/5 with Flying, Hexproof.
/// Oracle text:
///   "Flying, hexproof.
///    Spells and abilities your opponents control can't cause you to
///    sacrifice permanents."
///
/// ## Implemented (v1)
/// - 5/5 Legendary Creature — Angel, mana cost {2}{G}{W}, supertypes
///   {Legendary}, subtypes {Angel}.
/// - Printed Flying + Hexproof (CR 702.9 / 702.11) wired as
///   <see cref="KeywordAbility"/> markers.
/// - <b>Forced-sacrifice protection</b> (CR 701.16 / CR 109.5): while
///   Sigarda is on the battlefield, an entry is registered in
///   <see cref="SacrificeRestriction"/> protecting her controller against
///   opponent-driven sacrifice. The sacrifice surfaces consult
///   <see cref="Player.IsProtectedFromForcedSacrifice"/>:
///   <list type="bullet">
///     <item><see cref="Majik.Core.Primitives.Fx.Sacrifice(ICard, ICard)"/>
///       — opponent-source overload silently no-ops.</item>
///     <item>The Diabolic-Edict / Innocent-Blood family of templates
///       (<see cref="Majik.Core.CardData.SpellTemplates.Templates.Misc.StubBindTemplates"/>'s
///       <c>TargetPlayerSacrificesCreatureTemplate</c> and
///       <c>EachOpponentSacrificesCreatureTemplate</c>) skip the
///       protected target without sacrificing anything.</item>
///   </list>
///   Additional costs paid by the controller themselves (e.g. as part of
///   casting their own spell) are NOT gated — Sigarda only protects
///   against opponent-driven forced sacrifice.
///
/// Lifecycle is bus-driven: a <see cref="CardMovedEvent"/> subscription
/// (registered when an event bus is supplied to <see cref="Create"/>)
/// re-syncs the registry whenever Sigarda moves between zones. The
/// initial sync at Attach time covers the "Sigarda was created directly
/// onto the battlefield" path used by most tests.
///
/// ## Deferred (v1 gaps)
/// - <b>Activated-ability sources</b>: cost-driven sacrifices (Smallpox-
///   style) currently route through callsites that may not yet plumb the
///   requesting source through the <see cref="Majik.Core.Primitives.Fx.Sacrifice(ICard, ICard)"/>
///   overload. The single-arg overload bypasses the gate; effects that
///   force opponent-side sacrifices should be migrated to the opponent-
///   source overload as they're touched.
/// </summary>
[CardName("Sigarda, Host of Herons")]
public static class SigardaHostOfHeronsFactory
{
    public const string CardName = "Sigarda, Host of Herons";
    public const string PrintedManaCost = "{2}{G}{W}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Sigarda with printed keywords wired but no live
    /// event-bus subscription. The forced-sacrifice protection is
    /// synced once at construction (active iff Sigarda is already on the
    /// battlefield) but won't re-sync across subsequent zone moves.
    /// Suitable for unit / shape tests that mutate <see cref="Card.Zone"/>
    /// directly and call <see cref="SacrificeRestriction"/> themselves.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Sigarda with optional event-bus wiring. When
    /// <paramref name="eventBus"/> is supplied, a
    /// <see cref="CardMovedEvent"/> subscription re-syncs the
    /// <see cref="SacrificeRestriction"/> registry as Sigarda enters /
    /// leaves the battlefield.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Angel });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 / 702.11 — printed evergreens.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Hexproof", card, owner));

        // Sync the protection grant now (covers "created onto the
        // battlefield" tests) and again on every CardMovedEvent involving
        // Sigarda. Sync = add when on battlefield, remove otherwise.
        void Sync()
        {
            if (card.Zone == ZoneType.Battlefield)
            {
                SacrificeRestriction.AddCannotBeForcedToSacrifice(owner, card);
            }
            else
            {
                SacrificeRestriction.RemoveCannotBeForcedToSacrifice(owner, card);
            }
        }

        Sync();

        if (eventBus != null)
        {
            eventBus.SubscribeAll(e =>
            {
                if (e is CardMovedEvent moved && ReferenceEquals(moved.Card, card))
                {
                    Sync();
                }
            });
        }

        return card;
    }
}
