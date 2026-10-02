using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How the application is asked to run: windowed or fullscreen, at which size and refresh rate, and how
/// hard the frame loop is allowed to push. One such answer is a <see cref="DisplayPreference"/>.
///
/// The player preferences are the store, for the same reason <see cref="VisualizationSwitchMemory"/> uses
/// them: that is where Unity already keeps this kind of per-user preference, in the editor and on every
/// platform alike, with no path to create and no file to corrupt. Each value is stored under its own key,
/// and only once somebody has actually chosen it, so an application that was never configured keeps the
/// window it was built with instead of a size this class invented.
///
/// <see cref="Current"/> answers what is in force and <see cref="Shipped"/> answers what the application
/// was built to run at, which are the two references a settings page needs: the first to show what is
/// happening, the second to offer a way back to it. The shipped values are captured the first time this
/// class touches the screen, before anything is applied - capturing them later would capture the stored
/// preference instead.
/// </summary>
public static class DisplaySettings
{
    /// <summary>Prefix of the keys, so a display preference cannot collide with one of another feature.</summary>
    private const string KeyPrefix = "robotsnap.display.";

    private const string ModeKey = KeyPrefix + "windowMode";
    private const string WidthKey = KeyPrefix + "resolutionWidth";
    private const string HeightKey = KeyPrefix + "resolutionHeight";
    private const string RefreshNumeratorKey = KeyPrefix + "refreshNumerator";
    private const string RefreshDenominatorKey = KeyPrefix + "refreshDenominator";
    private const string VsyncKey = KeyPrefix + "vsync";
    private const string FrameRateKey = KeyPrefix + "frameRate";
    private const string QualityKey = KeyPrefix + "qualityLevel";

    /// <summary>
    /// The sizes a settings page offers, smallest first: what a screen is normally sold at.
    ///
    /// A display reports every mode it can drive, which on a desktop is several dozen entries differing only
    /// by refresh rate, and listing them all buries the five answers anybody is looking for. The page keeps
    /// whichever of these the display actually supports, at the highest refresh rate it reports for that
    /// size, plus whatever the application is running at so the current size is never missing from its own
    /// list.
    /// </summary>
    public static readonly IReadOnlyList<Vector2Int> CommonSizes = new[]
    {
        new Vector2Int(1280, 720),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
        new Vector2Int(1920, 1200),
        new Vector2Int(2560, 1440),
        new Vector2Int(2560, 1600),
        new Vector2Int(3440, 1440),
        new Vector2Int(3840, 2160)
    };

    private static bool _shippedTaken;
    private static DisplayPreference _shipped;

    // -- the two references -----------------------------------------------------------------------

    /// <summary>
    /// What the application is running at: the preference an earlier session stored, or - for anything
    /// nobody ever chose - the value the application actually has.
    ///
    /// Reading the stored preference rather than the live screen is deliberate. A window size the platform
    /// adjusted, or the editor's Game view, is not the size that was asked for, and a page comparing the two
    /// would report a difference the reader never made.
    /// </summary>
    public static DisplayPreference Current()
    {
        TakeShipped();

        FullScreenMode mode = NormalizeMode(Screen.fullScreenMode);
        if (PlayerPrefs.HasKey(ModeKey))
            mode = NormalizeMode((FullScreenMode)PlayerPrefs.GetInt(ModeKey));

        // A size nobody ever chose is not a preference, and it is reported as one that was never made - a zero
        // pair. Reading the live window here instead would turn the editor's Game view, or any size the platform
        // happened to give the window, into a resolution the reader had supposedly asked for: the page would
        // compare it against its own field, find a difference, and offer to apply a size nobody picked.
        int width = 0;
        int height = 0;
        RefreshRate refresh = default;
        if (PlayerPrefs.HasKey(WidthKey) && PlayerPrefs.HasKey(HeightKey))
        {
            width = PlayerPrefs.GetInt(WidthKey);
            height = PlayerPrefs.GetInt(HeightKey);
            refresh = LoadRefresh();
        }

        bool vsync = PlayerPrefs.HasKey(VsyncKey)
            ? PlayerPrefs.GetInt(VsyncKey) != 0
            : QualitySettings.vSyncCount != 0;

        int framesPerSecond = PlayerPrefs.HasKey(FrameRateKey)
            ? PlayerPrefs.GetInt(FrameRateKey)
            : Application.targetFrameRate;

        int quality = PlayerPrefs.HasKey(QualityKey)
            ? PlayerPrefs.GetInt(QualityKey)
            : QualitySettings.GetQualityLevel();

        return new DisplayPreference(mode, width, height, refresh, vsync, framesPerSecond, quality);
    }

    /// <summary>
    /// What the application shipped with, which is the state a first run finds and the one the page's
    /// <c>Reset to defaults</c> prepares.
    /// </summary>
    public static DisplayPreference Shipped()
    {
        TakeShipped();
        return _shipped;
    }

    private static void TakeShipped()
    {
        if (_shippedTaken)
            return;

        _shipped = new DisplayPreference(
            NormalizeMode(Screen.fullScreenMode),
            Screen.width,
            Screen.height,
            Screen.currentResolution.refreshRateRatio,
            QualitySettings.vSyncCount != 0,
            Application.targetFrameRate,
            QualitySettings.GetQualityLevel());
        _shippedTaken = true;
    }

