using UnityEngine;

namespace CarXModManager.Ui;

/// <summary>
/// Remembers where one of the game's UI objects is (anchors, pivot, size, position and scale) so that a change to it can be undone exactly.
/// </summary>
internal sealed class RectMemory
{
    private readonly RectTransform _rect;
    private readonly Vector2 _anchorMin, _anchorMax, _pivot, _sizeDelta, _anchoredPosition;
    private readonly Vector3 _localScale;

    /// <summary>Takes note of the current placement of <paramref name="rect"/>.</summary>
    public RectMemory(RectTransform rect)
    {
        _rect = rect;
        _anchorMin = rect.anchorMin;
        _anchorMax = rect.anchorMax;
        _pivot = rect.pivot;
        _sizeDelta = rect.sizeDelta;
        _anchoredPosition = rect.anchoredPosition;
        _localScale = rect.localScale;
    }

    /// <summary>The scale the object had when this was created.</summary>
    public Vector3 OriginalScale => _localScale;

    /// <summary>Puts the object back as it was when this was created. Does nothing if the object has been destroyed since.</summary>
    public void Restore()
    {
        if (_rect == null) return;
        _rect.anchorMin = _anchorMin;
        _rect.anchorMax = _anchorMax;
        _rect.pivot = _pivot;
        _rect.sizeDelta = _sizeDelta;
        _rect.anchoredPosition = _anchoredPosition;
        _rect.localScale = _localScale;
    }
}
