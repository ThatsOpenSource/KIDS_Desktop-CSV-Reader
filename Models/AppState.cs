namespace KidsXmlToCsv.Models;

public sealed class AppState
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public bool IsMaximized { get; set; }

    public bool HasValidSize => Width >= 900 && Height >= 600;
    public bool HasValidPosition => X.HasValue && Y.HasValue
                                    && X.Value > -10000 && X.Value < 10000
                                    && Y.Value > -10000 && Y.Value < 10000;
}
