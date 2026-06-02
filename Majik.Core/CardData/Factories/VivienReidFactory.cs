using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vivien Reid (Core Set 2019, {3}{G}{G}).
///
/// Legendary Planeswalker — Vivien. Starting loyalty 5.
/// Oracle text (Scryfall, verified):
///   "+1: Look at the top four cards of your library. You may reveal a
///        creature or land card from among them and put it into your hand.
///        Put the rest on the bottom of your library in a random order.
///    −3: Destroy target artifact, enchantment, or creature with flying.
///    −8: You get an emblem with 'Creatures you control get +2/+2 and have
///         vigilance, trample, and indestructible.'"
///
/// The base shape (name, Legendary Planeswalker — Vivien, {3}{G}{G}, loyalty
/// 5) is materialised from the embedded JSON definition
/// (<c>vivien-reid.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three loyalty abilities are
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// loyalty abilities, library digs, destroy clauses, or emblems, so they live
/// in the factory (same posture as <see cref="ChandraTorchOfDefianceFactory"/>
/// / <see cref="LilianaTheLastHopeFactory"/> / <see cref="KaitoBaneOfNightmaresFactory"/>).
///
/// ## Implemented (v1)
/// - <b>+1: Look at the top four cards of your library; you may reveal a
///   creature or land card from among them and put it into your hand; put the
///   rest on the bottom of your library (CR 606 + CR 701.15 reveal-and-choose
///   + CR 121.2)</b>: routed through the shared
///   <see cref="RevealAndChoose.RevealTopAndChoose"/> primitive — peeks the
///   top four (clamped to library size), prompts the registered agent to pick
///   one creature/land card (deterministic first-eligible when no agent is
///   wired), puts the pick into hand, and re-bottoms the rest. "You may" ⇒ an
///   empty eligible set / decline is a legal no-op (CR 116.1b). Empty library
///   ⇒ no dig, loyalty change still applies (CR 606.3).
/// - <b>−3: Destroy target artifact, enchantment, or creature with flying
///   (CR 606 + CR 701.7)</b>: destroys the first legal permanent the
///   <paramref name="targetResolver"/> offers — an artifact, an enchantment,
///   or a creature with the Flying keyword (CR 702.9). The destroy routes
///   through <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Destroy"/>, so indestructible (CR 702.12b) and
///   regeneration shields (CR 701.15c) are respected. No resolver / no legal
///   target ⇒ no-op (loyalty change still applies).
/// - <b>−8: You get an emblem with "Creatures you control get +2/+2 and have
///   vigilance, trample, and indestructible" (CR 606 + CR 114)</b>: mints a
///   structural <see cref="Emblem"/> in the controller's command zone. The
///   continuous team-wide anthem (Layer 7c +2/+2 + Layer 6 keyword grants) is
///   recorded structurally — same posture as the Kaito / Liliana / Wrenn
///   emblems, whose anthem layer wiring is the deferred surface below.
///
/// ## Deferred (v1 gaps)
/// - <b>+1 "in a random order"</b>: the rest of the revealed cards go to the
///   bottom in reveal order, not a randomized order. Library order is hidden
///   information, so the printed randomization is cosmetic for the observable
///   contract; the deterministic re-bottom is the same posture every
///   reveal-and-choose card (Impulse, Sleight of Hand) takes through the
///   shared primitive.
/// - <b>−3 target prompt</b>: <see cref="LoyaltyAbility"/> doesn't declare a
///   <see cref="Majik.Core.Targeting.TargetRequest"/>; the destroy target is
///   picked from <paramref name="targetResolver"/> rather than the agent.
///   Same gap Chandra / Liliana / Nahiri share.
/// - <b>−8 emblem anthem layer</b>: the "+2/+2 and vigilance/trample/
///   indestructible to creatures you control" continuous static is structural
///   (the emblem exists in the command zone); the live layer-system anthem is
///   not auto-registered (same posture as the Kaito / Liliana / Wrenn
///   emblems). <see cref="LordStaticEffect"/> exists for a battlefield-source
///   team buff, but it gates on its source being a battlefield permanent
///   (<c>IsActive() => Source.Zone == Battlefield</c>); an emblem lives in the
///   command zone, so an emblem-sourced static needs a separate command-zone
///   anthem primitive that does not exist yet — deferred, not half-built.
/// </summary>
[CardName("Vivien Reid")]
public static class VivienReidFactory
{
    public const string CardName = "Vivien Reid";
    public const string Slug = "vivien-reid";
    public const int StartingLoyalty = 5;
    public const int Plus1Loyalty = +1;
    public const int Minus3Loyalty = -3;
    public const int Minus8Loyalty = -8;
    /// <summary>CR 701.15 — the +1 looks at the top four cards.</summary>
    public const int DigCount = 4;

