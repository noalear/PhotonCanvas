namespace PhotonCanvas.Core;

/// <summary>Durations in words, as the program shows them everywhere (desktop and phone alike).</summary>
public static class TimeText
{
    /// <summary>Plain duration: 45 s, 3 min 07 s, 1 h 02 min.</summary>
    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "–";
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : t.TotalMinutes >= 1 ? $"{t.Minutes} min {t.Seconds:00} s" : $"{t.Seconds} s";
    }

    /// <summary>Short clock-style duration: 45 s, 3:07, 1:02:30.</summary>
    public static string Clock(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "";
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : t.TotalMinutes >= 1 ? $"{t.Minutes}:{t.Seconds:00}" : $"{t.Seconds} s";
    }
}
