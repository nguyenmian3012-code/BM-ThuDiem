namespace BMThuDiem;

public sealed class ChartPane : Control
{
    public readonly record struct Sample(DateTime T, int G);
    readonly List<Sample> _all = new();
    public IReadOnlyList<Sample> Samples => _all;

    public ChartPane()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.FromArgb(18, 8, 14);
        Height = 168;
        Dock = DockStyle.Top;
    }

    public void Add(DateTime t, int g)
    {
        if (_all.Count > 0 && (t - _all[^1].T).TotalSeconds < 1.2) return;
        _all.Add(new Sample(t, g));
        if (_all.Count > 20000) _all.RemoveRange(0, _all.Count - 16000);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var padL = 48;
        var padR = 12;
        var padT = 16;
        var padB = 22;
        var plot = new Rectangle(padL, padT, Math.Max(10, Width - padL - padR), Math.Max(10, Height - padT - padB));
        using var axis = new Pen(Color.FromArgb(90, 50, 60));
        using var zero = new Pen(Color.FromArgb(120, 90, 40));
        using var line = new Pen(Color.FromArgb(80, 200, 220), 2f);
        using var cropPen = new Pen(Color.FromArgb(180, 140, 70), 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        using var font = new Font("Segoe UI", 8f);
        using var brush = new SolidBrush(Color.FromArgb(201, 180, 173));
        g.DrawRectangle(axis, plot);
        var y0 = Y(plot, 0);
        g.DrawLine(zero, plot.Left, y0, plot.Right, y0);
        g.DrawString("6000", font, brush, 2, plot.Top - 2);
        g.DrawString("0", font, brush, 18, y0 - 8);
        g.DrawString("-1500", font, brush, 2, plot.Bottom - 12);
        var view = Compress(_all);
        if (view.Count < 2)
        {
            g.DrawString("Chưa đủ mẫu COM4", font, brush, plot.Left + 8, plot.Top + 8);
            return;
        }
        var span = view[^1].X - view[0].X;
        if (span < 1) span = 1;
        for (var i = 1; i < view.Count; i++)
        {
            var a = view[i - 1];
            var b = view[i];
            var x1 = plot.Left + (float)((a.X - view[0].X) / span * plot.Width);
            var x2 = plot.Left + (float)((b.X - view[0].X) / span * plot.Width);
            var y1 = Y(plot, a.G);
            var y2 = Y(plot, b.G);
            g.DrawLine(b.Crop ? cropPen : line, x1, y1, x2, y2);
            if (b.Crop)
                g.DrawString("cắt", font, brush, x2 + 2, y2 - 14);
        }
        g.DrawString(view[0].T.ToString("HH:mm:ss"), font, brush, plot.Left, plot.Bottom + 2);
        g.DrawString(view[^1].T.ToString("HH:mm:ss"), font, brush, plot.Right - 58, plot.Bottom + 2);
    }

    static float Y(Rectangle plot, int grams)
    {
        var t = (grams - (-1500)) / 7500f;
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        return plot.Bottom - t * plot.Height;
    }

    readonly record struct View(DateTime T, int G, double X, bool Crop);

    static List<View> Compress(IReadOnlyList<Sample> src)
    {
        var view = new List<View>();
        double x = 0;
        var i = 0;
        while (i < src.Count)
        {
            var j = i;
            while (j + 1 < src.Count && Math.Abs(src[j + 1].G - src[i].G) <= 8)
                j++;
            var dur = (src[j].T - src[i].T).TotalSeconds;
            if (j > i + 1 && dur >= 45)
            {
                view.Add(new View(src[i].T, src[i].G, x, false));
                x += 2;
                view.Add(new View(src[j].T, src[j].G, x, true));
                x += 2;
                i = j + 1;
            }
            else
            {
                var step = i == 0 ? 0 : Math.Max(1.5, (src[i].T - src[i - 1].T).TotalSeconds);
                x += step;
                view.Add(new View(src[i].T, src[i].G, x, false));
                i++;
            }
        }
        return view;
    }
}