    // -- applying ---------------------------------------------------------------------------------

    /// <summary>
    /// A window mode the platform actually defined, and windowed for one it did not.
    ///
    /// The editor reports an undefined mode while the Game view is docked. Left as it is, that value would sit
    /// in the page as a window mode the dropdown can only show as "Windowed", and the field would report itself
    /// as changed the moment it was looked at - a difference the reader never made, which is exactly what the
    /// Apply button is supposed to mean.
    /// </summary>
    private static FullScreenMode NormalizeMode(FullScreenMode mode)
        => Enum.IsDefined(typeof(FullScreenMode), mode) ? mode : FullScreenMode.Windowed;

    /// <summary>
    /// Puts the application into the display it was left in. Called once when the application starts, so a
    /// session that was ended fullscreen at 1440p opens the way it was closed.
    ///
    /// Only what an earlier session actually stored is applied. Anything nobody ever chose is left alone
    /// rather than forced to a value of this class's own.
    /// </summary>
    public static void ApplyStored()
    {
        TakeShipped();

        if (PlayerPrefs.HasKey(ModeKey))
            Screen.fullScreenMode = (FullScreenMode)PlayerPrefs.GetInt(ModeKey);

        if (PlayerPrefs.HasKey(WidthKey) && PlayerPrefs.HasKey(HeightKey))
        {
            FullScreenMode mode = Screen.fullScreenMode;
            if (PlayerPrefs.HasKey(ModeKey))
                mode = (FullScreenMode)PlayerPrefs.GetInt(ModeKey);

            // The mode travels with the size because a resolution request carries one, and passing the mode
            // in force keeps a change of size from undoing a change of mode.
            Screen.SetResolution(
                PlayerPrefs.GetInt(WidthKey),
                PlayerPrefs.GetInt(HeightKey),
                mode,
                LoadRefresh());
        }

        if (PlayerPrefs.HasKey(VsyncKey))
            QualitySettings.vSyncCount = PlayerPrefs.GetInt(VsyncKey) != 0 ? 1 : 0;

        if (PlayerPrefs.HasKey(FrameRateKey))
            Application.targetFrameRate = PlayerPrefs.GetInt(FrameRateKey);

        if (PlayerPrefs.HasKey(QualityKey))
            QualitySettings.SetQualityLevel(PlayerPrefs.GetInt(QualityKey), true);
    }

    /// <summary>
    /// Applies a whole display and remembers it, member by member.
    ///
    /// Only what differs from <see cref="Current"/> is pushed and stored. That is what keeps a field the
    /// reader never touched - the resolution, say, while only the quality level was changed - from asking the
    /// platform for a size nobody chose, which would be a visible change produced by a control that was not
    /// moved.
    /// </summary>
    public static void Commit(DisplayPreference preference)
    {
        TakeShipped();
        DisplayPreference current = Current();

        if (preference.Mode != current.Mode)
        {
            Screen.fullScreenMode = preference.Mode;
            PlayerPrefs.SetInt(ModeKey, (int)preference.Mode);
        }

        // Only a size that was actually chosen is applied. A page that never touched its resolution field carries
        // whatever the application happens to be running at, and pushing that through Screen.SetResolution would
        // resize the window over a control nobody moved.
        bool sizeDiffers = preference.Width != current.Width || preference.Height != current.Height;
        bool refreshDiffers =
            preference.Refresh.numerator != current.Refresh.numerator ||
            preference.Refresh.denominator != current.Refresh.denominator;
        if (preference.HasResolution && (sizeDiffers || refreshDiffers))
        {
            Screen.SetResolution(preference.Width, preference.Height, preference.Mode, preference.Refresh);

            PlayerPrefs.SetInt(WidthKey, preference.Width);
            PlayerPrefs.SetInt(HeightKey, preference.Height);
            StoreRefresh(preference.Refresh);
        }

        if (preference.Vsync != current.Vsync)
        {
            QualitySettings.vSyncCount = preference.Vsync ? 1 : 0;
            PlayerPrefs.SetInt(VsyncKey, preference.Vsync ? 1 : 0);
        }

        if (preference.FramesPerSecond != current.FramesPerSecond)
        {
            Application.targetFrameRate = preference.FramesPerSecond;
            PlayerPrefs.SetInt(FrameRateKey, preference.FramesPerSecond);
        }

        if (preference.Quality != current.Quality)
        {
            QualitySettings.SetQualityLevel(preference.Quality, true);
            PlayerPrefs.SetInt(QualityKey, preference.Quality);
        }

        PlayerPrefs.Save();
    }

