using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Primeval Titan (Magic 2011, {4}{G}{G}).
///
/// Creature — Giant 6/6. Oracle text:
///   "Trample
///    Whenever Primeval Titan enters or attacks, you may search your
///    library for up to two land cards, put them onto the battlefield
///    tapped, then shuffle."
///
/// ## Implemented (v1)
/// - 6/6 Creature — Giant, mana cost {4}{G}{G}.
/// - Trample wired as a <see cref="KeywordAbility"/> marker (CR 702.19)
///   consumed by <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/>.
/// - <b>ETB triggered ability (CR 603.1)</b>: On battlefield entry,
///   tutor up to two lands from the controller's library onto the
///   battlefield tapped (CR 701.19a). "Up to two" composes the existing
///   single-land tutor primitive twice — the agent (via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) picks zero or one
///   land per slot. Returning null from the agent on either slot is a
///   legal decline.
/// - <b>Attack triggered ability (CR 508.1f)</b>: Same tutor effect on
///   the <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
///   surface, so each combat where Primeval Titan attacks fetches up to
///   two more lands.
///
/// ## Selector override
/// Both triggers default to consulting the registered agent twice. For
/// deterministic tests, callers may pass a <c>selector</c>
/// (<see cref="Func{Player, IReadOnlyList{ICard}}"/>) that returns the
/// pre-picked land set (0, 1, or 2 cards). The factory clamps to two
/// picks, filters non-lands defensively, and ignores duplicates.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt</b>: each trigger is faithfully optional in
///   the oracle text. The v1 effect always attempts the search — agent
///   returning null per pick acts as the opt-out lever. A first-class
///   yes/no agent prompt is deferred (see <c>StoneforgeMystic</c> for
///   the same gap).
/// - <b>Per-pick uniqueness</b>: the engine does not yet enforce
///   that a multi-pick search must choose distinct cards. The default
///   agent path naturally picks distinct lands (each picked land is
///   removed from the library before the second prompt). Test selectors
///   that return duplicates have the duplicates filtered defensively.
/// </summary>
[CardName("Primeval Titan")]
public static class PrimevalTitanFactory
{
    public const string CardName = "Primeval Titan";
    public const string PrintedManaCost = "{4}{G}{G}";

    /// <summary>
    /// Construct Primeval Titan with no live TriggerManager wiring and the
    /// default agent-driven selector (the shape/dispatcher path).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, selector: null);

    /// <summary>
    /// Construct Primeval Titan with optional runtime services.
    /// <paramref name="triggers"/> registers the ETB + attack triggers
    /// with a live manager. <paramref name="selector"/> overrides the
    /// agent-driven default with a deterministic test selector returning
    /// the up-to-two lands to fetch from the controller's library.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<Player, IReadOnlyList<ICard>>? selector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 6,
            toughness: 6,
            subtypes: new[] { CardSubtype.Giant });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Trample — CR 702.19. KeywordAbility marker; CombatAbilities
        // .HasTrample / Attacker.HasTrample / CombatDamageAssigner consume it.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // Shared tutor effect — search up to two lands → battlefield tapped.
        // CR 701.19a (search), CR 701.20a (shuffle after — see helper call).
        // ----------------------------------------------------------------
        IEffect BuildTutorEffect(string label) =>
            new Effect(label, () => TutorUpToTwoLandsTapped(owner, selector));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "Whenever Primeval Titan enters …, you may search your library
        //    for up to two land cards, put them onto the battlefield
        //    tapped, then shuffle."
        // ----------------------------------------------------------------
        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { BuildTutorEffect("Primeval Titan: ETB tutor up to 2 lands -> battlefield tapped") },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Attack triggered ability — CR 508.1f.
        //   "Whenever Primeval Titan … attacks, you may search your
        //    library for up to two land cards, put them onto the
        //    battlefield tapped, then shuffle."
        // Fires on CreatureAttacksEvent matching this card.
        // ----------------------------------------------------------------
        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new[] { BuildTutorEffect("Primeval Titan: attack tutor up to 2 lands -> battlefield tapped") },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Tutor up to two land cards from <paramref name="caster"/>'s library
    /// onto the battlefield tapped. When <paramref name="selector"/> is
    /// supplied, its return value (clamped to 2 distinct land entries) is
    /// used directly; otherwise the registered agent is prompted twice
    /// via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>. The agent
    /// may return null per slot to decline (legal under CR 701.19a).
    /// </summary>
    private static void TutorUpToTwoLandsTapped(
        Player caster,
        Func<Player, IReadOnlyList<ICard>>? selector)
    {
        // Deterministic selector path (tests). Filter to lands actually
        // present in the library, dedupe by reference, and clamp to 2.
        if (selector != null)
        {
            var picks = selector(caster) ?? Array.Empty<ICard>();
            var library = caster.Zones.Library.GetCards().ToHashSet();
            var seen = new HashSet<ICard>();
            int placed = 0;
            foreach (var pick in picks)
            {
                if (placed == 2) break;
                if (pick == null) continue;
                if (!pick.HasType(CardType.Land)) continue;
                if (!library.Contains(pick)) continue;
                if (!seen.Add(pick)) continue;
                MoveToBattlefieldTapped(caster, pick);
                placed++;
            }
            // CR 701.20a — shuffle after the search resolves.
            Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(caster, "primeval-titan");
            return;
        }

        // Agent-driven path: two sequential single-land tutors. Each call
        // refilters the library so the agent never sees the previously
        // picked land in the candidate set.
        var agent = AgentRegistry.Get(caster);
        for (int slot = 0; slot < 2; slot++)
        {
            var candidates = caster.Zones.Library.GetCards()
                .Where(c => c.HasType(CardType.Land))
                .ToList();
            if (candidates.Count == 0) break;

            ICard? pick = agent != null
                ? agent.ChooseLibraryPickAsync(ctx: null, candidates, "land card")
                    .GetAwaiter().GetResult()
                : candidates[0];
            if (pick == null) break; // CR 701.19a — decline is legal.

            MoveToBattlefieldTapped(caster, pick);
        }
        // CR 701.20a — shuffle after the search resolves.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(caster, "primeval-titan");
    }

    /// <summary>
    /// Move <paramref name="pick"/> from the library to
    /// <paramref name="caster"/>'s battlefield tapped (CR 701.19a + CR
    /// 305 — lands enter tapped per the printed instruction).
    /// <para>
    /// CR 603.6a / CR 614 — when a live <see cref="ZoneService"/> is
    /// registered for the caster, the Library → Battlefield move routes
    /// through it so <see cref="Majik.Core.Events.CardMovedEvent"/>
    /// publishes and ETB-tapped / "untap on ETB tapped" replacements +
    /// triggers fire (bounce-land bounce, Amulet of Vigor untap).
    /// We tap the card AFTER the move so any ETB-tapped replacement
    /// (bounce lands, shock lands) has already run; double-tapping is
    /// a no-op so this is safe. An Amulet-of-Vigor trigger that already
    /// went pending off the move's CardMovedEvent stays pending — the
    /// post-move tap doesn't suppress it because the trigger ran its
    /// condition at event-publish time.
    /// </para>
    /// </summary>
    private static void MoveToBattlefieldTapped(Player caster, ICard pick)
    {
        var zones = ZoneServiceRegistry.Get(caster);
        if (zones != null)
        {
            zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, caster);
            if (pick is Permanent perm && !perm.IsTapped)
            {
                perm.Tap();
            }
        }
        else
        {
            caster.Zones.Library.RemoveCard(pick);
            caster.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(caster);
            if (pick is Permanent perm)
            {
                perm.Tap();
            }
        }
    }
}
