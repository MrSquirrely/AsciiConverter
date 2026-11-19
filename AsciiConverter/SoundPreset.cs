namespace ASCIIV.Converter;

/// <summary>
/// A named setting of the lo-fi audio effect.
/// </summary>
/// <param name="Name">Shown in the sound preset list.</param>
/// <param name="Description">One line on what it sounds like, shown as a tooltip.</param>
/// <param name="BitCrush">False leaves the audio untouched.</param>
/// <param name="BitDepth">Bit depth of the effect; lower is grittier.</param>
/// <param name="DownsampleFactor">Sample-rate reduction of the effect; higher is more muffled and metallic.</param>
public sealed record SoundPreset(string Name, string Description, bool BitCrush, int BitDepth, int DownsampleFactor) {
    public static SoundPreset Clean { get; } = new("Clean", "The original audio, untouched", BitCrush: false, BitDepth: 16, DownsampleFactor: 2);
    public static SoundPreset LoFi { get; } = new("Lo-Fi", "Soft and fuzzy, like a worn recording", BitCrush: true, BitDepth: 9, DownsampleFactor: 10);
    public static SoundPreset WarmTape { get; } = new("Warm Tape", "Only a little grit, still easy on the ears", BitCrush: true, BitDepth: 12, DownsampleFactor: 3);
    public static SoundPreset RetroConsole { get; } = new("Retro Console", "8-bit home console sound", BitCrush: true, BitDepth: 8, DownsampleFactor: 6);
    public static SoundPreset Arcade { get; } = new("Arcade", "Bright, buzzy arcade cabinet sound", BitCrush: true, BitDepth: 6, DownsampleFactor: 10);
    public static SoundPreset WalkieTalkie { get; } = new("Walkie-Talkie", "Thin and crackly, like a cheap radio", BitCrush: true, BitDepth: 5, DownsampleFactor: 16);
    public static SoundPreset Crunch { get; } = new("Crunch", "Heavily distorted and metallic", BitCrush: true, BitDepth: 4, DownsampleFactor: 20);
    public static SoundPreset Destroyed { get; } = new("Destroyed", "As broken as it gets: barely recognisable", BitCrush: true, BitDepth: 2, DownsampleFactor: 30);
    public static SoundPreset Swamp { get; } = new("Swamp", "Murky and gurgling, like sound from the bottom of a bog", BitCrush: true, BitDepth: 4, DownsampleFactor: 24);

    /// <summary>The sound the application starts with.</summary>
    public static SoundPreset Default => LoFi;

    public static IReadOnlyList<SoundPreset> All { get; } = [Clean, LoFi, WarmTape, RetroConsole, Arcade, WalkieTalkie, Crunch, Swamp, Destroyed];
}