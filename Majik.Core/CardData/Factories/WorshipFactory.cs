using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Worship (Tempest, {2}{W}).
///
/// Enchantment. Oracle text:
///   "If you control a creature, damage that would reduce your life total
///    to less than 1 reduces it to 1 instead."
///
/// ## Implemented (v1)
/// - Enchantment {2}{W}, owner/controller wired.
/// - <b>CR 614 replacement</b> on <see cref="DamageIntent"/> targeting
///   Worship's controller (<see cref="DamageIntent.TargetPlayer"/>). When
///   the controller currently controls one or more creatures AND the
///   incoming damage would reduce their life total below 1, the intent's
///   <see cref="DamageIntent.Amount"/> is rewritten to
///   <c>max(0, Life - 1)</c> — i.e. the smallest amount that leaves the
///   controller at exactly 1 life. Wired via
///   <see cref="WorshipDamageReplacement"/> registered on the supplied
///   <see cref="ReplacementBus"/>; the replacement self-gates on Worship
///   being on the battlefield (LTB lifts the rider naturally — same
///   pattern as <see cref="SoulScarMageDamageReplacement"/>).
///
/// ## CR alignment
/// - <b>CR 614.1b</b>: "would reduce ... reduces it to 1 instead" is a
///   classic "instead" replacement. Worship rewrites the incoming amount
///   so callers commit the safe-cap value via the existing
///   <see cref="ReplacementBus"/> flow — no separate "damage prevented"
///   event is published (matches the rest of the prevention family —
///   PreventAllDamage*, DamageHalveRoundedUpReplacement, etc.).
/// - <b>CR 119.3</b>: damage to a player causes them to lose that much
///   life. Worship inspects current <see cref="Player.LifeTotal"/> at the
///   moment of replacement; if life is already at or below 1 the
///   replacement rewrites Amount to 0 (life clamped at 1 means "no
///   further damage can reduce it"). When life is &gt;= 1 the new Amount
///   is <c>Life - 1</c> so the post-damage total lands at exactly 1.
/// - <b>"If you control a creature"</b> is evaluated at the moment of
///   replacement (CR 614.6 — the replacement effect checks its conditions
///   when it would apply, not when it was created). Includes Worship
///   itself only if it's a Creature (it isn't), so the printed text's
///   "creature" carve-out is honoured — when Worship's controller has no
///   creatures, the replacement skips and lethal damage resolves normally.
///   This matches the famous "kill Worship's controller by removing the
///   creature, then attacking" pattern.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape-only. No replacement registered.
///   Suitable for dispatcher / structural tests. Mirrors
///   <see cref="HardenedScalesFactory.Create(Player)"/>.
/// - <see cref="Create(Player, ReplacementBus?)"/> — when a bus is
///   supplied, the <see cref="WorshipDamageReplacement"/> is registered.
///
/// ## Deferred (v1 gaps)
/// - <b>Damage from sources not routed through ReplacementBus</b>: ability
///   "ping" damage (Walking Ballista, Goblin Bombardment, etc.) currently
///   calls <c>player.LoseLife</c> / <c>creature.TakeDamage</c> directly
///   without publishing a <see cref="DamageIntent"/>. Worship only catches
///   damage that flows through the bus — same coverage gap as
///   <see cref="SoulScarMageFactory"/>'s noncombat rider and the Fog
///   shield. When ability damage starts routing through the bus the
///   replacement picks it up without further changes.
/// - <b>Replacement ordering</b>: when Worship overlaps with another
///   damage-replacement effect (Leyline of Sanctity for spell-only damage,
///   Phyresis-style poison redirects), CR 616.1 lets the affected player
///   choose order. <see cref="ReplacementBus"/> currently applies in
///   registration order — same simplification as every other replacement
///   here.
/// - <b>"Reduce to less than 1" precision</b>: today damage to a player
///   is straight life-loss with no source-side floor or 0-life-prevention
///   shield (life going to 0 = lose game, CR 704.5a). Worship's cap is
///   computed against <see cref="Player.LifeTotal"/> at replacement time;
///   composite "this damage and that damage" intents aren't a thing in v1
///   (each <see cref="DamageIntent"/> represents one source's intent), so
///   ordering matters — first-in wins.
/// </summary>
[CardName("Worship")]
public static class WorshipFactory
{
    public const string CardName = "Worship";
    public const string PrintedManaCost = "{2}{W}";

