namespace ASCIIV.Core;

public class AsciiProject {
    public string AsciiTxtPath { get; set; } = null!;
    public string AudioPath { get; set; } = null!;
    public double FramesPerSecond { get; set; }
    public int TotalFrames { get; set; }
    public string ColorName { get; set; } = "White";
}