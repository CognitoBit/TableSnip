namespace TableSnip.Core;

/// <summary>
/// A single recognised word with its bounding box (pixel units of the source image) and a
/// 0-100 confidence. Engines that report no confidence use a neutral 75.
/// </summary>
public readonly record struct OcrWord(string Text, double X, double Y, double W, double H, double Conf = 75)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public double CX => X + W / 2;
    public double CY => Y + H / 2;
    public double Area => W * H;
}
