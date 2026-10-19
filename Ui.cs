using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace QuickFind;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(15, 18, 25);
    public static readonly Color Header = Color.FromArgb(15, 18, 25);
    public static readonly Color SearchBackground = Color.FromArgb(32, 39, 52);
    public static readonly Color Surface = Color.FromArgb(24, 30, 41);
    public static readonly Color Border = Color.FromArgb(42, 52, 68);
    public static readonly Color BorderStrong = Color.FromArgb(56, 70, 92);
    public static readonly Color Accent = Color.FromArgb(91, 157, 255);
    public static readonly Color AccentSoft = Color.FromArgb(44, 91, 158);
    public static readonly Color Text = Color.FromArgb(242, 245, 250);
    public static readonly Color TextMuted = Color.FromArgb(141, 153, 174);
    public static readonly Color TextFaint = Color.FromArgb(96, 108, 128);
    public static readonly Color Selection = Color.FromArgb(37, 49, 70);
    public static readonly Color SelectionStrong = Color.FromArgb(46, 62, 88);

    public static Font UiFont(float size, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style, GraphicsUnit.Point);

    public static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (diameter <= 1)
        {
            path.AddRectangle(rect);
            return path;
        }

        var arc = new RectangleF(rect.X, rect.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class SurfacePanel : Panel
{
    public SurfacePanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }
}

internal sealed class SearchShell : Panel
{
    public TextBox Box { get; }

    private readonly Label _placeholder;
    private bool _active;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
            {
                return;
            }

            _active = value;
            Invalidate();
        }
    }

    public SearchShell()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Background;

        Box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.SearchBackground,
            ForeColor = Theme.Text,
            Font = Theme.UiFont(15.5f),
            AccessibleName = "搜索文件",
            ShortcutsEnabled = true
        };

        _placeholder = new Label
        {
            AutoSize = false,
            Text = "输入文件名，快速定位…",
            ForeColor = Theme.TextFaint,
            BackColor = Theme.SearchBackground,
            Font = Theme.UiFont(15f),
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.IBeam,
            UseMnemonic = false
        };
        _placeholder.Click += (_, _) => Box.Focus();

        Box.GotFocus += (_, _) => Active = true;
        Box.LostFocus += (_, _) => Active = false;
        Box.TextChanged += (_, _) => UpdatePlaceholder();
        Controls.Add(Box);
        Controls.Add(_placeholder);
        _placeholder.BringToFront();

        Click += (_, _) => Box.Focus();
        Resize += (_, _) => LayoutBox();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        LayoutBox();
    }

    private void LayoutBox()
    {
        Box.Location = new Point(52, Math.Max(0, (Height - Box.Height) / 2));
        Box.Width = Math.Max(40, Width - 76);

        _placeholder.Location = new Point(56, Math.Max(0, (Height - 26) / 2));
        _placeholder.Size = new Size(Math.Max(20, Width - 84), 26);
    }

    private void UpdatePlaceholder()
    {
        _placeholder.Visible = Box.TextLength == 0;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Background);

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using GraphicsPath path = Theme.RoundedRectangle(rect, 12);
        using var fill = new SolidBrush(Theme.SearchBackground);
        g.FillPath(fill, path);

        Color borderColor = _active ? Theme.Accent : Theme.Border;
        using var border = new Pen(borderColor, _active ? 1.6f : 1f);
        g.DrawPath(border, path);

        if (_active)
        {
            using var glow = new Pen(Color.FromArgb(42, Theme.Accent), 3.5f);
            g.DrawPath(glow, path);
        }

        // Magnifier glyph.
        float cx = 25;
        float cy = Height / 2f - 1;
        using var pen = new Pen(_active ? Theme.Accent : Theme.TextMuted, 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawEllipse(pen, cx - 6.5f, cy - 6.5f, 12f, 12f);
        g.DrawLine(pen, cx + 4.5f, cy + 4.5f, cx + 9f, cy + 9f);
    }
}

internal sealed class TabButton : Control
{
    private readonly Font _font = Theme.UiFont(9.2f);
    private readonly Font _selectedFont = Theme.UiFont(9.2f, FontStyle.Bold);
    private string _label = string.Empty;
    private int _count = -1;
    private bool _selected;
    private bool _hover;