    /// <summary>
    /// Construct Vivien with no resolvers / triggers wired — the +1 still digs
    /// four (deterministic first-eligible pick into hand, rest re-bottomed),
    /// the −3 no-ops (no target resolver), and the −8 mints a structural-only
    /// emblem. Loyalty changes still apply. Suitable for shape / dispatcher
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, targetResolver: null, triggers: null);

    /// <summary>
    /// Construct Vivien Reid.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="targetResolver">Returns candidate permanents for the −3
    /// "destroy target artifact, enchantment, or creature with flying" clause.
    /// v1 destroys the first legal candidate. May be null — the clause no-ops.
    /// </param>
    /// <param name="triggers">Reserved for parity with sibling planeswalker
    /// factories whose emblems carry triggered abilities; Vivien's −8 emblem is
    /// a static anthem (no trigger), so this is currently unused. May be null.
    /// </param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Permanent>>? targetResolver,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = triggers; // reserved for parity with sibling PW factories

        // Base shape from the embedded JSON definition (name, Legendary
        // Planeswalker — Vivien, {3}{G}{G}, loyalty 5). The JSON carries no
        // abilities — the three loyalty abilities are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var vivien = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        // -- +1: Look at the top four cards of your library. You may reveal a
        //    creature or land card from among them and put it into your hand.
        //    Put the rest on the bottom of your library in a random order. ----
        // CR 606 (loyalty) + CR 701.15 (reveal-and-choose) + CR 121.2 (top-N
        // underflow). Routed through the shared RevealAndChoose primitive: it
        // prompts the registered agent (deterministic first-eligible when none
        // is wired), puts the pick into hand, and re-bottoms the rest. "You
        // may" ⇒ empty eligible / decline is a legal no-op (CR 116.1b). The
        // printed "in a random order" re-bottom is order-preserving here (see
        // class xmldoc) — library order is hidden, so it's cosmetic.
        vivien.AddAbility(new LoyaltyAbility(vivien, Plus1Loyalty, () =>
        {
            var controller = vivien.Controller ?? owner;
            RevealAndChoose.RevealTopAndChoose(
                caster: controller,
                count: DigCount,
                eligiblePredicate: c =>
                    c.HasType(CardType.Creature) || c.HasType(CardType.Land),
                optional: true,
                label: "Creature or land to put into your hand",
                pickedDestination: ZoneType.Hand,
                restDestination: ZoneType.Library,
                sourceTag: Slug);
        }));

        // -- −3: Destroy target artifact, enchantment, or creature with
        //    flying. ----------------------------------------------------------
        // CR 606 (loyalty) + CR 701.7 (destroy). v1 destroys the first legal
        // candidate from the resolver — an artifact, an enchantment, or a
        // creature with the Flying keyword (CR 702.9). MoveToGraveyard with
        // ZoneMoveReason.Destroy respects indestructible (CR 702.12b) and
        // regeneration (CR 701.15c).
        vivien.AddAbility(new LoyaltyAbility(vivien, Minus3Loyalty, () =>
        {
            var candidates = targetResolver?.Invoke();
            if (candidates == null) return;
            foreach (var p in candidates)
            {
                if (p == null) continue;
                if (p.Zone != ZoneType.Battlefield) continue;
                if (!IsDestroyTarget(p)) continue;

                OracleSpellBinder.MoveToGraveyard(p, ZoneMoveReason.Destroy);
                return; // "target" — a single permanent.
            }
        }));

        // -- −8: You get an emblem with "Creatures you control get +2/+2 and
        //    have vigilance, trample, and indestructible." -------------------
        // CR 606 (loyalty) + CR 114 (emblem). Structural emblem — the anthem
        // layer is the deferred surface (see class xmldoc; matches the Kaito /
        // Liliana / Wrenn emblem posture).
        vivien.AddAbility(new LoyaltyAbility(vivien, Minus8Loyalty, () =>
        {
            var controller = vivien.Controller ?? owner;
            var emblem = new Emblem(
                controller: controller,
                sourceName:
                    $"{CardName} — \"Creatures you control get +2/+2 and have " +
                    "vigilance, trample, and indestructible\" emblem",
                abilities: Array.Empty<IAbility>());
            controller.AddEmblem(emblem);
        }));

        return vivien;
    }

    /// <summary>
    /// CR 701.7 — legal −3 destroy target: any artifact, any enchantment, or a
    /// creature with the Flying keyword (CR 702.9). Flying is read off the
    /// permanent's <see cref="KeywordAbility"/> markers (the same marker
    /// <see cref="Majik.Core.Targeting.TargetLegality"/> scans for).
    /// </summary>
    private static bool IsDestroyTarget(Permanent p)
    {
        if (p.HasType(CardType.Artifact)) return true;
        if (p.HasType(CardType.Enchantment)) return true;
        if (p.HasType(CardType.Creature) && HasFlying(p)) return true;
        return false;
    }

    private static bool HasFlying(Permanent p) =>
        p.Abilities
            .OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Flying", StringComparison.OrdinalIgnoreCase));
}
