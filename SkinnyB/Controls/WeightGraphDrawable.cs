using Microsoft.Maui.Graphics;

namespace SkinnyB.Controls;
public class WeightGraphDrawable : IDrawable
{
    public List<(DateTime Date, double Weight)> Points { get; set; } = [];

    public Color LineColor { get; set; } = Color.FromArgb("#2D6A4F");
    public Color DotColor { get; set; } = Color.FromArgb("#52B788");
    public Color AxisColor { get; set; } = Color.FromArgb("#CCCCCC");
    public Color LabelColor { get; set; } = Color.FromArgb("#888880");
    public Color FillColor { get; set; } = Color.FromArgb("#1A2D6A4F");

    private const float Pad = 48f;   // left/bottom padding for axis labels
    private const float PadR = 16f;   // right padding
    private const float PadT = 16f;   // top padding

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Points.Count < 2) return;

        float w = dirtyRect.Width;
        float h = dirtyRect.Height;

        float plotW = w - Pad - PadR;
        float plotH = h - Pad - PadT;

        double minW = Points.Min(p => p.Weight);
        double maxW = Points.Max(p => p.Weight);
        double rangeW = maxW - minW;

        if (rangeW < 1) { minW -= 1; maxW += 1; rangeW = 2; }

        // Scale helpers
        float xScale = plotW / Math.Max(Points.Count - 1, 1);
        Func<int, float> xFor = i => Pad + i * xScale;
        Func<double, float> yFor = v => PadT + plotH - (float)((v - minW) / rangeW * plotH);

        // Grid lines & Y labels 
        int gridLines = 4;
        canvas.FontSize = 9;
        canvas.FontColor = LabelColor;
        canvas.StrokeColor = AxisColor;
        canvas.StrokeDashPattern = [4, 4];
        canvas.StrokeSize = 0.5f;

        for (int g = 0; g <= gridLines; g++)
        {
            double val = minW + rangeW * g / gridLines;
            float yPos = yFor(val);

            canvas.DrawLine(Pad, yPos, w - PadR, yPos);
            canvas.DrawString($"{val:0.#}", 0, yPos - 7, Pad - 4, 14,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        canvas.StrokeDashPattern = null;

        // X axis labels (show ~5 evenly spaced dates) 
        int labelStep = Math.Max(1, (Points.Count - 1) / 4);
        for (int i = 0; i < Points.Count; i += labelStep)
        {
            float xPos = xFor(i);
            string label = Points[i].Date.ToString("MMM d");
            canvas.DrawString(label, xPos - 24, h - Pad + 4, 48, 16,
                HorizontalAlignment.Center, VerticalAlignment.Top);
        }
        // Always label the last point
        if ((Points.Count - 1) % labelStep != 0)
        {
            int last = Points.Count - 1;
            string label = Points[last].Date.ToString("MMM d");
            canvas.DrawString(label, xFor(last) - 24, h - Pad + 4, 48, 16,
                HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        // Fill path under the line 
        var fillPath = new PathF();
        fillPath.MoveTo(xFor(0), yFor(Points[0].Weight));
        for (int i = 1; i < Points.Count; i++)
            fillPath.LineTo(xFor(i), yFor(Points[i].Weight));

        fillPath.LineTo(xFor(Points.Count - 1), PadT + plotH);
        fillPath.LineTo(xFor(0), PadT + plotH);
        fillPath.Close();

        canvas.FillColor = FillColor;
        canvas.FillPath(fillPath);

        // Line 
        canvas.StrokeColor = LineColor;
        canvas.StrokeDashPattern = null;
        canvas.StrokeSize = 2f;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        var linePath = new PathF();
        linePath.MoveTo(xFor(0), yFor(Points[0].Weight));
        for (int i = 1; i < Points.Count; i++)
            linePath.LineTo(xFor(i), yFor(Points[i].Weight));

        canvas.DrawPath(linePath);

        // Dots 
        canvas.FillColor = DotColor;
        float dotR = Points.Count > 20 ? 2f : 4f;
        foreach (var (_, pt) in Points.Select((p, i) => (i, p)))
        {
            int i2 = Points.IndexOf(pt);
            canvas.FillCircle(xFor(i2), yFor(pt.Weight), dotR);
        }

        // Axis lines 
        canvas.StrokeColor = AxisColor;
        canvas.StrokeSize = 1f;
        canvas.DrawLine(Pad, PadT, Pad, PadT + plotH);            // Y axis
        canvas.DrawLine(Pad, PadT + plotH, w - PadR, PadT + plotH); // X axis
    }
}