    public TabButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Background;
        Cursor = Cursors.Hand;
        TabStop = false;
        Height = 32;
        AccessibleRole = AccessibleRole.PageTab;
    }

    public void SetState(string label, int count, bool selected)
    {
        _label = label;
        _count = count;
        _selected = selected;
        AccessibleName = count >= 0 ? $"{label} {count}" : label;

        string countText = CountText();
        string text = string.IsNullOrEmpty(countText) ? label : $"{label}  {countText}";
        Width = TextRenderer.MeasureText(text, _selectedFont).Width + 28;
        Invalidate();
    }

    private string CountText()
    {
        if (_count < 0)
        {
            return string.Empty;
        }

        return _count > 999 ? "999+" : _count.ToString("N0");
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.Background);

        var rect = new Rectangle(0, 3, Width - 1, Height - 8);
        if (_selected || _hover)
        {
            using GraphicsPath path = Theme.RoundedRectangle(rect, 7);
            using var fill = new SolidBrush(_selected ? Theme.Selection : Theme.Surface);
            g.FillPath(fill, path);
        }

        string countText = CountText();
        string text = string.IsNullOrEmpty(countText) ? _label : $"{_label}  {countText}";
        var textRect = new Rectangle(10, rect.Top, Width - 20, rect.Height);
        TextRenderer.DrawText(
            g, text, _selected ? _selectedFont : _font, textRect,
            _selected ? Theme.Text : Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        if (_selected)
        {
            using var underline = new SolidBrush(Theme.Accent);
            g.FillRectangle(underline, 10, Height - 3, Math.Max(18, Width - 20), 2);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font.Dispose();
            _selectedFont.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class EmptyView : Panel
{
    private readonly Font _titleFont = Theme.UiFont(14f, FontStyle.Bold);
    private readonly Font _subtitleFont = Theme.UiFont(9.5f);
    private readonly Font _actionFont = Theme.UiFont(9.5f, FontStyle.Bold);

    private string _title = "输入文件名，立即定位";
    private string _subtitle = "支持空格拆分多个关键词，例如“项目 报告”";
    private string? _actionText;
    private Rectangle _actionRect;
    private bool _actionHover;

    public event EventHandler? ActionClicked;

    public EmptyView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Background;
        Cursor = Cursors.Default;
    }

    public void SetText(string title, string subtitle, string? actionText = null)
    {
        _title = title;
        _subtitle = subtitle;
        _actionText = actionText;
        _actionHover = false;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        float cx = Width / 2f;
        float cy = Height / 2f - (string.IsNullOrEmpty(_actionText) ? 66 : 82);
        using var ring = new Pen(Color.FromArgb(58, Theme.Accent), 2f);
        g.DrawEllipse(ring, cx - 33, cy - 33, 66, 66);
        using var handle = new Pen(Color.FromArgb(58, Theme.Accent), 3.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawLine(handle, cx + 22, cy + 22, cx + 42, cy + 42);

        using var dot = new SolidBrush(Color.FromArgb(90, Theme.Accent));
        g.FillEllipse(dot, cx - 4, cy - 4, 8, 8);

        var titleRect = new Rectangle(20, (int)(cy + 70), Width - 40, 34);
        TextRenderer.DrawText(
            g, _title, _titleFont, titleRect, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        var subtitleRect = new Rectangle(20, titleRect.Bottom + 2, Width - 40, 26);
        TextRenderer.DrawText(
            g, _subtitle, _subtitleFont, subtitleRect, Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        _actionRect = Rectangle.Empty;
        if (!string.IsNullOrEmpty(_actionText))
        {
            const int buttonWidth = 176;
            const int buttonHeight = 34;
            var button = new Rectangle(
                (Width - buttonWidth) / 2,
                subtitleRect.Bottom + 14,
                buttonWidth,
                buttonHeight);
            _actionRect = button;

            using GraphicsPath path = Theme.RoundedRectangle(button, 8);
            using var fill = new SolidBrush(_actionHover ? Color.FromArgb(112, 172, 255) : Theme.Accent);
            g.FillPath(fill, path);
            TextRenderer.DrawText(
                g, _actionText, _actionFont, button, Color.FromArgb(12, 20, 34),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hover = _actionRect.Contains(e.Location);
        if (hover != _actionHover)
        {
            _actionHover = hover;
            Cursor = hover ? Cursors.Hand : Cursors.Default;
            Invalidate(_actionRect);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_actionHover)
        {
            _actionHover = false;
            Cursor = Cursors.Default;
            Invalidate(_actionRect);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && _actionRect.Contains(e.Location))
        {
            ActionClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _subtitleFont.Dispose();
            _actionFont.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class BufferedListBox : ListBox
{
    public event EventHandler? Scrolled;

    public BufferedListBox()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        // WM_VSCROLL, WM_MOUSEWHEEL, WM_KEYDOWN, WM_KEYUP
        if (m.Msg is 0x0115 or 0x020A or 0x0100 or 0x0101)
        {
            Scrolled?.Invoke(this, EventArgs.Empty);
        }
    }
}

internal sealed class ScrollIndicator : Control
{
    private ListBox? _list;

    public ScrollIndicator()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Background;
        Enabled = false;
        TabStop = false;
    }

    public void Attach(ListBox list)
    {
        _list = list;
    }

    public void RefreshFromList()
    {
        if (_list is null)
        {
            return;
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_list is null || _list.Items.Count == 0)
        {
            return;
        }

        int visible = Math.Max(1, _list.ClientSize.Height / Math.Max(1, _list.ItemHeight));
        int total = _list.Items.Count;
        if (total <= visible)
        {
            return;
        }

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int thumbX = Math.Max(0, Width - 4);
        using (var track = new SolidBrush(Color.FromArgb(25, 32, 44)))
        {
            g.FillRectangle(track, thumbX, 2, 4, Height - 4);
        }

        int thumbHeight = Math.Max(24, (int)((Height - 4) * (visible / (float)total)));
        int travel = Math.Max(0, Height - 4 - thumbHeight);
        int maxTop = Math.Max(1, total - visible);
        int top = Math.Clamp(_list.TopIndex, 0, maxTop);
        int thumbY = 2 + (int)(travel * (top / (float)maxTop));

        var rect = new RectangleF(thumbX, thumbY, 4, thumbHeight);
        using GraphicsPath path = Theme.RoundedRectangle(rect, 2);
        using var thumb = new SolidBrush(Color.FromArgb(82, 98, 124));
        g.FillPath(thumb, path);
    }
}
