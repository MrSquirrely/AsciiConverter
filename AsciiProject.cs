namespace AsciiConverter;

public class AsciiProject {
    public string AsciiTxtPath { get; set; }
    public string AudioPath { get; set; } // This will be "audio.mp3" or "audio.wav"
    public double FramesPerSecond { get; set; }
    public int TotalFrames { get; set; }
}