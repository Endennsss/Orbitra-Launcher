using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Splat;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Utility;
using NAudio.Wave;

namespace SS14.Launcher;

public static class UiSoundService
{
    private static bool _initialized;
    private static readonly List<(WaveOutEvent Output, AudioFileReader Reader)> ActiveSounds = [];

    public static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;
        Button.ClickEvent.AddClassHandler<Button>((button, _) =>
        {
            if (button.Classes.Contains("SoundPreview"))
                return;
            if (button is ToggleButton)
                Play("toggle.wav");
            else
                Play("click.wav");
        });
        InputElement.PointerPressedEvent.AddClassHandler<TabItem>((_, _) => Play("navigation.wav"));
    }

    public static void PlayNavigation() => Play("navigation.wav");
    public static void PlayNotification() => Play("notification.wav");
    public static void PlayError() => Play("error.wav");
    public static void Preview(string fileName) => Play(fileName);

    private static void Play(string fileName)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var cfg = Locator.Current.GetService<DataManager>();
            if (cfg != null && !cfg.GetCVar(CVars.UiSoundsEnabled))
                return;

            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", fileName);
            if (!File.Exists(path)) return;
            var reader = new AudioFileReader(path)
            {
                Volume = Math.Clamp((cfg?.GetCVar(CVars.UiSoundVolume) ?? 65) / 100f, 0f, 1f)
            };
            var output = new WaveOutEvent();
            output.Init(reader);
            ActiveSounds.Add((output, reader));
            output.PlaybackStopped += (_, _) =>
            {
                ActiveSounds.RemoveAll(x => ReferenceEquals(x.Output, output));
                output.Dispose();
                reader.Dispose();
            };
            output.Play();
        }
        catch
        {
            // Optional UI audio must never affect launcher behavior.
        }
    }
}