    // -- helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// A refresh rate as the two halves of its fraction.
    ///
    /// <see cref="RefreshRate"/> is a fraction and not a number - 59.94 Hz is 60000/1001 - so both halves
    /// are stored and the rate comes back exact. A rate whose halves do not fit in a preference falls back
    /// to its rounded value rather than to no preference at all.
    /// </summary>
    private static void StoreRefresh(RefreshRate refresh)
    {
        long numerator = refresh.numerator;
        long denominator = refresh.denominator < 1 ? 1 : refresh.denominator;

        if (numerator > int.MaxValue / 2 || denominator > int.MaxValue / 2)
        {
            numerator = Mathf.Max(1, Mathf.RoundToInt((float)refresh.value));
            denominator = 1;
        }

        PlayerPrefs.SetInt(RefreshNumeratorKey, (int)numerator);
        PlayerPrefs.SetInt(RefreshDenominatorKey, (int)denominator);
    }

    private static RefreshRate LoadRefresh()
    {
        int numerator = PlayerPrefs.GetInt(RefreshNumeratorKey, 0);
        int denominator = PlayerPrefs.GetInt(RefreshDenominatorKey, 1);

        if (numerator <= 0)
            return Screen.currentResolution.refreshRateRatio;

        return new RefreshRate
        {
            numerator = (uint)numerator,
            denominator = (uint)(denominator < 1 ? 1 : denominator)
        };
    }

    /// <summary>A refresh rate written the way a person reads it: whole hertz, never a fraction.</summary>
    public static string DescribeRefresh(RefreshRate refresh)
    {
        double hertz = refresh.value;
        if (hertz <= 0)
            return "unknown refresh rate";

        return $"{Mathf.RoundToInt((float)hertz)} Hz";
    }
}

/// <summary>
/// One whole answer to how the application should run: the window mode, the size, the refresh rate, the
/// vertical-sync choice, the frame rate ceiling and the quality level, as one value.
///
/// It is a value rather than six loose fields so a page can hold the display a reader is preparing, compare
/// it with the one in force, and apply it in a single call - which is what an Apply button is.
/// </summary>
public readonly struct DisplayPreference : IEquatable<DisplayPreference>
{
    public DisplayPreference(
        FullScreenMode mode,
        int width,
        int height,
        RefreshRate refresh,
        bool vsync,
        int framesPerSecond,
        int quality)
    {
        Mode = mode;
        Width = width;
        Height = height;
        Refresh = refresh;
        Vsync = vsync;
        FramesPerSecond = framesPerSecond;
        Quality = quality;
    }

    public FullScreenMode Mode { get; }
    public int Width { get; }
    public int Height { get; }
    public RefreshRate Refresh { get; }

    /// <summary>
    /// Whether a size was actually chosen, as opposed to whatever the window happens to be. Nothing asks the
    /// platform for a resolution unless this is true, which is what keeps a preference that was never made from
    /// turning into one.
    /// </summary>
    public bool HasResolution => Width > 0 && Height > 0;

    public bool Vsync { get; }

    /// <summary>Frame rate ceiling, <c>-1</c> meaning none - the value Unity uses for no ceiling.</summary>
    public int FramesPerSecond { get; }

    public int Quality { get; }

    /// <summary>The same display at another size and rate, keeping every other answer.</summary>
    public DisplayPreference WithResolution(int width, int height, RefreshRate refresh)
        => new DisplayPreference(Mode, width, height, refresh, Vsync, FramesPerSecond, Quality);

    /// <summary>The same display in another window mode, keeping every other answer.</summary>
    public DisplayPreference WithMode(FullScreenMode mode)
        => new DisplayPreference(mode, Width, Height, Refresh, Vsync, FramesPerSecond, Quality);

    /// <summary>The same display with another vertical-sync choice, keeping every other answer.</summary>
    public DisplayPreference WithVsync(bool vsync)
        => new DisplayPreference(Mode, Width, Height, Refresh, vsync, FramesPerSecond, Quality);

    /// <summary>The same display with another frame rate ceiling, keeping every other answer.</summary>
    public DisplayPreference WithFrameRate(int framesPerSecond)
        => new DisplayPreference(Mode, Width, Height, Refresh, Vsync, framesPerSecond, Quality);

    /// <summary>The same display at another quality level, keeping every other answer.</summary>
    public DisplayPreference WithQuality(int quality)
        => new DisplayPreference(Mode, Width, Height, Refresh, Vsync, FramesPerSecond, quality);

    public bool Equals(DisplayPreference other)
        => Mode == other.Mode &&
           Width == other.Width &&
           Height == other.Height &&
           Refresh.numerator == other.Refresh.numerator &&
           Refresh.denominator == other.Refresh.denominator &&
           Vsync == other.Vsync &&
           FramesPerSecond == other.FramesPerSecond &&
           Quality == other.Quality;

    public override bool Equals(object obj) => obj is DisplayPreference other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)Mode;
            hash = (hash * 397) ^ Width;
            hash = (hash * 397) ^ Height;
            hash = (hash * 397) ^ (int)Refresh.numerator;
            hash = (hash * 397) ^ (int)Refresh.denominator;
            hash = (hash * 397) ^ (Vsync ? 1 : 0);
            hash = (hash * 397) ^ FramesPerSecond;
            return (hash * 397) ^ Quality;
        }
    }

    public static bool operator ==(DisplayPreference left, DisplayPreference right) => left.Equals(right);

    public static bool operator !=(DisplayPreference left, DisplayPreference right) => !left.Equals(right);
}
