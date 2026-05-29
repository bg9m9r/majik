using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Teferi, Hero of Dominaria (Dominaria, {3}{W}{U}).
///
/// Legendary Planeswalker — Teferi. Starting loyalty 4.
/// Oracle text (Scryfall, verified):
///   "+1: Draw a card. At the beginning of the next end step, untap up to
///        two lands.
///    −3: Put target nonland permanent into its owner's library third from
///        the top.
///    −8: You get an emblem with 'Whenever you draw a card, exile target
///        permanent an opponent controls.'"
///
/// The card's base shape (name, Legendary Planeswalker — Teferi, {3}{W}{U},
/// loyalty 4) is materialised from the embedded JSON definition
/// (<c>teferi-hero-of-dominaria.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three loyalty abilities are
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// loyalty abilities, delayed triggers, targeted library-insertion, or
/// emblems, so they live in the factory (same posture as
/// <see cref="UginEyeOfTheStormsFactory"/> and
/// <see cref="LilianaTheLastHopeFactory"/>).
///
/// ## Implemented (v1)
/// - <b>+1: Draw a card; at the beginning of the next end step, untap up to
///   two lands (CR 606 + CR 121 + CR 603.7)</b>: draws one card for the
///   controller (<see cref="Fx.DrawCards"/>), then — when
///   <paramref name="triggers"/> is wired — registers a one-shot
///   <see cref="DelayedTriggeredAbility"/> over the next End-step
///   <see cref="StepStartedEvent"/> (CR 603.7 — "at the beginning of the next
///   end step"). The delayed effect untaps up to two lands taken from
///   <paramref name="landUntapResolver"/> (<see cref="Fx.Untap"/>, capped at
///   two — "up to two"). The TriggerManager auto-unregisters the delayed
///   ability after it fires (CR 603.7c). Without resolvers / triggers the
///   draw still happens; the untap clause is a legal no-op.
/// - <b>−3: Put target nonland permanent into its owner's library third from
///   the top (CR 606 + CR 701 + CR 401)</b>: takes the first nonland
///   permanent from <paramref name="targetPermanentResolver"/> (CR 110.4a —
///   nonland = not a Land), removes it from its current battlefield and
///   inserts it into its <em>owner's</em> library at index 2 — "third from
///   the top" in a top-first library — via <see cref="IZone.InsertCardAt"/>.
///   Without a resolver the clause no-ops (loyalty change still applies).
/// - <b>−8: emblem with "Whenever you draw a card, exile target permanent an
///   opponent controls" (CR 606 + CR 114 + CR 603.1)</b>: mints a structural
///   <see cref="Emblem"/> in the controller's command zone. When
///   <paramref name="triggers"/> is wired, the emblem carries a
///   <see cref="TriggeredAbility"/> over <see cref="CardDrawnEvent"/> gated to
///   the emblem's controller; its effect exiles the first opponent-controlled
///   permanent from <paramref name="opponentPermanentResolver"/> (CR 701.21).
///   Structural-only without the trigger service (matches Liliana −7 posture).
///
/// ## Deferred (v1 gaps)
/// - <b>Target prompts</b>: <see cref="LoyaltyAbility"/> and the emblem's
///   triggered ability don't declare <see cref="Majik.Core.Targeting.TargetRequest"/>s;
///   the −3 target, the +1 land choice, and the emblem's exile target are all
///   picked deterministically from the supplied resolvers. Same gap Karn /
///   Liliana / Ugin share.
/// - <b>ZoneService routing</b>: −3 and the emblem's exile use raw zone
///   manipulation, so <see cref="CardMovedEvent"/> isn't published on those
///   paths (same posture as Liliana −2 / Ugin −X).
/// </summary>
[CardName("Teferi, Hero of Dominaria")]
public static class TeferiHeroOfDominariaFactory
{
    public const string CardName = "Teferi, Hero of Dominaria";
    public const string Slug = "teferi-hero-of-dominaria";
    public const int StartingLoyalty = 4;
    public const int Plus1DrawCount = 1;
    public const int Plus1MaxUntap = 2;
    public const int Minus3LibraryIndex = 2; // "third from the top" (0-based, top-first)
    public const int UltimateLoyaltyCost = -8;

