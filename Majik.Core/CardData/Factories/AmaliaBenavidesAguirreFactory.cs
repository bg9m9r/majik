using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Amalia Benavides Aguirre
/// (The Lost Caverns of Ixalan, {W}{B}).
/// Legendary Creature — Vampire Scout 2/2.
///
/// Oracle text (verified against Scryfall):
///   "Ward—Pay 3 life.
///    Whenever you gain life, Amalia Benavides Aguirre explores. Then destroy
///    all other creatures if its power is exactly 20. (To have this creature
///    explore, reveal the top card of your library. Put that card into your
///    hand if it's a land. Otherwise, put a +1/+1 counter on this creature,
///    then put the card back or put it into your graveyard.)"
///
/// ## Implemented (v1)
/// - 2/2 Legendary Vampire Scout, mana cost {W}{B}, owner / controller wired.
/// - <b>Ward—Pay 3 life (CR 702.21c)</b>: a <see cref="KeywordAbility"/>("Ward")
///   marker for the uniform discovery surface plus a bound
///   <see cref="WardEffect"/> via <see cref="BuildWardEffect"/> whose payment is
///   a real <see cref="PayLifeCost"/>(3) — same posture as
///   <see cref="SedgemoorWitchFactory"/> / <see cref="SireOfSevenDeathsFactory"/>.
/// - <b>"Whenever you gain life, Amalia explores. Then destroy all other
///   creatures if its power is exactly 20."</b> (CR 119.3 lifegain trigger +
///   CR 701.40 explore + CR 701.7 sweep): a single
///   <see cref="TriggeredAbility"/> over <see cref="Triggers.OnLifeGainedByPlayer"/>
///   (same condition Heliod, Sun-Crowned / Ajani's Pridemate use). On
///   resolution Amalia explores herself via the shared
///   <see cref="ExploreAction.ExploreAsync"/> primitive (PR #2237) — so the
///   non-land top puts the +1/+1 counter on Amalia and may push her power up.
///   THEN, only if her CURRENT power (CR 711 — after the explore counter) is
///   exactly 20, every OTHER creature on every battlefield is destroyed
///   (<see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
///   <see cref="ZoneMoveReason.Destroy"/> — Indestructible / regeneration gate
///   applies; "all OTHER creatures" spares Amalia herself). The sweep snapshots
///   each battlefield up front (MoveToGraveyard mutates the source zone in
///   place) and enumerates players from the resolving
///   <c>ctx.Game.AllPlayers</c> snapshot, falling back to Amalia's controller's
///   own battlefield when no game context is supplied.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only; the trigger is attached but not
///   registered with a <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?)"/> — registers the lifegain
///   trigger so a matching <c>LifeChangedEvent</c> stacks it.
/// </summary>
[CardName("Amalia Benavides Aguirre")]
public static class AmaliaBenavidesAguirreFactory
{
    public const string CardName = "Amalia Benavides Aguirre";
    public const string PrintedManaCost = "{W}{B}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>The life an opponent pays for Amalia's Ward (CR 702.21c).</summary>
    public const int WardLifeAmount = 3;

    /// <summary>The exact power at which the board wipe fires (CR 711 power read
    /// after the explore counter).</summary>
    public const int WipePowerThreshold = 20;

    /// <summary>
    /// CR 702.21 — Amalia's printed "Ward—Pay 3 life" effect, bound to the
    /// supplied <paramref name="card"/>. The ward cost is the non-mana
    /// "Pay 3 life" rider, modelled via <see cref="PayLifeCost"/>;
    /// <see cref="WardEffect.Resolve"/> charges the 3-life payment when an
    /// opponent's spell/ability targets Amalia (same posture as
    /// <see cref="SedgemoorWitchFactory.BuildWardEffect"/>).
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, new PayLifeCost(WardLifeAmount));

    public static Creature Create(Player owner) => Create(owner, triggers: null);

    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Vampire, CardSubtype.Scout });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.21 — Ward—Pay 3 life. Shipped as a keyword marker (uniform
        // discovery surface) plus the functional life-payment rider via
        // BuildWardEffect / WardEffect.Resolve (Sedgemoor Witch precedent).
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        // CR 119.3 / CR 701.40 / CR 701.7 — "Whenever you gain life, Amalia
        // explores. Then destroy all other creatures if its power is exactly 20."
        var lifegainEffect = new Effect(
            $"{CardName}: explore, then destroy all other creatures if power is exactly {WipePowerThreshold} (whenever you gain life)",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // CR 701.40 — Amalia explores herself; a non-land top puts the
                // +1/+1 counter on Amalia (raising her power before the check).
                await ExploreAction.ExploreAsync(
                    creature: card,
                    controller: controller,
                    agent: ctx.Agent ?? AgentRegistry.Get(controller),
                    game: ctx.Game,
                    replacements: null,
                    eventBus: null,
                    zones: ZoneServiceRegistry.Get(controller),
                    ct: ctx.Ct).ConfigureAwait(false);

                // "Then destroy all OTHER creatures if its power is exactly 20."
                // CR 711 — read Amalia's CURRENT power (post-explore counter).
                if (card.Power != WipePowerThreshold) return;

                // CR 701.7 — destroy every other creature. Enumerate players
                // from the live game snapshot; fall back to the controller's
                // own battlefield when no game context is supplied. Snapshot
                // each battlefield up front (MoveToGraveyard mutates in place).
                var players = ctx.Game?.AllPlayers
                    ?? (IReadOnlyList<Player>)new[] { controller };
                foreach (var pl in players)
                {
                    var others = pl.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .Where(c => !ReferenceEquals(c, card))
                        .ToList();
                    foreach (var c in others)
                    {
                        Fx.MoveToGraveyard(c, ZoneMoveReason.Destroy);
                    }
                }
            });

        var lifegainTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLifeGainedByPlayer(owner),
            effects: new IEffect[] { lifegainEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(lifegainTrigger);
        triggers?.RegisterTriggeredAbility(lifegainTrigger);

        return card;
    }
}
