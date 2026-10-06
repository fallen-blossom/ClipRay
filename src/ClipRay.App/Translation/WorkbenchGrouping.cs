using ClipRay.Core.Geometry;

namespace ClipRay.App.Translation;

public sealed partial class TranslationWorkbenchSession
{
    private sealed record GroupChange(IReadOnlyList<WorkbenchRegion> Before, IReadOnlyList<WorkbenchRegion> After);
    private readonly Stack<GroupChange> _groupUndo = new();
    private readonly Stack<GroupChange> _groupRedo = new();
    public bool CanUndoGrouping { get { lock (_sync) return !_closed && _groupUndo.Count > 0; } }
    public bool CanRedoGrouping { get { lock (_sync) return !_closed && _groupRedo.Count > 0; } }

    public IReadOnlyList<WorkbenchRegion> PreviewMerge(IReadOnlyList<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        lock (_sync) { ThrowIfClosed(); return ValidateMerge(ids); }
    }

    private WorkbenchRegion[] ValidateMerge(IReadOnlyList<Guid> ids)
    {
        if (ids.Count < 2 || ids.Distinct().Count() != ids.Count)
            throw new ArgumentException("Select at least two distinct regions.", nameof(ids));
        var selected = ids.Select(id => _regions.TryGetValue(id, out var region) ? region
            : throw new ArgumentException("A selected region no longer exists.", nameof(ids)))
            .OrderBy(region => region.Original.Bounds.Top).ThenBy(region => region.Original.Bounds.Left).ToArray();
        if (selected.Any(region => region.SourceLanguage != selected[0].SourceLanguage
            || region.TargetLanguage != selected[0].TargetLanguage || region.Route != selected[0].Route))
            throw new InvalidOperationException("Selected regions must use the same languages and route.");
        return selected;
    }

    public Guid Merge(IReadOnlyList<Guid> ids)
    {
        var pending = new List<CancellationTokenSource>();
        Guid id;
        lock (_sync)
        {
            ThrowIfClosed();
            var before = ValidateMerge(ids);
            id = Guid.NewGuid();
            var left = before.Min(region => region.Original.Bounds.Left);
            var top = before.Min(region => region.Original.Bounds.Top);
            var right = before.Max(region => region.Original.Bounds.Right);
            var bottom = before.Max(region => region.Original.Bounds.Bottom);
            var original = new VisualTextRegion(id.ToString("N"), string.Join("\n", before.Select(region => region.Original.SourceText)),
                Array.AsReadOnly(before.SelectMany(region => region.Original.Lines).ToArray()),
                new PixelRect(left, top, right - left, bottom - top), VisualTextRole.Body, VisualStructureConfidence.Low)
            {
                SharesParentOcrEvidence = before.Any(region => region.Original.SharesParentOcrEvidence),
                OriginalOcrWords = Array.AsReadOnly(before.SelectMany(region => region.Original.OriginalOcrWords.Count > 0
                    ? region.Original.OriginalOcrWords : region.Original.Lines.SelectMany(line => line.Words)).Distinct().ToArray())
            };
            var merged = new WorkbenchRegion(id, original, string.Join("\n", before.Select(region => region.Draft)),
                before[0].SourceLanguage, before[0].TargetLanguage, before[0].Route, 0, ++_nextAttempt, WorkbenchRegionState.Modified);
            ReplaceGrouping(before, [merged], pending);
            _groupUndo.Push(new(before, [merged]));
            _groupRedo.Clear();
        }
        foreach (var operation in pending) CancelSafely(operation);
        Notify();
        return id;
    }

    public void UndoGrouping() => MoveGrouping(undo: true);
    public void RedoGrouping() => MoveGrouping(undo: false);

    public IReadOnlyList<WorkbenchRegion> PreviewSplit(Guid id, int firstLineCount, int draftOffset)
    {
        lock (_sync) { ThrowIfClosed(); return BuildSplit(_regions[id], firstLineCount, draftOffset); }
    }