    /// <summary>
    /// Construct Teferi with no resolvers / triggers wired — +1 draws but the
    /// untap clause no-ops, −3 no-ops, and −8 mints a structural-only emblem.
    /// Loyalty changes still apply. Suitable for shape / dispatcher tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, landUntapResolver: null, targetPermanentResolver: null,
            opponentPermanentResolver: null, triggers: null);

    /// <summary>
    /// Construct Teferi, Hero of Dominaria.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="landUntapResolver">Returns candidate lands for the +1
    /// delayed "untap up to two lands" clause. v1 untaps the first two. May
    /// be null — the clause no-ops.</param>
    /// <param name="targetPermanentResolver">Returns candidate permanents for
    /// the −3 "target nonland permanent" clause. v1 picks the first nonland.
    /// May be null — the clause no-ops.</param>
    /// <param name="opponentPermanentResolver">Returns candidate permanents an
    /// opponent controls for the −8 emblem's draw-trigger exile. v1 exiles the
    /// first. May be null — the clause no-ops.</param>
    /// <param name="triggers">TriggerManager used to register the +1 delayed
    /// end-step trigger and the −8 emblem's draw trigger. May be null — the +1
    /// untap clause never schedules and the emblem is structural-only.</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Land>>? landUntapResolver,
        Func<IReadOnlyList<Permanent>>? targetPermanentResolver,
        Func<IReadOnlyList<Permanent>>? opponentPermanentResolver,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Planeswalker — Teferi, {3}{W}{U}, loyalty 4). The JSON carries no
        // abilities — the three loyalty abilities are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var teferi = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        // -- +1: Draw a card. At the beginning of the next end step, untap up
        //    to two lands. ----------------------------------------------------
        // CR 606 (loyalty) + CR 121 (draw) + CR 603.7 (delayed trigger). The
        // untap is a one-shot delayed trigger on the next End step; the
        // TriggerManager auto-unregisters it after firing (CR 603.7c).
        teferi.AddAbility(new LoyaltyAbility(teferi, +1, () =>
        {
            var controller = teferi.Controller ?? owner;
            Fx.DrawCards(controller, Plus1DrawCount);

            if (triggers == null) return;

            var untapEffect = Fx.Inline(
                $"{CardName}: untap up to two lands (CR 603.7)",
                () =>
                {
                    var lands = landUntapResolver?.Invoke();
                    if (lands == null) return;
                    var untapped = 0;
                    foreach (var land in lands)
                    {
                        if (land == null) continue;
                        if (land.Zone != ZoneType.Battlefield) continue;
                        Fx.Untap(land);
                        if (++untapped >= Plus1MaxUntap) break; // "up to two"
                    }
                });

            // CR 603.7 — "at the beginning of the next end step". Fires once
            // on the next End-step StepStartedEvent regardless of whose turn
            // it is (the clause is unqualified — "the next end step").
            var delayed = new DelayedTriggeredAbility(
                source: teferi,
                controller: controller,
                condition: new EventTriggerCondition<StepStartedEvent>(
                    (e, _) => e.StepType == PhaseStateType.End),
                effects: new[] { untapEffect });

            teferi.AddAbility(delayed);
            triggers.RegisterDelayed(delayed);
        }));

        // -- −3: Put target nonland permanent into its owner's library third
        //    from the top. ----------------------------------------------------
        // CR 606 (loyalty) + CR 401 (library order) + CR 110.4a (nonland).
        // "Third from the top" = index 2 in a top-first library.
        teferi.AddAbility(new LoyaltyAbility(teferi, -3, () =>
        {
            var candidates = targetPermanentResolver?.Invoke();
            if (candidates == null) return;
            foreach (var p in candidates)
            {
                if (p == null) continue;
                if (p.HasType(CardType.Land)) continue; // "nonland permanent"
                if (p.Zone != ZoneType.Battlefield) continue;

                var holder = p.Controller ?? p.Owner;
                holder?.Zones.Battlefield.RemoveCard(p);

                // "its owner's library" — CR 401. Insert third from the top.
                var libOwner = p.Owner ?? owner;
                libOwner.Zones.Library.InsertCardAt(Minus3LibraryIndex, p);
                return; // "target" — a single permanent.
            }
        }));

        // -- −8: You get an emblem with "Whenever you draw a card, exile
        //    target permanent an opponent controls." -------------------------
        // CR 606 (loyalty) + CR 114 (emblem) + CR 603.1 (whenever-trigger) +
        // CR 701.21 (exile). When the trigger service is wired the emblem
        // carries a CardDrawnEvent trigger gated to the emblem controller.
        // Structural-only on the no-triggers path (matches Liliana −7).
        teferi.AddAbility(new LoyaltyAbility(teferi, UltimateLoyaltyCost, () =>
        {
            var controller = teferi.Controller ?? owner;

            // Build the emblem's abilities up-front — Emblem snapshots its
            // Abilities collection at construction (CR 114 — an emblem's only
            // characteristics are the abilities granted at creation), so the
            // trigger must exist before the emblem is minted.
            var emblemAbilities = new List<IAbility>();

            if (triggers != null)
            {
                var exileEffect = new Effect(
                    $"{CardName} emblem: exile target permanent an opponent controls",
                    () =>
                    {
                        var candidates = opponentPermanentResolver?.Invoke();
                        if (candidates == null) return;
                        foreach (var p in candidates)
                        {
                            if (p == null) continue;
                            if (p.Zone != ZoneType.Battlefield) continue;

                            var holder = p.Controller ?? p.Owner;
                            holder?.Zones.Battlefield.RemoveCard(p);
                            var exileOwner = p.Owner ?? holder;
                            exileOwner?.Zones.Exile.AddCard(p);
                            p.SetZone(ZoneType.Exile);
                            return; // "target permanent" — a single permanent.
                        }
                    });

                // Source is Teferi (a card) but the ability is registered
                // explicitly with the manager, so its activeZones gate is
                // irrelevant — the emblem lives in the command zone for the
                // rest of the game (CR 114) and the trigger fires for as long
                // as it stays registered.
                var drawAbility = new TriggeredAbility(
                    source: teferi,
                    controller: controller,
                    condition: new EventTriggerCondition<CardDrawnEvent>(
                        (e, _) => ReferenceEquals(e.Player, controller)),
                    effects: new IEffect[] { exileEffect });

                emblemAbilities.Add(drawAbility);
                triggers.RegisterTriggeredAbility(drawAbility);
            }

            // Mint the emblem (CR 114) with its abilities now populated.
            var emblem = new Emblem(
                controller: controller,
                sourceName: $"{CardName} — draw-exile emblem",
                abilities: emblemAbilities);
            controller.AddEmblem(emblem);
        }));

        return teferi;
    }
}
