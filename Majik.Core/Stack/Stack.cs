using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;

namespace Majik.Core.Stack;

/// <summary>
/// Manages the spell/ability stack.
/// Implements LIFO (Last In, First Out) structure per Magic rules.
/// </summary>
public class Stack
{
    private readonly System.Collections.Generic.Stack<IStackObject> _objects = new();
    // Parallel to _objects: the id of each stack entry. Equals the object's Id,
    // except for a repeat push of an object already on the stack (one triggered
    // ability that fired twice, CR 603.3), which gets a StackEntryIds.Repeat id.
    private readonly System.Collections.Generic.Stack<Guid> _entryIds = new();
    private readonly List<CastInProgress> _beingCast = new();

    /// <summary>
    /// CR 601.2a — spells being cast: moved to the stack, not yet pushed
    /// (see <see cref="CastInProgress"/>), oldest first. Display data for
    /// snapshots only; not part of <see cref="GetAll"/>.
    /// </summary>
    public IReadOnlyList<CastInProgress> BeingCast => _beingCast.ToArray();

    internal CastInProgress BeginCast(ICard card, Player caster, Guid id)
    {
        var cast = new CastInProgress(card, caster, id);
        _beingCast.Add(cast);
        return cast;
    }

    internal CastInProgress? FindCast(ICard card) =>
        _beingCast.LastOrDefault(c => ReferenceEquals(c.Card, card));

    internal void EndCast(ICard card) => _beingCast.RemoveAll(c => ReferenceEquals(c.Card, card));
    private readonly IEventBus? _eventBus;

    /// <summary>
    /// Whether the stack is empty.
    /// </summary>
    public bool IsEmpty => _objects.Count == 0;

    /// <summary>
    /// Number of objects on the stack.
    /// </summary>
    public int Count => _objects.Count;

    /// <summary>
    /// The top object on the stack (most recently added).
    /// </summary>
    public IStackObject? Top => _objects.Count > 0 ? _objects.Peek() : null;