    /// <summary>
    /// Construct a Worship card with no live wiring. Shape-only —
    /// suitable for dispatcher / structural tests. Mirrors the no-arg
    /// posture on <see cref="HardenedScalesFactory.Create(Player)"/>.
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct a Worship card with optional replacement-bus wiring.
    /// When <paramref name="replacements"/> is supplied, a
    /// <see cref="WorshipDamageReplacement"/> is registered so the
    /// "reduces it to 1 instead" cap fires on every
    /// <see cref="DamageIntent"/> targeting Worship's controller while
    /// they control a creature.
    /// </summary>
    public static Enchantment Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        if (replacements != null)
        {
            replacements.Register<DamageIntent>(new WorshipDamageReplacement(card));
        }

        return card;
    }
}

/// <summary>
/// Replacement effect for Worship's "damage that would reduce your life
/// total to less than 1 reduces it to 1 instead" clause. Reads every
/// <see cref="DamageIntent"/> from the <see cref="ReplacementBus"/> and
/// caps the amount when:
///
///   - Worship is on the battlefield (CR 614.6 — replacement is only
///     active while the printed source is in the right zone).
///   - <see cref="DamageIntent.TargetPlayer"/> is Worship's controller
///     (the printed "you / your life total" clause).
///   - Worship's controller currently controls at least one
///     <see cref="Creature"/> on the battlefield (the printed "if you
///     control a creature" gate).
///   - The incoming damage would reduce the controller's life total
///     below 1 (the printed "to less than 1" gate; if life - amount is
///     already &gt;= 1 no replacement is needed).
///
/// On match the intent's <see cref="DamageIntent.Amount"/> is rewritten
/// to <c>max(0, Life - 1)</c> — leaving the controller at exactly 1 life
/// after the caller commits the damage.
/// </summary>
public sealed class WorshipDamageReplacement : IReplacementEffect<DamageIntent>
{
    private readonly Enchantment _source;

    public WorshipDamageReplacement(Enchantment source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    /// <summary>The Worship instance this replacement is keyed to.</summary>
    public Enchantment Source => _source;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history)
    {
        // CR 614.6 — only active while Worship is on the battlefield.
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (intent.Amount <= 0) return false;

        var controller = _source.Controller;
        if (controller is null) return false;

        // Only intercept damage to Worship's controller.
        if (!ReferenceEquals(intent.TargetPlayer, controller)) return false;

        // "If you control a creature" — printed gate. No creatures => no
        // protection, lethal damage resolves normally (the famous Worship
        // play pattern — remove the creature first, then swing for game).
        var hasCreature = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any();
        if (!hasCreature) return false;

        // "to less than 1" — only fires when the incoming damage would
        // drop life below 1. If post-damage life is already >= 1, no
        // replacement needed (also keeps history-replay idempotent).
        if (controller.LifeTotal - intent.Amount >= 1) return false;

        return true;
    }

    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history)
    {
        var controller = _source.Controller;
        if (controller is null) return intent;

        // Cap to whatever amount leaves the controller at exactly 1 life.
        // If they're already at or below 1, rewrite to 0 (no further
        // reduction possible). CR 119.3 — life loss commits at the caller
        // site after replacement; the cap is computed against the
        // current LifeTotal snapshot.
        var allowed = Math.Max(0, controller.LifeTotal - 1);
        return intent with { Amount = allowed };
    }
}
