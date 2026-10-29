using BepInEx.Configuration;
using BepInEx.Logging;
using CarXModManager.Ui;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CarXModManager.Integrations;

/// <summary>What a screen rearranger changed, so it can be checked, put back and undone.</summary>
internal abstract class RearrangedState
{
    /// <summary>Describes what the arrangement was made for (which entries existed); a different value means the screen changed and is arranged again.</summary>
    public string Signature = string.Empty;

    /// <summary>How many times the game moved things back and they were put there again.</summary>
    public int Reapplies;
}

/// <summary>
/// The common behaviour of the integrations that rearrange one of the game's existing screens (the main menu, the Play menu): they have no
/// hooks; twice a second they look for the open screen, wait for it to settle, run their checks, rearrange it, verify the result, keep
/// verifying that the game has not moved things back, and undo everything if it cannot be made to hold. A subclass says how to find the
/// screen's pieces, what to check and how to move them; this class decides <em>when</em>:
/// <list type="bullet">
/// <item>nothing is touched until the screen has been open for half a second (the game may still be arranging or animating it);</item>
/// <item>a screen that is not ready (too few entries, hidden behind another) is waited for, which does not count as a failed try;</item>
/// <item>each opening of the screen gets three tries, with a growing pause, because a screen that is still sliding in can fail a check that
/// passes a moment later; the last failure is an error and shows on the status line;</item>
/// <item>after three openings whose every try failed, the screen is left alone for the rest of the session;</item>
/// <item>if the game moves the entries back, they are put back up to three times, after that the change is undone for good;</item>
/// <item>an exception is a bug, not a timing problem: the change is undone and the screen is left alone.</item>
/// </list>
/// The screen's original layout is written to <c>BepInEx/carx.modmanager.ui-NAME.txt</c> the first time it is ready, before anything changes.
/// </summary>
/// <typeparam name="TView">The screen's context component (found by looking for it in the scene).</typeparam>
/// <typeparam name="TState">What was changed on the screen.</typeparam>
internal abstract class ScreenRearranger<TView, TState> : IIntegration
    where TView : Component
    where TState : RearrangedState
{
    /// <summary>Seconds between looks at whether the screen is open.</summary>
    private const float PollSeconds = 0.5f;

    /// <summary>Seconds the screen must have been open before it is touched.</summary>
    private const float SettleSeconds = 0.5f;

    /// <summary>Tries each time the screen opens.</summary>
    private const int AttemptsPerOpening = 3;

    /// <summary>Times the game may move things back before the change is undone for good.</summary>
    private const int ReappliesBeforeGivingUp = 3;

    /// <summary>Openings of the screen whose every try failed, after which it is left alone for the rest of the session.</summary>
    private const int MaxFailedOpenings = 3;

    private TView? _view;
    private float _nextPoll, _visibleSince = -1f, _retryAt;
    private int _attempt, _failedOpenings;
    private bool _dumped, _waitingLogged;
    private TState? _state;

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <summary>The prefix of this integration's log codes, e.g. <c>MAINMENU</c>.</summary>
    protected abstract string Code { get; }

    /// <summary>The name used for the layout reference file, e.g. <c>mainmenu</c> for <c>carx.modmanager.ui-mainmenu.txt</c>.</summary>
    protected abstract string DumpName { get; }

    /// <inheritdoc />
    public abstract bool IsEnabled { get; }

    /// <inheritdoc />
    public abstract void BindConfig(ConfigFile config);

    /// <inheritdoc />
    /// <remarks>None: the screen is found by looking for it twice a second, not by hooking a method.</remarks>
    public IReadOnlyList<HookSpec> Hooks { get; } = Array.Empty<HookSpec>();

    /// <summary>What to write to the log when the integration is installed.</summary>
    protected abstract string InstallMessage { get; }

    /// <inheritdoc />
    public bool Install(Harmony harmony, ManualLogSource log)
    {
        log.LogInfo($"{Code}_WATCHING — {InstallMessage}");
        return true;
    }

    /// <inheritdoc />
    public void Shutdown() { /* the game is closing: nothing to put back */ }

    // ---- what a subclass provides -----------------------------------------------------------------------------------------

    /// <summary>False when the player asked for the game's own arrangement (nothing is then changed).</summary>
    protected virtual bool Wanted => true;

    /// <summary>Why the screen cannot be arranged yet, or <c>null</c> if it can. Not a failure: the screen is looked at again later.</summary>
    protected abstract string? NotReadyBecause(TView view, RectTransform canvas);

    /// <summary>What the arrangement depends on (which entries exist); when this changes the screen is arranged again.</summary>
    protected abstract string SignatureOf(TView view, RectTransform canvas);

    /// <summary>
    /// Runs the checks, makes the change, verifies it and logs the outcome. Returns what was changed, or <c>null</c> if a check failed or
    /// the result did not verify (in which case everything is already put back and logged; use <see cref="ReportFailure"/>).
    /// </summary>
    protected abstract TState? Arrange(TView view, RectTransform canvas, string signature, int attempt, bool last);

    /// <summary>True while everything the change moved is still where it was put.</summary>
    protected abstract bool Holds(TState state);

    /// <summary>Puts everything back where the change put it (the game moved it).</summary>
    protected abstract void Reapply(TState state);

    /// <summary>Puts the screen back as the game made it.</summary>
    protected abstract void Undo(TState state);

    /// <summary>The object whose layout is written to the reference file (the screen, or the container of its entries).</summary>
    protected abstract Transform DumpRoot(TView view, RectTransform canvas);

    // ---- helpers for the subclasses -----------------------------------------------------------------------------------------

    /// <summary>" (try 1 of 3; trying again shortly)" for a failure that will be retried, empty for the last try.</summary>
    protected static string TryNote(int attempt, bool last) => last ? string.Empty : $" (try {attempt} of {AttemptsPerOpening}; trying again shortly)";

    /// <summary>
    /// Logs a failed try: a warning for a try that will be repeated, an error (and a note on the status line) for the last one.
    /// </summary>
    /// <param name="message">The full log line, starting with its code.</param>
    /// <param name="last">Whether this was the last try for this opening.</param>
    /// <param name="healthReason">Short reason for the status line's problem list.</param>
    protected void ReportFailure(string message, bool last, string healthReason)
    {
        if (last)
        {
            Plugin.LogSource.LogError(message);
            IntegrationHealth.Report(Name, healthReason);
        }
        else Plugin.LogSource.LogWarning(message);
    }

    /// <summary>Tells the status line that this integration works.</summary>
    protected void ReportWorking() => IntegrationHealth.Clear(Name);

    /// <summary>The lowest object that holds all the given objects, or <c>null</c> for none.</summary>
    protected static Transform? CommonAncestor(IEnumerable<Transform> transforms)
    {
        List<Transform> list = transforms.ToList();
        if (list.Count == 0) return null;
        for (Transform? candidate = list[0].parent ?? list[0]; candidate != null; candidate = candidate.parent)
        {
            Transform current = candidate;
            if (list.All(node => IsUnder(node, current))) return candidate;
        }
        return null;
    }

    /// <summary>True if <paramref name="node"/> is <paramref name="ancestor"/> or somewhere below it.</summary>
    protected static bool IsUnder(Transform node, Transform ancestor)
    {
        for (Transform? t = node; t != null; t = t.parent)
            if (t.Pointer == ancestor.Pointer) return true;
        return false;
    }

    // ---- per frame --------------------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public void Update()
    {
        float now = Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + PollSeconds;

        TView? view = FindOpenView();
        if (view == null)
        {
            _visibleSince = -1f; // the screen is not open
            return;
        }
        if (_view == null || _view.Pointer != view.Pointer)
        {
            _view = view; // a new instance of the screen: whatever was done to the old one is gone with it
            _state = null;
            _visibleSince = -1f;
        }
        if (_visibleSince < 0f)
        {
            _visibleSince = now;
            _attempt = 0;
            _retryAt = 0f;
            _waitingLogged = false;
            return;
        }
        if (now - _visibleSince < SettleSeconds || now < _retryAt || _failedOpenings >= MaxFailedOpenings) return;

        try { Maintain(view, now); }
        catch (Exception exception)
        {
            Plugin.LogSource.LogError($"{Code}_ERROR — {exception}");
            _failedOpenings = MaxFailedOpenings; // an exception is a bug, not a timing problem: stop touching the screen
            if (_state != null) Undo(_state);
            _state = null;
            IntegrationHealth.Report(Name, "stopped after an error");
        }
    }

    private TView? FindOpenView()
    {
        TView? known = _view;
        if (known != null && known.gameObject.activeInHierarchy) return known;
        TView? found = Object.FindObjectOfType<TView>();
        return found != null && found.gameObject.activeInHierarchy ? found : null;
    }

    private void Maintain(TView view, float now)
    {
        RectTransform? canvas = GameUi.CanvasRoot(view.transform);
        if (canvas == null) return;

        string? waiting = NotReadyBecause(view, canvas);
        if (!_dumped && waiting == null)
        {
            _dumped = true;
            Plugin.LogSource.LogInfo($"{Code}_UI_DUMP — the screen's original layout: {UiDump.Write(DumpName, DumpRoot(view, canvas), 8, 1500)}");
        }
        if (!Wanted)
        {
            // the game's own arrangement was asked for (or is needed again): put back anything that was changed
            if (_state != null)
            {
                Undo(_state);
                _state = null;
            }
            return;
        }

        if (waiting != null)
        {
            if (!_waitingLogged)
            {
                _waitingLogged = true;
                Plugin.LogSource.LogInfo($"{Code}_WAITING — the screen is not ready to be rearranged yet: {waiting}.");
            }
            return;
        }

        string signature = SignatureOf(view, canvas);
        if (_state != null && _state.Signature == signature)
        {
            if (!Holds(_state)) HandleMoved(_state);
            return;
        }

        if (_state != null)
        {
            Plugin.LogSource.LogInfo($"{Code}_ENTRIES_CHANGED — the screen's entries changed; arranging again.");
            Undo(_state);
            _state = null;
        }
        if (_attempt >= AttemptsPerOpening) return;
        _attempt++;
        bool last = _attempt >= AttemptsPerOpening;
        TState? arranged = Arrange(view, canvas, signature, _attempt, last);
        if (arranged != null) _state = arranged;
        else if (last) _failedOpenings++;
        else _retryAt = now + _attempt; // a screen still sliding in gets a little longer each time
    }

    /// <summary>The game moved things: put them back a few times, after that undo the change for good.</summary>
    private void HandleMoved(TState state)
    {
        state.Reapplies++;
        if (state.Reapplies <= ReappliesBeforeGivingUp)
        {
            Reapply(state);
            Plugin.LogSource.LogWarning($"{Code}_MOVED — the game moved the entries; they were put back (time {state.Reapplies} of {ReappliesBeforeGivingUp}).");
            return;
        }
        Undo(state);
        _state = null;
        _failedOpenings = MaxFailedOpenings;
        Plugin.LogSource.LogError($"{Code}_OVERRIDDEN — the game keeps moving the entries (an animation or a reset), so the change was undone and the screen is left as designed. The original layout is in carx.modmanager.ui-{DumpName}.txt.");
        IntegrationHealth.Report(Name, "the game keeps moving the entries");
    }
}