    public Stack(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>
    /// The controller of the stack object that is CURRENTLY resolving, set by
    /// the resolution entry points (<see cref="Majik.Core.Services.StackResolver"/>,
    /// <see cref="Majik.Core.Abilities.TriggeredAbility.ResolveAsync"/>,
    /// <see cref="Majik.Core.Abilities.ActivatedAbility.ResolveAsync"/>) for the
    /// duration of that object's resolution. Read by
    /// <see cref="PublishSpellCountered"/> so a "counter" effect run during
    /// resolution can attribute the counter to "a spell or ability you
    /// control" (Baral, Chief of Compliance) without every counter caller
    /// having to thread the countering controller explicitly. Null when no
    /// object is mid-resolution.
    /// </summary>
    public Player? CurrentResolutionController { get; set; }

    /// <summary>
    /// The game's zone service, when the stack belongs to one. A spell's card
    /// leaving the stack moves through it (<see cref="Majik.Core.Spells.SpellStackExit"/>)
    /// so its zone lists, CardMovedEvent and zone-change replacements stay in
    /// step. Null for a bare test stack: the move then only updates the lists.
    /// </summary>
    public Majik.Core.Services.ZoneService? ZoneService { get; internal set; }

    /// <summary>
    /// The stack object StackResolver is resolving right now, and the stack
    /// entry id it had. Null when nothing is mid-resolution through the
    /// resolver. Read by <see cref="PublishSpellCountered"/> to name what
    /// countered a spell (SpellCounteredEvent.CounteringObject).
    /// </summary>
    public IStackObject? CurrentlyResolving { get; internal set; }

    /// <inheritdoc cref="CurrentlyResolving"/>
    public Guid? CurrentlyResolvingEntryId { get; internal set; }

    /// <summary>
    /// CR 701.5 — announce that <paramref name="spell"/> was countered. Fires
    /// <see cref="Majik.Core.Domain.DomainEvents.SpellCounteredEvent"/> on the
    /// stack's event bus, attributing the counter to
    /// <see cref="CurrentResolutionController"/> (the controller of the
    /// spell/ability that is resolving and performed the counter). Called from
    /// the single counter chokepoint
    /// (<see cref="Majik.Core.CardData.OracleSpellBinder.RemoveFromStack"/>).
    /// </summary>
    internal void PublishSpellCountered(Majik.Core.Spells.ISpell spell)
    {
        if (spell is null) return;
        _eventBus?.Publish(
            new Majik.Core.Domain.DomainEvents.SpellCounteredEvent(
                spell, CurrentResolutionController)
            {
                CounteringObject = CurrentlyResolving,
                CounteringEntryId = CurrentlyResolving is null ? null : CurrentlyResolvingEntryId,
            });
    }

    /// <summary>
    /// Add an object to the top of the stack.
    /// </summary>
    public void Push(IStackObject stackObject)
    {
        if (stackObject == null)
        {
            throw new ArgumentNullException(nameof(stackObject));
        }

        var entryId = NextEntryId(stackObject);
        _objects.Push(stackObject);
        _entryIds.Push(entryId);
        _eventBus?.Publish(new StackObjectAddedEvent(stackObject) { EntryId = entryId });
    }

    /// <summary>
    /// The entry id of the top of the stack (see <see cref="GetEntryIds"/>),
    /// or null when the stack is empty.
    /// </summary>
    public Guid? TopEntryId => _entryIds.Count > 0 ? _entryIds.Peek() : null;

    /// <summary>
    /// The id of each stack entry, BOTTOM to TOP, parallel to
    /// <see cref="GetAll"/>. CR 603.3 — each firing of a triggered ability is
    /// its own stack object: the engine pushes the same ability object once per
    /// firing, so a repeat copy gets a <see cref="StackEntryIds.Repeat"/> id
    /// (the same scheme the order-triggers prompt uses) instead of sharing the
    /// first copy's id. Every other entry's id is its object's
    /// <see cref="IStackObject.Id"/>. Identity only: resolution still reads the
    /// shared object.
    /// </summary>
    public IReadOnlyList<Guid> GetEntryIds() => _entryIds.ToArray().Reverse().ToList().AsReadOnly();

    private Guid NextEntryId(IStackObject stackObject)
    {
        var copies = 0;
        foreach (var o in _objects) if (ReferenceEquals(o, stackObject)) copies++;
        if (copies == 0) return stackObject.Id;
        var used = new HashSet<Guid>(_entryIds);
        var id = stackObject.Id;
        for (var salt = copies; used.Contains(id); salt++) id = StackEntryIds.Repeat(stackObject.Id, salt);
        return id;
    }

    /// <summary>
    /// Remove and return the top object from the stack.
    /// </summary>
    public IStackObject? Pop()
    {
        if (_objects.Count == 0)
        {
            return null;
        }

        _entryIds.Pop();
        return _objects.Pop();
    }

    /// <summary>
    /// Get all objects on the stack from BOTTOM to TOP: index 0 is the first
    /// object put on the stack, the last element is the top (the object that
    /// resolves next, CR 608.1). Returns a read-only list for encapsulation.
    /// </summary>
    public IReadOnlyList<IStackObject> GetAll()
    {
        // System.Collections.Generic.Stack.ToArray() is top-first; reversed = bottom-first.
        return _objects.ToArray().Reverse().ToList().AsReadOnly();
    }

    /// <summary>
    /// Clear all objects from the stack.
    /// </summary>
    public void Clear()
    {
        _objects.Clear();
        _entryIds.Clear();
        _eventBus?.Publish(new StackClearedEvent());
    }

    /// <summary>
    /// Simulation-only: build a new Stack whose objects are remapped clones of
    /// the objects in <paramref name="src"/>, using <paramref name="cardMap"/>
    /// (InstanceId → cloned ICard) and <paramref name="playerMap"/> (original
    /// Player → cloned Player) to redirect source-card and target references.
    ///
    /// Only <see cref="Majik.Core.Spells.Spell"/> stack objects are cloned;
    /// <see cref="Majik.Core.Abilities.ActivatedAbility"/> and
    /// <see cref="Majik.Core.Abilities.TriggeredAbility"/> carry effect closures
    /// that captured the original game state and cannot be safely remapped —
    /// those objects are silently dropped from the cloned stack.
    ///
    /// LIFO order is preserved: the object that was on top of <paramref name="src"/>
    /// is also on top of the returned stack.
    /// </summary>
    internal static Stack CloneFrom(
        Stack src,
        IReadOnlyDictionary<Guid, ICard> cardMap,
        IReadOnlyDictionary<Player, Player> playerMap)
    {
        var clone = new Stack(eventBus: null);   // sim stack — no event bus

        // GetAll() returns bottom-to-top; push in that order so the original
        // bottom ends up at the bottom and the original top ends up on top.
        foreach (var obj in src.GetAll())
        {
            if (obj is not Majik.Core.Spells.Spell spell)
            {
                // Activated/triggered abilities carry captured closures over
                // original game objects — cannot be safely remapped.
                // Skip silently (documented on this method).
                continue;
            }

            // Remap source card: look up the clone by InstanceId.
            var clonedCard = cardMap.TryGetValue(spell.Card.InstanceId, out var cc)
                ? cc
                : spell.Card;   // fallback: card not in any zone (defensive)

            // Remap controller.
            var clonedController = playerMap.TryGetValue(spell.Controller, out var cp)
                ? cp
                : spell.Controller;

            // Remap targets: for each ITarget, rebuild a new Target pointing
            // at the cloned object.  Supported target types: Player, Permanent,
            // Card.  Unknown / Spell-on-stack / Ability targets are carried
            // as-is (they reference stack objects which are also being rebuilt
            // — cross-stack references are an edge case the sim doesn't handle
            // in v1).
            var remappedTargets = spell.Targets
                .Select(t => RemapTarget(t, cardMap, playerMap))
                .ToList();

            var clonedSpell = new Majik.Core.Spells.Spell(
                card: clonedCard,
                controller: clonedController,
                targets: remappedTargets);

            // Preserve the original Spell.Id so snapshot equality holds:
            // the stack DTO emits spell.Id as the stack-object key.
            clonedSpell.Id = spell.Id;

            // Copy boolean stamps (each defaults to false on a fresh Spell, so
            // only set when the original had it set).
            if (spell.WasFreeCast)         clonedSpell.WasFreeCast         = true;
            if (spell.WasCastForEscape)    clonedSpell.WasCastForEscape    = true;
            if (spell.WasCastFromSuspend)  clonedSpell.WasCastFromSuspend  = true;
            if (spell.WasCastFromHand)     clonedSpell.WasCastFromHand     = true;
            if (spell.WasCastFromLibrary)  clonedSpell.WasCastFromLibrary  = true;
            if (spell.WasCastFromGraveyard) clonedSpell.WasCastFromGraveyard = true;
            if (spell.WasKicked)           clonedSpell.WasKicked           = true;
            if (spell.CannotBeCountered)   clonedSpell.CannotBeCountered   = true;
            if (spell.IsCopy)              clonedSpell.IsCopy              = true;
            clonedSpell.TimesKicked = spell.TimesKicked;
            clonedSpell.TotalManaSpentThisCast = spell.TotalManaSpentThisCast;
            if (spell.PostResolutionZoneOverride.HasValue)
                clonedSpell.PostResolutionZoneOverride = spell.PostResolutionZoneOverride;
            if (spell.GiftRecipient != null && playerMap.TryGetValue(spell.GiftRecipient, out var gr))
                clonedSpell.GiftRecipient = gr;

            // Remap raw ChosenTargets (flat list of objects used by resolution).
            foreach (var raw in spell.ChosenTargets)
                clonedSpell.ChosenTargets.Add(RemapRawTarget(raw, cardMap, playerMap));

            clone._objects.Push(clonedSpell);
            clone._entryIds.Push(clonedSpell.Id);
        }

        // _objects was pushed bottom-to-top, giving us top-to-bottom internal
        // order (System.Stack is LIFO).  But we pushed in GetAll() order which
        // is bottom-to-top, so the last push was the original top — which is
        // now Peek()/Top of the clone.  Correct.
        return clone;
    }

    private static ITarget RemapTarget(
        ITarget t,
        IReadOnlyDictionary<Guid, ICard> cardMap,
        IReadOnlyDictionary<Player, Player> playerMap)
    {
        if (t is not Target concreteTarget) return t;

        return concreteTarget.TargetType switch
        {
            TargetType.Player when concreteTarget.GetPlayer() is { } origPlayer =>
                playerMap.TryGetValue(origPlayer, out var clonePlayer)
                    ? Target.Player(clonePlayer)
                    : t,

            TargetType.Permanent when concreteTarget.GetPermanent() is { } origPerm =>
                cardMap.TryGetValue(origPerm.InstanceId, out var cloneCard)
                && cloneCard is Majik.Core.Cards.Permanent clonePerm
                    ? Target.Permanent(clonePerm)
                    : t,

            TargetType.Card when concreteTarget.GetCard() is { } origCard =>
                cardMap.TryGetValue(origCard.InstanceId, out var cloneCard2)
                    ? Target.Card(cloneCard2)
                    : t,

            _ => t   // Spell-on-stack / Ability targets: carry as-is
        };
    }

    private static object RemapRawTarget(
        object raw,
        IReadOnlyDictionary<Guid, ICard> cardMap,
        IReadOnlyDictionary<Player, Player> playerMap)
    {
        if (raw is Player p && playerMap.TryGetValue(p, out var cp2)) return cp2;
        if (raw is Majik.Core.Cards.Card c && cardMap.TryGetValue(c.InstanceId, out var cc2)) return cc2;
        return raw;
    }
}
