using System;

namespace VineMix.Desktop.Controls;

/// <summary>The tone node's original gain range and keyboard increments.</summary>
internal sealed class RotaryGainControl(string label, string frequency, Action<double> changed)
    : RotaryValueControl(label, -12, 12, .1, "dB", 0, changed,
        detail: frequency, smallChange: .5, largeChange: 2);