    private static WorkbenchRegion[] BuildSplit(WorkbenchRegion parent, int firstLineCount, int draftOffset)
    {
        var lines = parent.Original.Lines;
        if (firstLineCount <= 0 || firstLineCount >= lines.Count) throw new ArgumentOutOfRangeException(nameof(firstLineCount));
        if (draftOffset <= 0 || draftOffset >= parent.Draft.Length
            || !System.Globalization.StringInfo.ParseCombiningCharacters(parent.Draft).Contains(draftOffset))
            throw new ArgumentOutOfRangeException(nameof(draftOffset));
        var drafts = new[] { parent.Draft[..draftOffset], parent.Draft[draftOffset..] };
        if (drafts.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Both split drafts must contain text.");
        return new[] { lines.Take(firstLineCount).ToArray(), lines.Skip(firstLineCount).ToArray() }
            .Select((part, index) =>
            {
                var id = Guid.NewGuid();
                var left = part.Min(line => line.Bounds.Left); var top = part.Min(line => line.Bounds.Top);
                var right = part.Max(line => line.Bounds.Right); var bottom = part.Max(line => line.Bounds.Bottom);
                var original = parent.Original with { Id = id.ToString("N"), SourceText = string.Join("\n", part.Select(line => line.Text)),
                    Lines = Array.AsReadOnly(part), Bounds = new(left, top, right - left, bottom - top),
                    Confidence = VisualStructureConfidence.Low, SharesParentOcrEvidence = true };
                // Keep parent recognition provenance: transformed lines may no longer
                // carry all of the original engine alternatives.
                return new WorkbenchRegion(id, original, drafts[index], parent.SourceLanguage, parent.TargetLanguage,
                    parent.Route, 0, 0, WorkbenchRegionState.Modified);
            }).ToArray();
    }

    public IReadOnlyList<Guid> Split(Guid id, int firstLineCount, int draftOffset)
    {
        var pending = new List<CancellationTokenSource>();
        WorkbenchRegion[] after;
        lock (_sync)
        {
            ThrowIfClosed();
            var before = _regions[id];
            after = BuildSplit(before, firstLineCount, draftOffset)
                .Select(region => region with { Attempt = ++_nextAttempt }).ToArray();
            ReplaceGrouping([before], after, pending);
            _groupUndo.Push(new([before], after));
            _groupRedo.Clear();
        }
        foreach (var operation in pending) CancelSafely(operation);
        Notify();
        return after.Select(region => region.Id).ToArray();
    }

    private void MoveGrouping(bool undo)
    {
        var pending = new List<CancellationTokenSource>();
        lock (_sync)
        {
            ThrowIfClosed();
            var from = undo ? _groupUndo : _groupRedo;
            var to = undo ? _groupRedo : _groupUndo;
            if (from.Count == 0) return;
            var change = from.Peek();
            var removed = undo ? change.After : change.Before;
            var current = removed.Select(region => _regions[region.Id]).ToArray();
            var restored = (undo ? change.Before : change.After).Select(region => region with
            {
                Attempt = ++_nextAttempt,
                State = region.State == WorkbenchRegionState.Translating ? WorkbenchRegionState.Cancelled : region.State
            }).ToArray();
            ReplaceGrouping(current, restored, pending);
            from.Pop();
            // Snapshot current edits/results before reversing the grouping, so redo
            // restores the user's merged draft rather than the original merge text.
            to.Push(undo ? new(restored, current) : new(current, restored));
        }
        foreach (var operation in pending) CancelSafely(operation);
        Notify();
    }

    private void ReplaceGrouping(IReadOnlyList<WorkbenchRegion> removed, IReadOnlyList<WorkbenchRegion> added,
        List<CancellationTokenSource> pending)
    {
        foreach (var region in removed)
        {
            _regions.Remove(region.Id);
            if (_active.Remove(region.Id, out var operation)) pending.Add(operation);
        }
        foreach (var region in added) _regions.Add(region.Id, region);
    }
}
