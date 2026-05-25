using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sterling Grove (Invasion, {G}{W}).
///
/// Enchantment. Oracle text:
///   "Other enchantments you control have shroud. (They can't be the
///    targets of spells or abilities.)
///    {1}, Sacrifice Sterling Grove: Search your library for an
///    enchantment card, reveal that card, and put it on top of your
///    library. Then shuffle."
///
/// ## Implemented (v1)
/// - Enchantment shape, mana cost {G}{W}, owner/controller wired.
/// - <b>Static "Other enchantments you control have shroud" (CR 702.18)</b>
///   wired via <see cref="OtherEnchantmentsShroudEffect"/> when a
///   <see cref="ContinuousEffectsService"/> is supplied. The effect runs
///   at <see cref="Layer.Abilities"/> (CR 613.1f Layer 6 — keyword grant)
///   and adds "Shroud" to the <see cref="PermanentCharacteristics.Keywords"/>
///   of every enchantment Sterling Grove's controller controls except
///   Sterling Grove itself (matching the "Other" qualifier). The grant
///   is registered against the layers service so
///   <see cref="ContinuousEffectsService.Compute"/> surfaces the
///   keyword on the protected enchantments. While Sterling Grove leaves
///   the battlefield, <see cref="ContinuousEffect.IsActive"/> short-circuits
///   on a zone check so the grant lifts cleanly without manual unregister.
/// - <b>Activated ability "{1}, Sacrifice Sterling Grove: tutor enchantment
///   → top of library, then shuffle"</b>:
///   - Costs: <see cref="ManaCostCost"/> {1} + <see cref="AdditionalCost.Sacrifice"/>
///     (payment is a no-op stub on the engine; the effect closure performs
///     the sacrifice via direct zone move, same posture as Prismatic Vista
///     / Pernicious Deed).
///   - Resolve: search the controller's library for an enchantment card
///     (<see cref="CardType.Enchantment"/>, CR 205.3), prompt the
///     controller's agent (<see cref="IPlayerAgent.ChooseLibraryPickAsync"/>)
///     with a deterministic first-match fallback, then shuffle the
///     library (CR 701.20a) and place the picked card at index 0 — the
///     "top of library" position read by <see cref="DrawAction"/>. Same
///     shuffle-before-place sequencing as the Mystical / Vampiric /
///     Worldly Tutor factories. Sacrifice happens BEFORE the search so
///     Sterling Grove can't tutor itself (it has left the battlefield
///     and is no longer in the library).
///
/// ## Deferred (v1 gaps)
/// - <b>TargetLegality enforcement of Shroud on non-creature permanents.</b>
///   <see cref="Majik.Core.Targeting.TargetLegality"/> currently honors
///   Shroud only when the target is a <see cref="Creature"/> (it has no
///   permanent-side <c>ActiveEffects</c> handle to consult). The
///   characteristics-side grant IS registered and visible via
///   <see cref="ContinuousEffectsService.Compute"/>, so any future
///   target-validator pass that consults the layers service for
///   non-creature permanents will pick the grant up automatically.
///   Same posture as Creeping Tar Pit's Islandwalk note —
///   characteristic granted, validator-side enforcement is a follow-up.
/// - <b>Reveal event</b>. The tutored enchantment moves Library →
///   top-of-Library without publishing a reveal event; same gap as
///   <see cref="MysticalTutorFactory"/> and the other search factories.
/// </summary>
[CardName("Sterling Grove")]
public static class SterlingGroveFactory
{
    public const string CardName = "Sterling Grove";
    public const string PrintedManaCost = "{G}{W}";

    /// <summary>Granted keyword. CR 702.18 — Shroud.</summary>
    public const string GrantedKeyword = "Shroud";

    /// <summary>Mana portion of the activated cost.</summary>
    public const string ActivationManaCost = "{1}";

