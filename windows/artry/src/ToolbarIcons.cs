using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Artry;

internal static class ToolbarIcons
{
    public static Bitmap CreateIcon(string name, Color color, Color accentColor, int size = 18)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var thinPen = new Pen(color, 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(color);

        switch (name.ToLowerInvariant())
        {
            case "home":
                PointF[] roof = [new PointF(3f, 8.5f), new PointF(9f, 3.5f), new PointF(15f, 8.5f)];
                g.DrawLines(pen, roof);
                g.DrawRectangle(pen, 5f, 8.5f, 8f, 6.5f);
                g.DrawLine(pen, 8f, 15f, 8f, 11.5f);
                g.DrawLine(pen, 8f, 11.5f, 10f, 11.5f);
                g.DrawLine(pen, 10f, 11.5f, 10f, 15f);
                break;

            case "undo":
                // Curved arrow counter-clockwise
                using (var path = new GraphicsPath())
                {
                    path.AddArc(4f, 6f, 10f, 9f, 200, 160);
                    g.DrawPath(pen, path);
                }
                g.FillPolygon(brush, [new PointF(2.5f, 7.5f), new PointF(6.5f, 4f), new PointF(6f, 9f)]);
                break;

            case "redo":
                // Curved arrow clockwise
                using (var path = new GraphicsPath())
                {
                    path.AddArc(4f, 6f, 10f, 9f, 340, -160);
                    g.DrawPath(pen, path);
                }
                g.FillPolygon(brush, [new PointF(15.5f, 7.5f), new PointF(11.5f, 4f), new PointF(12f, 9f)]);
                break;

            case "print":
                // Minimalist printer
                g.DrawRectangle(pen, 3f, 6f, 12f, 7f);
                g.DrawLine(pen, 5f, 6f, 5f, 3f);
                g.DrawLine(pen, 5f, 3f, 13f, 3f);
                g.DrawLine(pen, 13f, 3f, 13f, 6f);
                g.FillRectangle(brush, 5f, 9f, 8f, 5f);
                g.FillEllipse(brush, 12f, 7.5f, 1.5f, 1.5f);
                break;

            case "bold":
                using (var font = new Font("Georgia", 11.5f, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("B", font, brush, new RectangleF(0, 0, size, size), sf);
                }
                break;

            case "italic":
                using (var font = new Font("Georgia", 11.5f, FontStyle.Italic | FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("I", font, brush, new RectangleF(0, 0, size, size), sf);
                }
                break;

            case "underline":
                using (var font = new Font("Georgia", 11f, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
                    g.DrawString("U", font, brush, new RectangleF(0, 0.5f, size, size - 4), sf);
                    g.DrawLine(new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round }, 3f, size - 2.5f, size - 3f, size - 2.5f);
                }
                break;

            case "strikethrough":
                using (var font = new Font("Georgia", 11f, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("S", font, brush, new RectangleF(0, 0, size, size), sf);
                    g.DrawLine(new Pen(color, 1.8f), 2.5f, size / 2f, size - 2.5f, size / 2f);
                }
                break;

            case "textcolor":
                using (var font = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
                    g.DrawString("A", font, brush, new RectangleF(0, -0.5f, size, size - 4), sf);
                }
                using (var barBrush = new SolidBrush(accentColor))
                {
                    g.FillRectangle(barBrush, 2.5f, size - 3.5f, size - 5f, 3f);
                }
                break;

            case "highlight":
                // Marker pen tip & color bar
                using (var markerPen = new Pen(color, 1.5f))
                {
                    g.DrawLine(markerPen, 5f, 11f, 11f, 5f);
                    g.DrawLine(markerPen, 11f, 5f, 13.5f, 7.5f);
                    g.DrawLine(markerPen, 13.5f, 7.5f, 7.5f, 13.5f);
                    g.DrawLine(markerPen, 7.5f, 13.5f, 5f, 11f);
                    g.FillPolygon(brush, [new PointF(3.5f, 14.5f), new PointF(5f, 11f), new PointF(7.5f, 13.5f)]);
                }
                using (var barBrush = new SolidBrush(accentColor))
                {
                    g.FillRectangle(barBrush, 2.5f, size - 3.5f, size - 5f, 3f);
                }
                break;

            case "alignleft":
                g.DrawLine(pen, 2.5f, 3.5f, size - 2.5f, 3.5f);
                g.DrawLine(pen, 2.5f, 7.5f, size - 6.5f, 7.5f);
                g.DrawLine(pen, 2.5f, 11.5f, size - 2.5f, 11.5f);
                g.DrawLine(pen, 2.5f, 15.5f, size - 8.5f, 15.5f);
                break;

            case "aligncenter":
                g.DrawLine(pen, 2.5f, 3.5f, size - 2.5f, 3.5f);
                g.DrawLine(pen, 5f, 7.5f, size - 5f, 7.5f);
                g.DrawLine(pen, 2.5f, 11.5f, size - 2.5f, 11.5f);
                g.DrawLine(pen, 6f, 15.5f, size - 6f, 15.5f);
                break;

            case "alignright":
                g.DrawLine(pen, 2.5f, 3.5f, size - 2.5f, 3.5f);
                g.DrawLine(pen, 6.5f, 7.5f, size - 2.5f, 7.5f);
                g.DrawLine(pen, 2.5f, 11.5f, size - 2.5f, 11.5f);
                g.DrawLine(pen, 8.5f, 15.5f, size - 2.5f, 15.5f);
                break;

            case "alignjustify":
                g.DrawLine(pen, 2.5f, 3.5f, size - 2.5f, 3.5f);
                g.DrawLine(pen, 2.5f, 7.5f, size - 2.5f, 7.5f);
                g.DrawLine(pen, 2.5f, 11.5f, size - 2.5f, 11.5f);
                g.DrawLine(pen, 2.5f, 15.5f, size - 2.5f, 15.5f);
                break;

            case "linespacing":
                // Vertical up/down arrow on left, 3 lines on right
                g.DrawLine(pen, 4f, 3.5f, 4f, 14.5f);
                g.FillPolygon(brush, [new PointF(4f, 1.5f), new PointF(2f, 4.5f), new PointF(6f, 4.5f)]);
                g.FillPolygon(brush, [new PointF(4f, 16.5f), new PointF(2f, 13.5f), new PointF(6f, 13.5f)]);
                g.DrawLine(thinPen, 8.5f, 4.5f, size - 2f, 4.5f);
                g.DrawLine(thinPen, 8.5f, 9f, size - 2f, 9f);
                g.DrawLine(thinPen, 8.5f, 13.5f, size - 2f, 13.5f);
                break;

            case "bulletlist":
                // 3 bullets + 3 lines
                g.FillEllipse(brush, 2.5f, 3.5f, 2.8f, 2.8f);
                g.FillEllipse(brush, 2.5f, 8f, 2.8f, 2.8f);
                g.FillEllipse(brush, 2.5f, 12.5f, 2.8f, 2.8f);
                g.DrawLine(pen, 8f, 5f, size - 2f, 5f);
                g.DrawLine(pen, 8f, 9.5f, size - 2f, 9.5f);
                g.DrawLine(pen, 8f, 14f, size - 2f, 14f);
                break;

            case "numberedlist":
                using (var numFont = new Font("Segoe UI", 6.5f, FontStyle.Bold))
                {
                    g.DrawString("1", numFont, brush, new PointF(1f, 1.5f));
                    g.DrawString("2", numFont, brush, new PointF(1f, 6.5f));
                    g.DrawString("3", numFont, brush, new PointF(1f, 11.5f));
                }
                g.DrawLine(pen, 8f, 4.5f, size - 2f, 4.5f);
                g.DrawLine(pen, 8f, 9.5f, size - 2f, 9.5f);
                g.DrawLine(pen, 8f, 14.5f, size - 2f, 14.5f);
                break;

            case "indentdecrease":
                g.DrawLine(pen, 2.5f, 3.5f, size - 2.5f, 3.5f);
                g.DrawLine(pen, 7f, 7.5f, size - 2.5f, 7.5f);
                g.DrawLine(pen, 7f, 11.5f, size - 2.5f, 11.5f);
                g.DrawLine(pen, 2.5f, 15.5f, size - 2.5f, 15.5f);
                // Left arrow
                g.FillPolygon(brush, [new PointF(2f, 9.5f), new PointF(5.5f, 7f), new PointF(5.5f, 12f)]);
                break;

            case "indentincrease":
                g.DrawLine(pen, 2.5f, 3.5f, size - 2.5f, 3.5f);
                g.DrawLine(pen, 7f, 7.5f, size - 2.5f, 7.5f);
                g.DrawLine(pen, 7f, 11.5f, size - 2.5f, 11.5f);
                g.DrawLine(pen, 2.5f, 15.5f, size - 2.5f, 15.5f);
                // Right arrow
                g.FillPolygon(brush, [new PointF(5.5f, 9.5f), new PointF(2f, 7f), new PointF(2f, 12f)]);
                break;

            case "clearformatting":
                using (var font = new Font("Segoe UI", 10f, FontStyle.Bold))
                {
                    g.DrawString("T", font, brush, new PointF(1.5f, 0.5f));
                }
                using (var slashPen = new Pen(Color.FromArgb(220, 50, 50), 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(slashPen, 9f, 4f, 15.5f, 14.5f);
                }
                break;
        }

        return bmp;
    }
}
