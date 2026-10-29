namespace CarXModManager.Logic;

/// <summary>What the caller should do after <see cref="PageLoader.Tick"/>.</summary>
internal enum PageAction
{
    /// <summary>Nothing to do now.</summary>
    Wait,
    /// <summary>Ask the game for the next page of rooms now.</summary>
    Request,
    /// <summary>No more rooms arrived after the last request: every room is assumed to be loaded.</summary>
    FinishedNoMoreRooms,
    /// <summary>The safety limit on extra pages was reached.</summary>
    FinishedPageLimit,
}

/// <summary>
/// Decides when to ask the game for the next page of rooms. The game loads a page only when the list is scrolled to the
/// bottom, and gives no sign that the last page has been reached; this keeps asking, one page at a time, until a request
/// brings nothing new within a timeout. A page that arrives late restarts it, and a shorter list (the game refreshed or its
/// server filters changed) starts over from page one.
/// Pure code (time is passed in), so it is covered by the test project.
/// </summary>
internal sealed class PageLoader
{
    private readonly float _timeoutSeconds;
    private readonly float _gapSeconds;
    private int _knownTotal;
    private int _totalAtRequest;
    private float _lastListedAt;
    private float _waitingSince = -1f;

    /// <summary>Creates a loader.</summary>
    /// <param name="timeoutSeconds">How long to wait for a requested page before deciding there is no more.</param>
    /// <param name="gapSeconds">Least time between the game listing rooms and the next request.</param>
    public PageLoader(float timeoutSeconds = 3f, float gapSeconds = 0.35f)
    {
        _timeoutSeconds = timeoutSeconds;
        _gapSeconds = gapSeconds;
    }

    /// <summary>True once every room is assumed to be loaded (or the limit was reached). Cleared when more rooms arrive.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>How many extra pages were requested since the last reset.</summary>
    public int PagesRequested { get; private set; }

    /// <summary>Starts over, for example when the server browser opens.</summary>
    public void Reset()
    {
        _knownTotal = 0;
        _totalAtRequest = 0;
        _lastListedAt = 0f;
        _waitingSince = -1f;
        IsComplete = false;
        PagesRequested = 0;
    }

    /// <summary>The game listed <paramref name="total"/> rooms (all pages so far) at time <paramref name="now"/>.</summary>
    public void OnListed(int total, float now)
    {
        if (total < _knownTotal)
        {
            // shorter than before: the game refreshed, so start loading from page one again
            PagesRequested = 0;
            IsComplete = false;
        }
        else if (total > _knownTotal)
        {
            IsComplete = false; // more rooms arrived: a page we asked for, or the player scrolled
        }
        if (total > _totalAtRequest) _waitingSince = -1f; // the page we were waiting for came
        _knownTotal = total;
        _lastListedAt = now;
    }

    /// <summary>Forgets a completed load so the next tick asks for pages again (for when the player switches auto-loading back on).</summary>
    public void Restart()
    {
        IsComplete = false;
        PagesRequested = 0;
        _waitingSince = -1f;
    }

    /// <summary>
    /// Call every frame. Returns <see cref="PageAction.Request"/> when a page should be requested now (the request is then
    /// counted as made).
    /// </summary>
    /// <param name="now">The current time in seconds.</param>
    /// <param name="canRequest">False while the game is loading, refreshing or empty, when asking would be pointless.</param>
    /// <param name="maxPages">The most extra pages to request.</param>
    public PageAction Tick(float now, bool canRequest, int maxPages)
    {
        if (IsComplete || !canRequest) return PageAction.Wait;
        if (PagesRequested >= maxPages)
        {
            IsComplete = true;
            return PageAction.FinishedPageLimit;
        }
        if (_waitingSince >= 0f)
        {
            if (now - _waitingSince < _timeoutSeconds) return PageAction.Wait;
            IsComplete = true;
            _waitingSince = -1f;
            return PageAction.FinishedNoMoreRooms;
        }
        if (now - _lastListedAt < _gapSeconds) return PageAction.Wait;

        PagesRequested++;
        _waitingSince = now;
        _totalAtRequest = _knownTotal;
        return PageAction.Request;
    }
}
