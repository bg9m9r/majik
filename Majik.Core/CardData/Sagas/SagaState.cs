using Majik.Core.Abilities;
using Majik.Core.Counters;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Sagas;

/// <summary>
/// CR 714 — Saga state tracking. A Saga enters with zero lore counters;
/// at the controller's pre-combat main beginning, add a lore counter and
/// trigger the chapter whose count matches the new total (CR 714.2b — a
/// chapter ability is a triggered ability that uses the stack). After the
/// final chapter triggers and resolves, SBA puts the Saga into its owner's
/// graveyard (CR 714.5 / 704.5r).
///
/// ## Chapter abilities on the stack (CR 714.2b)
/// When a <see cref="Abilities.TriggerManager"/> is supplied, the chapter
/// ability does NOT resolve synchronously when the lore counter reaches the
/// chapter number. Instead, <see cref="AdvanceAndChapter"/> builds a
/// <see cref="TriggeredAbility"/> wrapping the per-chapter effect and enqueues
/// it onto the trigger manager's pending queue (<see
/// cref="TriggerManager.EnqueuePending"/>). The engine's normal priority loop
/// then drains it onto the stack the next time a player would receive priority
/// (CR 603.3), so an opponent gets a priority window to respond — cast an
/// instant, activate an ability — BEFORE the chapter resolves (e.g. before a
/// transforming Saga's chapter III flips). <see cref="ChapterTriggerOnStack"/>
/// is held true from the moment the ability is enqueued until it resolves, so
/// the Saga-sacrifice SBA (CR 704.5r) defers across the priority window.
///
/// When no trigger manager is supplied (shape / unit tests that drive
/// <see cref="AdvanceAndChapter"/> directly), the chapter effect runs
/// synchronously, preserving the legacy behaviour.
/// </summary>
public sealed class SagaState
{
    private readonly Majik.Core.Cards.Permanent _source;
    private readonly int _finalChapter;
    private readonly Action<int>? _onChapter;
    private readonly TriggerManager? _triggers;

    public SagaState(Majik.Core.Cards.Permanent source, int finalChapter,
        Action<int>? onChapter = null, TriggerManager? triggers = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (finalChapter < 1) throw new ArgumentOutOfRangeException(nameof(finalChapter));
        _finalChapter = finalChapter;
        _onChapter = onChapter;
        _triggers = triggers;
    }

    public int LoreCounters => _source.Counters.Count(CounterType.Loyalty); // reused enum slot; future: Lore type
    public int FinalChapter => _finalChapter;

    /// <summary>CR 714.5 — true while a chapter-ability trigger from this saga
    /// is still on the stack (or pending). Held from the moment the chapter
    /// ability is enqueued (CR 714.2b) until it resolves; SBA defers the
    /// sacrifice (CR 704.5r) while true.</summary>
    public bool ChapterTriggerOnStack { get; set; }

    /// <summary>CR 714.2 — at beginning of pre-combat main, add a lore counter
    /// and fire the chapter trigger for the new count. When a
    /// <see cref="TriggerManager"/> was supplied the chapter ability is routed
    /// through the stack (CR 714.2b — it can be responded to); otherwise the
    /// chapter effect runs synchronously.</summary>
    public int AdvanceAndChapter()
    {
        _source.Counters.Add(CounterType.Loyalty, 1);
        var chapter = LoreCounters;

        if (_triggers != null)
        {
            EnqueueChapterTrigger(chapter);
        }
        else
        {
            _onChapter?.Invoke(chapter);
        }

        return chapter;
    }

    /// <summary>CR 714.2b / 603.3 — build the chapter ability as a triggered
    /// ability whose effect runs the per-chapter body, and place it on the
    /// pending queue so the engine drains it onto the stack with a priority
    /// window. <see cref="ChapterTriggerOnStack"/> is set now and cleared when
    /// the ability resolves (so the Saga-sacrifice SBA defers in between).</summary>
    private void EnqueueChapterTrigger(int chapter)
    {
        var controller = _source.Controller ?? _source.Owner!;

        ChapterTriggerOnStack = true;

        var effect = new Effect(
            $"Saga chapter {chapter} ({_source.Name})",
            () =>
            {
                _onChapter?.Invoke(chapter);
                // CR 714.5 — chapter resolved; release the SBA defer so a
                // completed Saga can now be sacrificed (CR 704.5r). For a
                // transforming Saga the chapter handler already cleared
                // SagaState, so this assignment lands on a detached state and
                // is harmless.
                ChapterTriggerOnStack = false;
            });

        var ability = new TriggeredAbility(
            source: _source,
            controller: controller,
            condition: Triggers.Never(),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        _triggers!.EnqueuePending(ability);
    }

    /// <summary>CR 714.5 / 704.5r — Saga with lore counter == final and no
    /// chapter trigger on stack should be sacrificed.</summary>
    public bool ShouldBeSacrificed() =>
        LoreCounters >= _finalChapter && !ChapterTriggerOnStack;
}
