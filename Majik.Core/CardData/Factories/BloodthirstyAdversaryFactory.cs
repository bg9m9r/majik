using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloodthirsty Adversary (Innistrad: Midnight Hunt,
/// {1}{R}). Creature — Vampire 2/2. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "Haste
///    When this creature enters, you may pay {2}{R} any number of times.
///    When you pay this cost one or more times, put that many +1/+1 counters
///    on this creature, then exile up to that many target instant and/or
///    sorcery cards with mana value 3 or less from your graveyard and copy
///    them. You may cast any number of the copies without paying their mana
///    costs."
///
/// The "Adversary" cycle's signature shape: an ETB "you may pay {cost} any
/// number of times" scaling payoff (CR 603.2 reflexive trigger). Here the
/// repeatable {2}{R} payment count N drives THREE payoffs: N +1/+1 counters,
/// up to N target instant/sorcery cards (mana value ≤ 3) exiled from your
/// graveyard, and free copies of those exiled cards.
///
/// ## Implementation
///
/// Base shape (name, Creature, Vampire, {1}{R}, 2/2) is materialised from the
/// embedded JSON definition (<c>bloodthirsty-adversary.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="StormscaleScionFactory"/> / <see cref="EverflowingChaliceFactory"/>.
/// Haste lives in the factory because the JSON ability schema doesn't yet
/// express keyword markers (same as Stormscale Scion's Flying).
///
/// ## Haste (CR 702.10)
/// Wired as a <see cref="KeywordAbility"/> marker — the same shape every
/// other JSON-backed keyworded creature uses.
///
/// ## "You may pay {2}{R} any number of times" (CR 603.2 / CR 601.2f)
/// The repeatable ETB payment is modelled the same way Everflowing Chalice's
/// Multikicker count is consumed: the caller decides N (how many times {2}{R}
/// is paid) and threads it into <see cref="BuildEtbEffect"/>. The "any number
/// of times" optional/repeatable payment is a decision the agent/UI makes at
/// resolution; this factory exposes the resolve body parameterised on the
/// chosen N rather than re-implementing a cost-payment prompt loop (the cost
/// itself, {2}{R} per iteration, is <see cref="PayCostText"/> for callers).
///
/// ## "put that many +1/+1 counters on this creature" (CR 122 / CR 121.6)
/// N <see cref="CounterType.PlusOnePlusOne"/> counters added at resolution.
///
/// ## "exile up to that many target instant/sorcery cards mv ≤ 3 ... copy
/// them ... you may cast the copies without paying their mana costs"
/// (CR 707.10 / CR 601.3b — cast without paying mana cost)
/// Mirrors <see cref="MizzixsMasteryFactory"/>'s copy-and-free-cast pipeline,
/// but scaled to up-to-N targets: for each chosen instant/sorcery card with
/// mana value ≤ 3 in the controller's graveyard, the card is exiled, its
/// <see cref="SpellDefinition"/> is looked up via the caller-supplied
/// <paramref name="spellDefinitionLookup"/> (production routes through
/// <see cref="Majik.Core.CardData.ScryfallCardFactory.LookupSpellDefinition"/>),
/// and the copy's effect list executes in place with
/// <see cref="ManaPayment.Empty"/> (CR 707.10 lossy-v1 copy — same shape as
/// Mizzix's Mastery: the copy isn't pushed as a distinct stack object).
/// "You may cast" auto-accepts (same v1 posture as Mizzix's Mastery / Past in
/// Flames). The exile happens before the copy executes, matching the printed
/// "exile ... and copy them" ordering (CR 608.2c).
///
/// ## Deferred (v1 gaps — inherited from the Mizzix's Mastery copy pipeline)
/// - Copies aren't distinct <see cref="Majik.Core.Stack.IStackObject"/>s; the
///   effect list re-executes in place (inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/> / Mizzix's Mastery).
/// - "Choose new targets for the copies" reuses the SpellDefinition's
///   resolve-time fallback target picking (same as Mizzix's Mastery).
/// - The interactive "pay {2}{R} any number of times" prompt loop and the
///   per-target selection prompt are caller responsibilities; this factory
///   takes the already-decided N + already-chosen target cards.
/// </summary>
[CardName("Bloodthirsty Adversary")]
public static class BloodthirstyAdversaryFactory
{
    public const string CardName = "Bloodthirsty Adversary";
    public const string Slug = "bloodthirsty-adversary";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>The repeatable ETB cost — {2}{R} per payment (CR 603.2).</summary>
    public const string PayCostText = "{2}{R}";

    /// <summary>Max mana value of a graveyard instant/sorcery this can target.</summary>
    public const int MaxTargetManaValue = 3;

    /// <summary>{2}{R} — the per-iteration ETB payment cost. Exposed so callers
    /// (bot decision layer / UI / tests) build the payment without hard-coding
    /// the value.</summary>
    public static ManaCost PayCost => ManaCost.Parse(PayCostText);

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Bloodthirsty Adversary's card shape: Creature — Vampire,
    /// {1}{R}, 2/2, with Haste. The ETB reflexive payoff is built separately
    /// via <see cref="BuildEtbEffect"/> (it needs the chosen pay-count N + a
    /// SpellDefinition lookup, neither known at construction). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.10 — Haste. KeywordAbility marker, same shape as every other
        // JSON-backed keyworded creature.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        return card;
    }

    /// <summary>
    /// Build the resolve-time ETB effect for Bloodthirsty Adversary, given the
    /// already-decided number of times <paramref name="timesPaid"/> the
    /// {2}{R} was paid and the (already-chosen) target instant/sorcery cards.
    ///
    /// "When you pay this cost one or more times" (CR 603.2 reflexive
    /// trigger): when <paramref name="timesPaid"/> is 0, NOTHING happens — no
    /// counters, no exile, no copies. When ≥ 1:
    ///   1. Put <paramref name="timesPaid"/> +1/+1 counters on the creature.
    ///   2. Exile up to <paramref name="timesPaid"/> of the chosen target
    ///      instant/sorcery cards (mana value ≤ 3) from the controller's
    ///      graveyard, copy them, and cast the copies free (CR 707.10 /
    ///      CR 601.3b — "without paying their mana costs").
    /// </summary>
    /// <param name="adversary">The resolving creature.</param>
    /// <param name="controller">The controller — only THEIR graveyard is a
    /// legal source ("from your graveyard"); their copies are cast.</param>
    /// <param name="timesPaid">N — how many times the {2}{R} was paid. 0 ⇒
    /// no-op (the reflexive "when you pay one or more times" never fires).</param>
    /// <param name="chosenTargets">The target instant/sorcery cards chosen
    /// from the controller's graveyard (up to N). Each is re-validated at
    /// resolution (still in graveyard, owned by controller, instant/sorcery,
    /// mana value ≤ 3) — CR 608.2b illegal-target filtering.</param>
    /// <param name="spellDefinitionLookup">Binds an exiled card's oracle to a
    /// <see cref="SpellDefinition"/> for the free copy-cast. When null, the
    /// copy half is skipped (the exile + counters still happen) — shape-test
    /// path. Production callers always wire the lookup.</param>
    /// <param name="zoneService">Routes the graveyard → exile move so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes for any
    /// "leaves graveyard" triggers (CR 603.6a). When null, raw zone
    /// manipulation is used.</param>
    public static IEffect BuildEtbEffect(
        Creature adversary,
        Player controller,
        int timesPaid,
        IReadOnlyList<Card> chosenTargets,
        Func<ICard, SpellDefinition?>? spellDefinitionLookup = null,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(adversary);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(chosenTargets);

        return new Effect(
            $"{CardName}: pay {{2}}{{R}} ×N ⇒ N +1/+1 counters + exile up to N target instant/sorcery (mv≤3) from your graveyard, copy, cast free",
            () =>
            {
                // CR 603.2 — the reflexive "when you pay this cost one or more
                // times" only fires when N ≥ 1. N == 0 ⇒ clean no-op.
                var n = Math.Max(0, timesPaid);
                if (n == 0) return;

                // 1) "put that many +1/+1 counters on this creature" (CR 122).
                if (adversary.Zone == ZoneType.Battlefield)
                {
                    adversary.Counters.Add(CounterType.PlusOnePlusOne, n);
                }

                // 2) "exile up to that many target instant/sorcery cards with
                // mana value 3 or less from your graveyard and copy them."
                // Take at most N legal targets (CR 608.2b illegal-target
                // filtering at resolution).
                var resolved = new List<Card>();
                foreach (var t in chosenTargets)
                {
                    if (resolved.Count >= n) break;
                    if (!IsLegalGraveyardTarget(t, controller)) continue;
                    resolved.Add(t);
                }

                foreach (var card in resolved)
                {
                    // "exile ... and copy them" — exile BEFORE executing the
                    // copy (CR 608.2c ordering).
                    if (zoneService != null)
                    {
                        zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Exile, controller);
                    }
                    else
                    {
                        controller.Zones.Graveyard.RemoveCard(card);
                        controller.Zones.Exile.AddCard(card);
                        card.SetZone(ZoneType.Exile);
                    }

                    // "copy them. You may cast any number of the copies without
                    // paying their mana costs." (CR 707.10 / CR 601.3b). v1
                    // auto-accepts the "may" and executes the looked-up
                    // SpellDefinition's effects in place with empty mana
                    // payment — same lossy-copy posture as Mizzix's Mastery.
                    if (spellDefinitionLookup == null) continue;
                    var def = spellDefinitionLookup(card);
                    if (def == null) continue;

                    var p = new ChosenSpellParams(
                        ModeIndex: null,
                        X: null,
                        Targets: Array.Empty<IReadOnlyList<object>>(),
                        Mana: ManaPayment.Empty);
                    foreach (var effect in def.EffectFactory(p))
                    {
                        effect.Execute();
                    }
                }
            });
    }

    /// <summary>
    /// CR 608.2b illegal-target recheck for a chosen graveyard card: it must
    /// still be in <paramref name="controller"/>'s graveyard, owned by the
    /// controller, be an instant or sorcery, and have mana value ≤ 3.
    /// </summary>
    private static bool IsLegalGraveyardTarget(Card card, Player controller)
    {
        if (card.Zone != ZoneType.Graveyard) return false;
        if (!ReferenceEquals(card.Owner, controller)) return false;
        if (!card.HasType(CardType.Instant) && !card.HasType(CardType.Sorcery)) return false;
        return card.ManaCostValue.TotalValue <= MaxTargetManaValue;
    }

    /// <summary>
    /// The candidate pool for the "up to N target instant/sorcery cards with
    /// mana value 3 or less from your graveyard" target request — exposed so
    /// callers build the <see cref="TargetRequest"/> consistently.
    /// </summary>
    public static IReadOnlyList<Card> LegalTargets(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Graveyard.GetCards()
            .OfType<Card>()
            .Where(c => IsLegalGraveyardTarget(c, controller))
            .ToList();
    }
}
