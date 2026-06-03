using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ajani, Caller of the Pride (Magic 2013, {1}{W}{W}).
///
/// Legendary Planeswalker — Ajani. Starting loyalty 4.
/// Oracle text (Scryfall, verified):
///   "+1: Put a +1/+1 counter on up to one target creature.
///    −3: Target creature gains flying and double strike until end of turn.
///    −8: Create X 2/2 white Cat creature tokens, where X is your life total."
///
/// The base shape (name, Legendary Planeswalker — Ajani, {1}{W}{W}, loyalty 4)
/// is materialised from the embedded JSON definition
/// (<c>ajani-caller-of-the-pride.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three loyalty abilities are
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// loyalty abilities, counters, keyword grants, or token creation, so they
/// live in the factory (same posture as
/// <see cref="ChandraTorchOfDefianceFactory"/>).
///
/// ## Implemented (v1)
/// - <b>+1: Put a +1/+1 counter on up to one target creature (CR 606 +
///   CR 122 + CR 115.1b "up to one")</b>: places a single +1/+1 counter on the
///   creature returned by <paramref name="plusOneTargetResolver"/> via
///   <see cref="Fx.PlaceCounter"/>. "Up to one" means zero targets is legal —
///   a null / off-battlefield resolver result no-ops while the loyalty change
///   still applies.
/// - <b>−3: Target creature gains flying and double strike until end of turn
///   (CR 606 + CR 613.1 layer 6 + CR 514.2)</b>: registers two
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>s (Flying, Double strike) on
///   the target's <see cref="Permanent.ActiveEffects"/>. Both expire at cleanup.
///   No-ops if the resolver yields null, an off-battlefield creature, or a
///   creature with no continuous-effects service wired.
/// - <b>−8: Create X 2/2 white Cat creature tokens, X = your life total
///   (CR 606 + CR 111)</b>: mints <see cref="Player.LifeTotal"/> 2/2 white Cat
///   tokens onto the controller's battlefield via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. X is read at resolution
///   (CR 608.2 — once, as the ability resolves). A non-positive life total
///   creates no tokens.
///
/// ## Deferred (v1 gaps)
/// - <b>Target prompts</b>: <see cref="LoyaltyAbility"/> doesn't declare
///   <see cref="Majik.Core.Targeting.TargetRequest"/>s; the +1 and −3 creature
///   targets are picked from the supplied resolvers rather than via the agent.
///   Same gap Chandra / Teferi / Karn / Liliana share.
/// - <b>Token-doubling replacement</b>: the −8 mints tokens directly rather
///   than routing through the <see cref="TokenCreationIntent"/> replacement bus,
///   so Doubling Season / Anointed Procession don't double the Cats in v1.
/// </summary>
[CardName("Ajani, Caller of the Pride")]
public static class AjaniCallerOfThePrideFactory
{
    public const string CardName = "Ajani, Caller of the Pride";
    public const string Slug = "ajani-caller-of-the-pride";
    public const int StartingLoyalty = 4;

    /// <summary>Granted keyword — CR 702.9 Flying.</summary>
    public const string GrantedFlying = "Flying";

    /// <summary>Granted keyword — CR 702.4 Double strike.</summary>
    public const string GrantedDoubleStrike = "Double strike";

    /// <summary>−8 token shape: 2/2 white Cat.</summary>
    public const int TokenPower = 2;
    public const int TokenToughness = 2;

    /// <summary>
    /// Construct Ajani with no resolvers / zone service wired — the +1 and −3
    /// clauses no-op (no target), the −8 mints X Cats via the controller's own
    /// battlefield zones. Loyalty changes still apply. Suitable for shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, plusOneTargetResolver: null, minusThreeTargetResolver: null,
            zones: null);

    /// <summary>
    /// Construct Ajani, Caller of the Pride.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="plusOneTargetResolver">Returns the "up to one target
    /// creature" for the +1 counter clause. May return null (legal — "up to
    /// one") or be null (the clause no-ops).</param>
    /// <param name="minusThreeTargetResolver">Returns the "target creature" the
    /// −3 grants Flying + Double strike to. May be null — the clause
    /// no-ops.</param>
    /// <param name="zones">ZoneService used to mint the −8 Cat tokens so
    /// CardMovedEvent fires (Soul Warden etc.). May be null — tokens enter via
    /// the controller's own battlefield zone directly.</param>
    public static Planeswalker Create(
        Player owner,
        Func<Creature?>? plusOneTargetResolver,
        Func<Creature?>? minusThreeTargetResolver,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Planeswalker — Ajani, {1}{W}{W}, loyalty 4). The JSON carries no
        // abilities — the three loyalty abilities are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var ajani = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        // -- +1: Put a +1/+1 counter on up to one target creature. -------------
        // CR 606 (loyalty) + CR 122 (counters) + CR 115.1b ("up to one" — zero
        // is a legal number of targets). v1 picks the creature from the
        // resolver; a null result is the legal zero-target choice.
        ajani.AddAbility(new LoyaltyAbility(ajani, +1, () =>
        {
            var target = plusOneTargetResolver?.Invoke();
            if (target == null) return;
            if (target.Zone != ZoneType.Battlefield) return;
            Fx.PlaceCounter(target, CounterType.PlusOnePlusOne);
        }));

        // -- −3: Target creature gains flying and double strike until end of
        //    turn. -------------------------------------------------------------
        // CR 606 (loyalty) + CR 613.1c layer 6 (keyword grant) + CR 514.2
        // (cleanup expiry). Both grants register on the target's
        // ContinuousEffectsService; without one wired the clause no-ops.
        ajani.AddAbility(new LoyaltyAbility(ajani, -3, () =>
        {
            var target = minusThreeTargetResolver?.Invoke();
            if (target == null) return;
            if (target.Zone != ZoneType.Battlefield) return;
            if (target.ActiveEffects == null) return;

            target.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(target, GrantedFlying));
            target.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(target, GrantedDoubleStrike));
        }));

        // -- −8: Create X 2/2 white Cat creature tokens, X = your life total. --
        // CR 606 (loyalty) + CR 111 (token creation). X is read at resolution
        // (CR 608.2). A non-positive life total mints nothing.
        ajani.AddAbility(new LoyaltyAbility(ajani, -8, () =>
        {
            var controller = ajani.Controller ?? owner;
            var x = controller.LifeTotal;
            if (x <= 0) return;

            var spec = new TokenFactory.TokenSpec(
                Name: "Cat",
                Power: TokenPower,
                Toughness: TokenToughness,
                Subtypes: new[] { CardSubtype.Cat },
                Keywords: null,
                Colors: new[] { ManaColor.White });

            for (var i = 0; i < x; i++)
            {
                TokenFactory.CreateOnBattlefield(spec, controller, zones);
            }
        }));

        return ajani;
    }
}