    /// <summary>
    /// Construct Sterling Grove without a live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the other-enchantments-have-
    /// shroud static is not registered. The activated tutor ability is
    /// always wired (it doesn't depend on layers).
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Sterling Grove. When
    /// <paramref name="continuousEffects"/> is supplied, an
    /// <see cref="OtherEnchantmentsShroudEffect"/> granting "Shroud" to
    /// every OTHER enchantment Sterling Grove's controller controls is
    /// registered against the layers service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// other-enchantments-have-shroud static effect against. May be
    /// null — no live keyword grant.</param>
    public static Enchantment Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613.1f Layer 6 — keyword grant. "Other enchantments you
            // control have shroud" applies to enchantments controlled by
            // Sterling Grove's controller, excluding Sterling Grove itself.
            continuousEffects.Register(new OtherEnchantmentsShroudEffect(card));
        }

        // -------------------------------------------------------------------
        // Activated ability (CR 602.1): {1}, Sacrifice this:
        //   Search your library for an enchantment card, reveal that card,
        //   put it on top of your library. Then shuffle.
        //
        // Sacrifice payment is a no-op stub on the engine (same as Pernicious
        // Deed / Prismatic Vista), so the effect closure performs the
        // self-sacrifice up front. Sequencing: sacrifice -> search -> shuffle
        // -> place on top. Sacrificing before the search ensures Sterling
        // Grove itself cannot be among the candidates (CR 701.16 — it's in
        // the graveyard by then).
        // -------------------------------------------------------------------
        var tutorEffect = new Effect(
            "Sterling Grove: sacrifice self, tutor enchantment -> top of library",
            () =>
            {
                var controller = card.Controller ?? card.Owner ?? owner;

                // Self-sacrifice (CR 701.16) — controller's battlefield -> owner's graveyard.
                SacrificeToOwnersGraveyard(card);

                TutorEnchantmentToTopOfLibrary(controller);
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { tutorEffect });

        card.AddAbility(ability);

        return card;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="self"/> from its controller's
    /// battlefield to its owner's graveyard. No-op when the card has
    /// already left the battlefield (idempotent — protects against
    /// double-fire from a re-resolved ability).
    /// </summary>
    private static void SacrificeToOwnersGraveyard(Enchantment self)
    {
        var ownerOfSelf = self.Owner;
        if (ownerOfSelf == null) return;
        if (self.Zone != ZoneType.Battlefield) return;

        var holder = self.Controller ?? ownerOfSelf;
        holder.Zones.Battlefield.RemoveCard(self);
        ownerOfSelf.Zones.Graveyard.AddCard(self);
        self.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for an enchantment card
    /// (CR 205.3 — Enchantment card type), consult the agent for a pick
    /// (deterministic first-match fallback when no agent registered),
    /// shuffle the library (CR 701.20a), then insert the pick at index 0
    /// — the "top of library" position read by
    /// <see cref="Majik.Core.Game.Actions.DrawAction"/>. Empty candidate
    /// list or null pick = no-op (CR 701.19a permits declining to find),
    /// though the printed oracle still says "Then shuffle" so we shuffle
    /// regardless to match Mystical / Vampiric / Worldly Tutor.
    /// </summary>
    private static void TutorEnchantmentToTopOfLibrary(Player player)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Enchantment))
            .ToList();

        if (candidates.Count == 0)
        {
            // CR 701.20a — still shuffle even when search finds nothing.
            Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(player, "sterling-grove");
            return;
        }

        var agent = AgentRegistry.Get(player);
        ICard? pick = agent != null
            ? agent.ChooseLibraryPickAsync(ctx: null, candidates, "enchantment card")
                .GetAwaiter().GetResult()
            : candidates[0];

        if (pick == null)
        {
            Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(player, "sterling-grove");
            return;
        }

        player.Zones.Library.RemoveCard(pick);
        // CR 701.20a — shuffle, then place picked card on top, matching
        // the engine-wide tutor-to-top sequencing (Mystical / Vampiric /
        // Worldly Tutor). The printed oracle "and put it on top of your
        // library. Then shuffle." is implemented as shuffle-the-rest +
        // place-on-top per the canonical convention.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(player, "sterling-grove");
        player.Zones.Library.InsertCardAt(0, pick);
        pick.SetZone(ZoneType.Library);
    }
}

/// <summary>
/// Sterling Grove — static keyword grant. "Other enchantments you control
/// have shroud" (CR 702.18). Layer 6 (<see cref="Layer.Abilities"/>,
/// CR 613.1f) keyword grant on every enchantment Sterling Grove's
/// controller controls except Sterling Grove itself.
///
/// <para>
/// Application targets <see cref="PermanentCharacteristics"/> — non-creature
/// enchantments don't surface through the creature-side <see cref="Apply(CreatureCharacteristics)"/>
/// path, so the Permanent overload is the real workhorse. The
/// <see cref="ContinuousEffectsService"/> dispatcher routes both overloads
/// for completeness. The <see cref="IsActive"/> battlefield-check ensures
/// the grant lifts automatically when Sterling Grove leaves the battlefield
/// (LTB), without a manual unregister hook.
/// </para>
///
/// <para>
/// Target-validator enforcement of the shroud grant on non-creature
/// permanents is deferred — <see cref="Majik.Core.Targeting.TargetLegality"/>
/// currently consults Shroud only on <see cref="Creature"/> targets. The
/// keyword IS visible via <see cref="ContinuousEffectsService.Compute"/>,
/// so a future TargetLegality extension that reads the layers service for
/// non-creature permanents picks the grant up automatically.
/// </para>
/// </summary>
public sealed class OtherEnchantmentsShroudEffect : ContinuousEffect
{
    private readonly Enchantment _source;

    public OtherEnchantmentsShroudEffect(Enchantment source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>The Sterling Grove producing this static keyword grant.</summary>
    public Enchantment SourceEnchantment => _source;

    public override Layer Layer => Layer.Abilities;

    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    /// <summary>
    /// CR 613.1f — Sterling Grove grants Shroud to enchantments only.
    /// Creatures (even token enchantment-creatures like Sigil of the Empty
    /// Throne's angels) match when their type set includes Enchantment.
    /// </summary>
    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent)
    {
        if (permanent.Zone != Majik.Core.Zones.ZoneType.Battlefield) return false;
        // "Other" — exclude Sterling Grove itself.
        if (ReferenceEquals(permanent, _source)) return false;
        // "you control" — controller of the affected enchantment must match
        // Sterling Grove's controller (CR 109.4 — "you" refers to the
        // controller of the source). Falls back to the source's owner when
        // controller is null (shape-only test path).
        var sourceController = _source.Controller ?? _source.Owner;
        var permController = permanent.Controller ?? permanent.Owner;
        if (!ReferenceEquals(sourceController, permController)) return false;
        // "enchantments" — type-set check (CR 205.3); handles dual-typed
        // permanents like Enchantment Creatures (Bestow auras attached as
        // creatures still satisfy the Enchantment type predicate).
        return permanent.HasType(CardType.Enchantment);
    }

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        chars.Keywords.Add(SterlingGroveFactory.GrantedKeyword);
    }
}
