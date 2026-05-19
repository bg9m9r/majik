using Majik.Core.Counters;

namespace Majik.Core.CardData.Sagas;

/// <summary>
/// CR 714 — Saga state tracking. A Saga enters with zero lore counters;
/// at the controller's pre-combat main beginning, add a lore counter and
/// trigger the chapter whose count matches the new total. After the
/// final chapter triggers and resolves, SBA puts the Saga into its
/// owner's graveyard (CR 714.5 / 704.5r).
///
/// MVP keeps Sagas as a value-object helper consulted by phase code +
/// SBA; full integration with permanent lifecycle deferred.
/// </summary>
public sealed class SagaState
{
    private readonly Majik.Core.Cards.Permanent _source;
    private readonly int _finalChapter;
    private readonly Action<int>? _onChapter;

    public SagaState(Majik.Core.Cards.Permanent source, int finalChapter,
        Action<int>? onChapter = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (finalChapter < 1) throw new ArgumentOutOfRangeException(nameof(finalChapter));
        _finalChapter = finalChapter;
        _onChapter = onChapter;
    }

    public int LoreCounters => _source.Counters.Count(CounterType.Loyalty); // reused enum slot; future: Lore type
    public int FinalChapter => _finalChapter;

    /// <summary>CR 714.5 — true while a chapter-ability trigger from this saga
    /// is still on the stack. Engine sets/clears this around stack push/resolve;
    /// SBA defers the sacrifice while true.</summary>
    public bool ChapterTriggerOnStack { get; set; }

    /// <summary>CR 714.2 — at beginning of pre-combat main, add a lore counter
    /// and fire chapter trigger for the new count.</summary>
    public int AdvanceAndChapter()
    {
        _source.Counters.Add(CounterType.Loyalty, 1);
        var chapter = LoreCounters;
        _onChapter?.Invoke(chapter);
        return chapter;
    }

    /// <summary>CR 714.5 / 704.5r — Saga with lore counter == final and no
    /// chapter trigger on stack should be sacrificed.</summary>
    public bool ShouldBeSacrificed() =>
        LoreCounters >= _finalChapter && !ChapterTriggerOnStack;
}
