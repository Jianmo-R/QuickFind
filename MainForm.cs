using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace QuickFind;

public sealed class MainForm : Form
{
    private const int HotkeyId = 0x51F1;
    private const int ResizeGrip = 7;
    private static readonly TimeSpan AutoRefreshAge = TimeSpan.FromMinutes(10);

    private static readonly string DataDirectory = AppPaths.DataDirectory;

    private static readonly string IndexPath = AppPaths.IndexPath;
    private static readonly string LastScanPath = AppPaths.LastScanPath;

    private readonly EventWaitHandle _showEvent;
    private readonly string _indexPath = IndexPath;

    private volatile FileIndex _index = FileIndex.Empty;
    private CancellationTokenSource? _indexCts;
    private CancellationTokenSource? _searchCts;
    private IndexProgress _indexProgress;
    private bool _isIndexing;
    private bool _isSaving;
    private bool _isPositioned;
    private bool _loadedOnce;
    private bool _balloonShown;
    private bool _hotkeyRegistered;
    private string _hotkeyText = "Ctrl + Alt + Space";
    private string _directStatus = string.Empty;
    private string _aiStatusText = string.Empty;
    private string _aiError = string.Empty;
    private string _query = string.Empty;
    private SearchResult? _directResult;
    private SearchResult? _aiResult;
    private IReadOnlyList<string> _aiKeywords = Array.Empty<string>();
    private SearchGroup _activeGroup = SearchGroup.Direct;
    private DeepSeekSession? _deepSeek;
    private CancellationTokenSource? _aiCts;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private int _directSelection;
    private int _aiSelection;
    private int _aiVersion;
    private bool _isAiRunning;
    private bool _isConnecting;
    private bool _aiConnected;
    private int _indexRunId;
    private int _searchVersion;

    private readonly Icon _appIcon;
    private readonly Panel _header = new SurfacePanel();
    private readonly Label _title = new();
    private readonly Label _subtitle = new();
    private readonly Label _hotkeyLabel = new();
    private readonly Label _minimizeButton = new();
    private readonly Label _closeButton = new();
    private readonly Panel _searchHost = new();
    private readonly SearchShell _searchShell = new();
    private readonly Panel _resultsHost = new();
    private readonly Panel _tabBar = new();
    private readonly TabButton _directTab = new();
    private readonly TabButton _aiTab = new();
    private readonly Label _aiStatus = new();
    private readonly Panel _contentHost = new();
    private readonly Panel _listClip = new();
    private readonly BufferedListBox _results = new();
    private readonly ScrollIndicator _scrollIndicator = new();
    private readonly EmptyView _emptyView = new();
    private readonly Button _emptyAction = new();
    private readonly Panel _footer = new SurfacePanel();
    private readonly Label _status = new();
    private readonly Label _hint = new();
    private readonly ToolTip _toolTip = new();
    private readonly NotifyIcon _trayIcon;
    private readonly Font _fileFont = Theme.UiFont(10.5f);
    private readonly Font _fileSelectedFont = Theme.UiFont(10.5f, FontStyle.Bold);
    private readonly Font _pathFont = Theme.UiFont(8.5f);
    private readonly Font _badgeFont = Theme.UiFont(7f, FontStyle.Bold);

    public MainForm(FileIndex initialIndex, EventWaitHandle showEvent)
    {
        _index = initialIndex;
        _showEvent = showEvent;
        _appIcon = LoadAppIcon();

        SuspendLayout();
        ConfigureWindow();
        BuildHeader();
        BuildSearchArea();
        BuildResultsArea();
        BuildFooter();

        Controls.Add(_resultsHost);
        Controls.Add(_footer);
        Controls.Add(_searchHost);
        Controls.Add(_header);

        _trayIcon = BuildTrayIcon();
        ResumeLayout(performLayout: true);
        LayoutHeader();
        LayoutFooter();
        StartShowListener();

        _searchShell.Box.TextChanged += (_, _) => _ = SearchAsync(immediate: false);
        _searchShell.Box.KeyDown += SearchBox_KeyDown;
        _results.DrawItem += DrawResultItem;
        _results.KeyDown += Results_KeyDown;
        _results.KeyPress += Results_KeyPress;
        _results.MouseDoubleClick += (_, e) =>
        {
            int index = _results.IndexFromPoint(e.Location);
            if (index >= 0)
            {
                _results.SelectedIndex = index;
                OpenSelected(reveal: false);
            }
        };
        _results.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                int index = _results.IndexFromPoint(e.Location);
                if (index >= 0)
                {
                    _results.SelectedIndex = index;
                }
            }
        };
        _results.ContextMenuStrip = BuildResultMenu();

        Resize += (_, _) =>
        {
            LayoutHeader();
            LayoutFooter();
            Invalidate();
        };
        DpiChanged += (_, _) =>
        {
            _results.ItemHeight = Math.Max(52, (int)(64 * DeviceDpi / 96f));
            LayoutHeader();
            LayoutFooter();
        };

        ShowInTaskbar = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        Location = SuggestedLocation();
        Load += (_, _) =>
        {
            _results.ItemHeight = Math.Max(52, (int)(64 * DeviceDpi / 96f));
            LayoutHeader();
            LayoutFooter();
        };
    }

    private void ConfigureWindow()
    {
        Text = "闪查 QuickFind";
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Theme.Background;
        ClientSize = new Size(880, 600);
        MinimumSize = new Size(700, 470);
        MaximizeBox = false;
        MinimizeBox = false;
        Padding = new Padding(1);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        Icon = _appIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
    }

    private void BuildHeader()
    {
        _header.Dock = DockStyle.Top;
        _header.Height = 60;
        _header.BackColor = Theme.Header;
        _header.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 22, _header.Height - 1, _header.Width - 22, _header.Height - 1);
        };
        _header.MouseDown += Header_MouseDown;

        _title.Text = "闪查";
        _title.Font = Theme.UiFont(13f, FontStyle.Bold);
        _title.ForeColor = Theme.Text;
        _title.AutoSize = true;
        _title.BackColor = Color.Transparent;
        _title.MouseDown += Header_MouseDown;
        _header.Controls.Add(_title);

        _subtitle.Text = "QUICK FIND";
        _subtitle.Font = Theme.UiFont(7.5f, FontStyle.Bold);
        _subtitle.ForeColor = Color.FromArgb(96, 140, 210);
        _subtitle.AutoSize = true;
        _subtitle.BackColor = Color.Transparent;
        _subtitle.MouseDown += Header_MouseDown;
        _header.Controls.Add(_subtitle);

        _hotkeyLabel.AutoSize = false;
        _hotkeyLabel.Size = new Size(190, 24);
        _hotkeyLabel.TextAlign = ContentAlignment.MiddleRight;
        _hotkeyLabel.ForeColor = Theme.TextFaint;
        _hotkeyLabel.Font = Theme.UiFont(8.5f);
        _hotkeyLabel.BackColor = Color.Transparent;
        _header.Controls.Add(_hotkeyLabel);

        _minimizeButton.Text = "—";
        _minimizeButton.Font = Theme.UiFont(10f);
        _minimizeButton.ForeColor = Theme.TextMuted;
        _minimizeButton.TextAlign = ContentAlignment.MiddleCenter;
        _minimizeButton.Size = new Size(34, 30);
        _minimizeButton.Cursor = Cursors.Hand;
        _minimizeButton.BackColor = Color.Transparent;
        _minimizeButton.MouseEnter += (_, _) => _minimizeButton.BackColor = Theme.Surface;
        _minimizeButton.MouseLeave += (_, _) => _minimizeButton.BackColor = Color.Transparent;
        _minimizeButton.Click += (_, _) => HideToTray();
        _header.Controls.Add(_minimizeButton);

        _closeButton.Text = "✕";
        _closeButton.Font = Theme.UiFont(9.5f);
        _closeButton.ForeColor = Theme.TextMuted;
        _closeButton.TextAlign = ContentAlignment.MiddleCenter;
        _closeButton.Size = new Size(34, 30);
        _closeButton.Cursor = Cursors.Hand;
        _closeButton.BackColor = Color.Transparent;
        _closeButton.MouseEnter += (_, _) =>
        {
            _closeButton.BackColor = Color.FromArgb(52, 72, 98);
            _closeButton.ForeColor = Theme.Text;
        };
        _closeButton.MouseLeave += (_, _) =>
        {
            _closeButton.BackColor = Color.Transparent;
            _closeButton.ForeColor = Theme.TextMuted;
        };
        _closeButton.Click += (_, _) => Close();
        _header.Controls.Add(_closeButton);

        _toolTip.SetToolTip(_minimizeButton, "最小化到托盘");
        _toolTip.SetToolTip(_closeButton, "退出");
        LayoutHeader();
    }

    private void LayoutHeader()
    {
        int right = _header.ClientSize.Width - 16;
        _closeButton.Location = new Point(right - _closeButton.Width, 15);
        _minimizeButton.Location = new Point(_closeButton.Left - _minimizeButton.Width - 2, 15);
        _hotkeyLabel.Location = new Point(Math.Max(220, _minimizeButton.Left - _hotkeyLabel.Width - 18), 18);

        _title.Location = new Point(22, 13);
        _subtitle.Location = new Point(_title.Right + 10, 24);
    }

    private void BuildSearchArea()
    {
        _searchHost.Dock = DockStyle.Top;
        _searchHost.Height = 86;
        _searchHost.Padding = new Padding(22, 10, 22, 14);
        _searchHost.BackColor = Theme.Background;

        _searchShell.Dock = DockStyle.Fill;
        _searchHost.Controls.Add(_searchShell);

        _searchShell.Box.TextChanged += (_, _) => UpdateStatus();
    }

    private void BuildResultsArea()
    {
        _resultsHost.Dock = DockStyle.Fill;
        _resultsHost.BackColor = Theme.Background;
        _resultsHost.Padding = new Padding(22, 0, 22, 0);

        _tabBar.Dock = DockStyle.Top;
        _tabBar.Height = 44;
        _tabBar.BackColor = Theme.Background;
        _tabBar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, _tabBar.Height - 1, _tabBar.Width, _tabBar.Height - 1);
        };
        _tabBar.Resize += (_, _) => LayoutTabBar();

        _directTab.SetState("文件名匹配", -1, true);
        _directTab.Click += (_, _) => SwitchGroup(SearchGroup.Direct);
        _tabBar.Controls.Add(_directTab);

        _aiTab.SetState("AI 相关", -1, false);
        _aiTab.Click += (_, _) => SwitchGroup(SearchGroup.Ai);
        _tabBar.Controls.Add(_aiTab);

        _aiStatus.AutoSize = false;
        _aiStatus.TextAlign = ContentAlignment.MiddleRight;
        _aiStatus.Font = Theme.UiFont(8.5f);
        _aiStatus.ForeColor = Theme.Accent;
        _aiStatus.Cursor = Cursors.Hand;
        _aiStatus.Click += (_, _) => _ = AiStatusClickedAsync();
        _tabBar.Controls.Add(_aiStatus);
        LayoutTabBar();

        _contentHost.Dock = DockStyle.Fill;
        _contentHost.BackColor = Theme.Background;
        _contentHost.Padding = new Padding(0, _tabBar.Height, 0, 0);
        _resultsHost.Controls.Add(_contentHost);

        _listClip.Dock = DockStyle.Fill;
        _listClip.BackColor = Theme.Background;
        _listClip.Resize += (_, _) => LayoutResultsList();
        _contentHost.Controls.Add(_listClip);

        _results.BorderStyle = BorderStyle.None;
        _results.BackColor = Theme.Background;
        _results.ForeColor = Theme.Text;
        _results.Font = _fileFont;
        _results.DrawMode = DrawMode.OwnerDrawFixed;
        _results.ItemHeight = 64;
        _results.IntegralHeight = false;
        _results.SelectionMode = SelectionMode.One;
        _results.TabStop = false;
        _results.HorizontalScrollbar = false;
        _results.HandleCreated += (_, _) => NativeMethods.ApplyDarkExplorerTheme(_results.Handle);
        _results.Scrolled += (_, _) => _scrollIndicator.RefreshFromList();
        _results.SelectedIndexChanged += (_, _) => _scrollIndicator.RefreshFromList();
        _listClip.Controls.Add(_results);

        _scrollIndicator.Attach(_results);
        _scrollIndicator.Visible = true;
        _listClip.Controls.Add(_scrollIndicator);
        _scrollIndicator.BringToFront();

        _emptyView.Dock = DockStyle.Fill;
        _emptyView.ActionClicked += (_, _) => EmptyViewAction();
        _contentHost.Controls.Add(_emptyView);
        _emptyView.BringToFront();

        _emptyAction.Text = "连接 DeepSeek";
        _emptyAction.Font = Theme.UiFont(9.5f, FontStyle.Bold);
        _emptyAction.FlatStyle = FlatStyle.Flat;
        _emptyAction.FlatAppearance.BorderSize = 0;
        _emptyAction.BackColor = Theme.Accent;
        _emptyAction.ForeColor = Color.FromArgb(12, 20, 34);
        _emptyAction.Size = new Size(186, 38);
        _emptyAction.Cursor = Cursors.Hand;
        _emptyAction.Visible = false;
        _emptyAction.Click += (_, _) => EmptyViewAction();
        _contentHost.Controls.Add(_emptyAction);
        _emptyAction.BringToFront();
        _emptyView.Resize += (_, _) => LayoutEmptyAction();

        _results.Visible = false;

        _resultsHost.Controls.Add(_tabBar);
        _tabBar.BringToFront();
        LayoutResultsList();
        UpdateTabs();
    }

    private void LayoutEmptyAction()
    {
        if (!_emptyAction.Visible)
        {
            return;
        }

        Rectangle area = _emptyView.Bounds;
        _emptyAction.Location = new Point(
            area.Left + Math.Max(0, (area.Width - _emptyAction.Width) / 2),
            area.Top + area.Height / 2 + 80);
        _emptyAction.BringToFront();
    }

    private void LayoutTabBar()
    {
        _directTab.Location = new Point(0, 6);
        _aiTab.Location = new Point(_directTab.Right + 6, 6);
        _aiStatus.Size = new Size(238, 30);
        _aiStatus.Location = new Point(Math.Max(_aiTab.Right + 8, _tabBar.Width - 22 - _aiStatus.Width), 7);
        _contentHost.Padding = new Padding(0, _tabBar.Height, 0, 0);
    }

    private void UpdateTabs()
    {
        bool hasQuery = _query.Length > 0;
        int directCount = hasQuery && _directResult is not null ? _directResult.TotalMatches : -1;
        int aiCount = hasQuery && _aiConnected && _aiResult is not null ? _aiResult.TotalMatches : -1;

        _directTab.SetState("文件名匹配", directCount, _activeGroup == SearchGroup.Direct);
        _aiTab.SetState("AI 相关", aiCount, _activeGroup == SearchGroup.Ai);
        LayoutTabBar();
        UpdateAiStatus();
    }

    private void UpdateAiStatus()
    {
        if (_isConnecting)
        {
            _aiStatus.Text = "正在连接 DeepSeek…";
            _aiStatus.ForeColor = Theme.TextMuted;
            return;
        }

        if (_isAiRunning)
        {
            _aiStatus.Text = "DeepSeek 分析中…";
            _aiStatus.ForeColor = Theme.TextMuted;
            return;
        }

        if (_aiConnected)
        {
            _aiStatus.Text = "DeepSeek 已连接";
            _aiStatus.ForeColor = Color.FromArgb(112, 182, 142);
            return;
        }

        _aiStatus.Text = "连接 DeepSeek";
        _aiStatus.ForeColor = Theme.Accent;
    }

    private void LayoutResultsList()
    {
        int visibleWidth = _listClip.ClientSize.Width;
        _results.Location = Point.Empty;
        _results.Size = new Size(
            visibleWidth + SystemInformation.VerticalScrollBarWidth + 6,
            _listClip.ClientSize.Height);
        _scrollIndicator.Bounds = new Rectangle(Math.Max(0, visibleWidth - 6), 0, 6, _listClip.ClientSize.Height);
        _scrollIndicator.BringToFront();
    }

    private void BuildFooter()
    {
        _footer.Dock = DockStyle.Bottom;
        _footer.Height = 42;
        _footer.BackColor = Theme.Background;
        _footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 22, 0, _footer.Width - 22, 0);
        };

        _status.AutoSize = false;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Theme.TextMuted;
        _status.Font = Theme.UiFont(8.5f);
        _status.AutoEllipsis = true;
        _footer.Controls.Add(_status);

        _hint.AutoSize = false;
        _hint.TextAlign = ContentAlignment.MiddleRight;
        _hint.ForeColor = Theme.TextFaint;
        _hint.Font = Theme.UiFont(8.5f);
        _hint.AutoEllipsis = true;
        _footer.Controls.Add(_hint);

        LayoutFooter();
        UpdateStatus();
    }

    private void LayoutFooter()
    {
        int width = _footer.ClientSize.Width;
        _hint.Text = width < 790
            ? "Enter 打开  ·  Esc 隐藏"
            : "↑↓ 选择   ·   Enter 打开   ·   Ctrl+Enter 定位   ·   Esc 隐藏";
        _hint.Size = new Size(width < 790 ? 210 : 370, 40);
        _hint.Location = new Point(Math.Max(0, width - 22 - _hint.Width), 1);
        _status.Location = new Point(22, 1);
        _status.Size = new Size(Math.Max(0, _hint.Left - 34), 40);
    }

    private NotifyIcon BuildTrayIcon()
    {
        var menu = CreateDarkMenu();
        var show = CreateMenuItem("显示闪查", "Ctrl+Alt+Space");
        show.Font = Theme.UiFont(9.5f, FontStyle.Bold);
        show.Click += (_, _) => ShowAndFocus();
        menu.Items.Add(show);
        menu.Items.Add(new ToolStripSeparator());

        var rebuild = CreateMenuItem("重建文件索引", "F5");
        rebuild.Click += (_, _) => BeginRebuild();
        menu.Items.Add(rebuild);

        var deepSeek = CreateMenuItem("连接网页版 DeepSeek");
        deepSeek.Click += (_, _) =>
        {
            ShowAndFocus();
            _ = ConnectDeepSeekAsync();
        };
        menu.Items.Add(deepSeek);

        var openData = CreateMenuItem("打开数据目录");
        openData.Click += (_, _) => OpenDataDirectory();
        menu.Items.Add(openData);

        menu.Items.Add(new ToolStripSeparator());
        var exit = CreateMenuItem("退出");
        exit.Click += (_, _) => Close();
        menu.Items.Add(exit);

        var tray = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "闪查 QuickFind · 本地文件定位",
            ContextMenuStrip = menu,
            Visible = false
        };
        tray.DoubleClick += (_, _) => ShowAndFocus();
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowAndFocus();
            }
        };
        return tray;
    }

    private ContextMenuStrip BuildResultMenu()
    {
        var menu = CreateDarkMenu();

        var open = CreateMenuItem("打开文件", "Enter");
        open.Click += (_, _) => OpenSelected(reveal: false);
        menu.Items.Add(open);

        var reveal = CreateMenuItem("打开所在位置", "Ctrl+Enter");
        reveal.Click += (_, _) => OpenSelected(reveal: true);
        menu.Items.Add(reveal);
        menu.Items.Add(new ToolStripSeparator());

        var copyPath = CreateMenuItem("复制完整路径", "Ctrl+C");
        copyPath.Click += (_, _) => CopySelectedPath();
        menu.Items.Add(copyPath);

        var copyName = CreateMenuItem("复制文件名");
        copyName.Click += (_, _) => CopySelectedName();
        menu.Items.Add(copyName);

        return menu;
    }

    private static ContextMenuStrip CreateDarkMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Theme.UiFont(9.5f),
            ShowImageMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new DarkColorTable()),
            Padding = new Padding(4, 5, 4, 5)
        };
        return menu;
    }

    private static ToolStripMenuItem CreateMenuItem(string text, string? shortcut = null)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            Padding = new Padding(8, 4, 8, 4),
            ShortcutKeyDisplayString = shortcut
        };
        return item;
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color MenuItemSelected => Theme.SelectionStrong;
        public override Color MenuItemSelectedGradientBegin => Theme.SelectionStrong;
        public override Color MenuItemSelectedGradientEnd => Theme.SelectionStrong;
        public override Color MenuItemBorder => Theme.SelectionStrong;
        public override Color MenuBorder => Theme.BorderStrong;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.EnableRoundedCorners(Handle);
        RegisterHotkey();
        _results.ItemHeight = Math.Max(52, (int)(64 * DeviceDpi / 96f));
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_hotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            _hotkeyRegistered = false;
        }

        base.OnHandleDestroyed(e);
    }

    private void RegisterHotkey()
    {
        _hotkeyRegistered = NativeMethods.RegisterHotKey(
            Handle, HotkeyId, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_SPACE);
        _hotkeyText = "Ctrl + Alt + Space";

        if (!_hotkeyRegistered)
        {
            _hotkeyRegistered = NativeMethods.RegisterHotKey(
                Handle, HotkeyId, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
                NativeMethods.VK_F);
            _hotkeyText = "Ctrl + Alt + F";
        }

        _hotkeyLabel.Text = _hotkeyRegistered ? $"{_hotkeyText} 唤起" : "全局热键被占用";
        _hotkeyLabel.ForeColor = _hotkeyRegistered ? Theme.TextFaint : Color.FromArgb(200, 135, 90);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_isPositioned)
        {
            _isPositioned = true;
            Location = SuggestedLocation();
        }

        _trayIcon.Visible = true;
        _searchShell.Box.Focus();

        if (!_loadedOnce)
        {
            _loadedOnce = true;
            _ = LoadAndRefreshAsync();
        }
    }

    private Point SuggestedLocation()
    {
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        int x = area.Left + Math.Max(0, (area.Width - Width) / 2);
        int y = area.Top + Math.Max(30, (int)(area.Height * 0.14));
        return new Point(x, y);
    }

    private async Task LoadAndRefreshAsync()
    {
        _status.Text = "正在加载本地索引…";
        FileIndex? cached = null;

        if (File.Exists(_indexPath))
        {
            try
            {
                cached = await Task.Run(() => FileIndex.TryLoad(_indexPath));
            }
            catch
            {
                cached = null;
            }
        }

        if (cached is { Count: > 0 } && !IsDisposed)
        {
            _index = cached;
            UpdateStatus();
            if (_searchShell.Box.Text.Trim().Length > 0)
            {
                _ = SearchAsync(immediate: true);
            }

            TimeSpan age = DateTime.UtcNow - GetLastScanUtc();
            if (age >= TimeSpan.Zero && age < AutoRefreshAge)
            {
                // The cache is fresh; a full rescan on every launch would waste disk I/O.
                return;
            }
        }
        else
        {
            UpdateFirstRunEmpty();
        }

        await RefreshIndexAsync();
    }

    private async Task RefreshIndexAsync()
    {
        if (_isIndexing || _isSaving || IsDisposed)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _indexCts = cts;
        _isIndexing = true;
        _indexProgress = new IndexProgress(0, string.Empty, string.Empty);
        int runId = ++_indexRunId;
        ulong previousSignature = _index.Signature;

        UpdateStatus();
        UpdateFirstRunEmpty();

        var progress = new Progress<IndexProgress>(p =>
        {
            if (runId != _indexRunId || IsDisposed)
            {
                return;
            }

            _indexProgress = p;
            UpdateStatus();
            if (_searchShell.Box.Text.Trim().Length == 0 && _index.Count == 0)
            {
                UpdateFirstRunEmpty();
            }
        });

        try
        {
            var built = await Task.Factory.StartNew(
                () =>
                {
                    IndexBuildResult result = IndexBuilder.Build(progress, cts.Token);
                    cts.Token.ThrowIfCancellationRequested();
                    FileIndex snapshot = FileIndex.FromPaths(result.Paths, result.Signature);
                    return (Snapshot: snapshot, Result: result);
                },
                cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            if (cts.IsCancellationRequested || IsDisposed)
            {
                return;
            }

            _index = built.Snapshot;
            _isIndexing = false;
            _indexProgress = new IndexProgress(built.Result.Paths.Count, string.Empty, string.Empty);
            UpdateStatus();
            ResetEmptyView();

            bool needsSave = built.Snapshot.Signature != previousSignature || !File.Exists(_indexPath);
            bool cacheReady = !needsSave;
            if (needsSave)
            {
                _isSaving = true;
                try
                {
                    _status.Text = $"索引已更新（{_index.Count:N0} 个文件），正在保存…";
                    await Task.Run(() => _index.Save(_indexPath), CancellationToken.None);
                    cacheReady = true;
                }
                catch (Exception ex)
                {
                    LogError("保存索引失败", ex);
                    _status.Text = "索引已更新，但缓存保存失败（下次启动会重新扫描）";
                }
                finally
                {
                    _isSaving = false;
                }

                if (!IsDisposed)
                {
                    UpdateStatus();
                }
            }

            if (cacheReady)
            {
                MarkScanComplete();
            }

            if (!IsDisposed && _searchShell.Box.Text.Trim().Length > 0)
            {
                _ = SearchAsync(immediate: true);
            }
        }
        catch (OperationCanceledException)
        {
            // A new scan or shutdown superseded this one.
        }
        catch (Exception ex)
        {
            LogError("建立索引失败", ex);
            if (!IsDisposed)
            {
                _status.Text = "建立索引失败：" + ex.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_indexCts, cts))
            {
                _indexCts = null;
                _isIndexing = false;
                if (!IsDisposed)
                {
                    UpdateStatus();
                }
            }

            cts.Dispose();
        }
    }

    private async Task SearchAsync(bool immediate)
    {
        int version = ++_searchVersion;
        string query = _searchShell.Box.Text.Trim();
        _searchCts?.Cancel();
        _query = query;

        _aiCts?.Cancel();
        _aiVersion++;
        _isAiRunning = false;
        _directResult = null;
        _directStatus = string.Empty;
        _aiResult = null;
        _aiStatusText = string.Empty;
        _aiError = string.Empty;
        _aiKeywords = Array.Empty<string>();
        _activeGroup = SearchGroup.Direct;

        if (query.Length == 0)
        {
            _results.Items.Clear();
            _results.Visible = false;
            UpdateTabs();
            ResetEmptyView();
            UpdateStatus();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        UpdateTabs();
        RefreshResultsView();

        try
        {
            if (!immediate)
            {
                await Task.Delay(65, cts.Token);
            }

            if (version != _searchVersion || query != _searchShell.Box.Text.Trim())
            {
                return;
            }

            FileIndex snapshot = _index;
            var clock = Stopwatch.StartNew();
            SearchResult result = await Task.Run(
                () => snapshot.Search(query, 240, cts.Token, nameOnly: true), cts.Token);
            clock.Stop();

            if (version != _searchVersion || IsDisposed)
            {
                return;
            }

            _directResult = result;
            _directSelection = 0;
            _directStatus = result.TotalMatches > result.Hits.Length
                ? $"{result.TotalMatches:N0} 个文件名匹配 · 显示前 {result.Hits.Length:N0} 个 · {clock.Elapsed.TotalMilliseconds:0} ms"
                : $"文件名匹配 {result.TotalMatches:N0} 个 · 共 {_index.Count:N0} 个文件 · {clock.Elapsed.TotalMilliseconds:0} ms";

            UpdateTabs();
            RefreshResultsView();
            UpdateStatus();
            ScheduleAiAnalysis(query);
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer input.
        }
        catch (Exception ex)
        {
            LogError("搜索失败", ex);
            _directStatus = "搜索失败：" + ex.Message;
            _status.Text = _directStatus;
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                _searchCts = null;
            }
        }
    }

    private void ScheduleAiAnalysis(string query)
    {
        if (!_aiConnected || _deepSeek is null || query.Length == 0)
        {
            UpdateTabs();
            return;
        }

        StartAiAnalysis(query);
    }

    private void StartAiAnalysis(string query)
    {
        if (!_aiConnected || _deepSeek is null || query.Length == 0)
        {
            return;
        }

        _aiCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _aiCts = cts;
        int version = ++_aiVersion;
        _isAiRunning = true;
        _aiResult = null;
        _aiError = string.Empty;
        _aiKeywords = Array.Empty<string>();
        _aiStatusText = "正在把搜索词交给 DeepSeek…";

        UpdateTabs();
        if (_activeGroup == SearchGroup.Ai)
        {
            RefreshResultsView();
        }

        UpdateStatus();
        _ = AnalyzeWithDeepSeekAsync(query, version, cts);
    }

    private async Task AnalyzeWithDeepSeekAsync(string query, int version, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(450, cts.Token);
            DeepSeekSession session = _deepSeek ?? throw new InvalidOperationException("DeepSeek 未连接。");
            IReadOnlyList<string> keywords = await session.ExpandQueryAsync(query, cts.Token);

            if (version != _aiVersion || cts.IsCancellationRequested)
            {
                return;
            }

            _aiKeywords = keywords;
            _aiStatusText = "DeepSeek 已返回扩展词，正在匹配本地文件…";
            UpdateStatus();

            string[] directTerms = query.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            SearchResult result = await Task.Run(
                () => _index.SearchRelated(keywords, directTerms, 120, cts.Token), cts.Token);

            if (version != _aiVersion || cts.IsCancellationRequested || IsDisposed)
            {
                return;
            }

            _aiResult = result;
            _aiStatusText = result.TotalMatches > 0
                ? $"AI 相关 {result.TotalMatches:N0} 个 · 扩展词 {keywords.Count} 个"
                : "没有找到 AI 相关文件";

            UpdateTabs();
            if (_activeGroup == SearchGroup.Ai)
            {
                RefreshResultsView();
            }
            else if (result.Hits.Length > 0 && (_directResult?.Hits.Length ?? 0) == 0)
            {
                SwitchGroup(SearchGroup.Ai);
            }

            UpdateStatus();
        }
        catch (OperationCanceledException)
        {
            // A newer query or shutdown canceled this analysis.
        }
        catch (Exception ex)
        {
            if (version != _aiVersion || IsDisposed)
            {
                return;
            }

            LogError("DeepSeek 分析失败", ex);
            _aiError = ex.Message;
            _aiStatusText = "AI 分析失败";
            if (ex.Message.Contains("登录", StringComparison.Ordinal) ||
                ex.Message.Contains("输入框", StringComparison.Ordinal))
            {
                _aiConnected = false;
            }

            UpdateTabs();
            if (_activeGroup == SearchGroup.Ai)
            {
                RefreshResultsView();
            }

            UpdateStatus();
        }
        finally
        {
            if (ReferenceEquals(_aiCts, cts))
            {
                _aiCts = null;
            }

            if (version == _aiVersion)
            {
                _isAiRunning = false;
                UpdateTabs();
                if (_activeGroup == SearchGroup.Ai && !IsDisposed)
                {
                    RefreshResultsView();
                }

                UpdateStatus();
            }

            cts.Dispose();
        }
    }

    private void SwitchGroup(SearchGroup group)
    {
        if (_activeGroup == group)
        {
            RefreshResultsView();
            return;
        }

        SaveSelection();
        _activeGroup = group;

        if (group == SearchGroup.Ai && _aiConnected && _query.Length > 0 &&
            _aiResult is null && !_isAiRunning && string.IsNullOrEmpty(_aiError))
        {
            StartAiAnalysis(_query);
        }

        UpdateTabs();
        RefreshResultsView();
        UpdateStatus();
    }

    private void SaveSelection()
    {
        if (_activeGroup == SearchGroup.Direct)
        {
            _directSelection = _results.SelectedIndex;
        }
        else
        {
            _aiSelection = _results.SelectedIndex;
        }
    }

    private void RefreshResultsView()
    {
        _results.BeginUpdate();
        try
        {
            _results.Items.Clear();

            if (_activeGroup == SearchGroup.Direct)
            {
                if (_query.Length == 0)
                {
                    ShowEmpty(
                        "输入文件名，立即定位",
                        "支持空格拆分多个关键词，例如“项目 报告”；AI 相关结果在另一个分栏",
                        null);
                    return;
                }

                if (_directResult is null)
                {
                    ShowEmpty("正在搜索文件名…", "结果会实时出现", null);
                    return;
                }

                if (_directResult.Hits.Length == 0)
                {
                    if (_index.Count == 0 && _isIndexing)
                    {
                        ShowEmpty("正在建立本地索引", "首次索引需要一点时间，完成后会自动显示结果", null);
                    }
                    else if (_index.Count == 0)
                    {
                        ShowEmpty("索引还是空的", "按 F5 重新建立索引，或稍后再试", null);
                    }
                    else
                    {
                        ShowEmpty(
                            "文件名中没有找到匹配项",
                            "可以切换到“AI 相关”，让 DeepSeek 按语义查找",
                            _aiConnected ? "查看 AI 相关" : "连接 DeepSeek");
                    }

                    return;
                }

                AddResultRows(_directResult.Hits, _directSelection);
                return;
            }

            if (!_aiConnected)
            {
                ShowEmpty(
                    "用 DeepSeek 查找相关文件",
                    "登录一次网页版 DeepSeek，之后搜索词会自动扩展同义词；搜索词会发送给 DeepSeek",
                    "连接 DeepSeek");
                return;
            }

            if (_query.Length == 0)
            {
                ShowEmpty("输入关键词，开始 AI 相关搜索", "DeepSeek 会把关键词扩展成相关词，再匹配本地文件名和路径", null);
                return;
            }

            if (_isAiRunning)
            {
                ShowEmpty(
                    "DeepSeek 正在分析关键词…",
                    "正在理解搜索词并扩展相关词，请稍候",
                    null);
                return;
            }

            if (!string.IsNullOrEmpty(_aiError))
            {
                ShowEmpty("AI 相关搜索失败", _aiError, "重试");
                return;
            }

            if (_aiResult is null)
            {
                ShowEmpty("还没有生成 AI 相关结果", "点击按钮让 DeepSeek 分析当前搜索词", "开始分析");
                return;
            }

            if (_aiResult.Hits.Length == 0)
            {
                string terms = _aiKeywords.Count > 0
                    ? "扩展词：" + string.Join(" · ", _aiKeywords.Take(8))
                    : "DeepSeek 没有找到更多相关词";
                ShowEmpty("没有找到 AI 相关文件", terms, "重新分析");
                return;
            }

            AddResultRows(_aiResult.Hits, _aiSelection);
        }
        finally
        {
            _results.EndUpdate();
        }
    }

    private void AddResultRows(IReadOnlyList<SearchHit> hits, int selection)
    {
        _results.Visible = true;
        _emptyView.Visible = false;
        _emptyAction.Visible = false;
        _results.TopIndex = 0;
        foreach (SearchHit hit in hits)
        {
            _results.Items.Add(new ResultRow(hit.Path, hit.NameText, hit.Folder));
        }

        if (_results.Items.Count > 0)
        {
            _results.SelectedIndex = Math.Clamp(selection, 0, _results.Items.Count - 1);
            _results.TopIndex = 0;
        }

        _scrollIndicator.RefreshFromList();
    }

    private void ShowEmpty(string title, string subtitle, string? action)
    {
        _results.Visible = false;
        _emptyView.SetText(title, subtitle);
        _emptyView.Visible = true;
        _emptyView.BringToFront();

        if (string.IsNullOrEmpty(action))
        {
            _emptyAction.Visible = false;
        }
        else
        {
            _emptyAction.Text = action;
            _emptyAction.Visible = true;
            LayoutEmptyAction();
            _emptyAction.BringToFront();
        }

        _scrollIndicator.RefreshFromList();
    }

    private void EmptyViewAction()
    {
        if (_activeGroup == SearchGroup.Direct)
        {
            if (_aiConnected)
            {
                SwitchGroup(SearchGroup.Ai);
            }
            else
            {
                _ = ConnectDeepSeekAsync();
            }

            return;
        }

        if (!_aiConnected)
        {
            _ = ConnectDeepSeekAsync();
            return;
        }

        if (!_isAiRunning && _query.Length > 0)
        {
            StartAiAnalysis(_query);
        }
    }

    private async Task AiStatusClickedAsync()
    {
        if (_isConnecting)
        {
            return;
        }

        if (_aiConnected)
        {
            _deepSeek?.ShowWindow(this);
            return;
        }

        await ConnectDeepSeekAsync();
    }

    private async Task ConnectDeepSeekAsync()
    {
        if (_isConnecting)
        {
            return;
        }

        _isConnecting = true;
        _aiError = string.Empty;
        UpdateTabs();
        UpdateStatus();

        try
        {
            _deepSeek ??= new DeepSeekSession();
            bool connected = await _deepSeek.EnsureLoggedInAsync(this, _lifetimeCts.Token);
            _aiConnected = connected;

            if (connected)
            {
                _aiStatusText = "DeepSeek 已连接";
                if (_query.Length > 0)
                {
                    StartAiAnalysis(_query);
                }
                else if (_activeGroup == SearchGroup.Ai)
                {
                    RefreshResultsView();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // App is closing.
        }
        catch (Exception ex)
        {
            LogError("连接 DeepSeek 失败", ex);
            _aiError = ex.Message;
            _aiConnected = false;
        }
        finally
        {
            _isConnecting = false;
            UpdateTabs();
            if (_activeGroup == SearchGroup.Ai)
            {
                RefreshResultsView();
            }

            UpdateStatus();
        }
    }

    private void UpdateStatus()
    {
        if (_isIndexing)
        {
            string count = _indexProgress.Count > 0 ? $" · {_indexProgress.Count:N0} 个文件" : string.Empty;
            if (_searchShell.Box.Text.Trim().Length == 0)
            {
                _status.Text = $"正在更新本地索引{count}…";
            }
            else
            {
                _status.Text = CurrentActivityText() + " · 索引更新中";
            }

            return;
        }

        if (_searchShell.Box.Text.Trim().Length == 0)
        {
            _status.Text = _index.Count > 0
                ? $"索引就绪 · {_index.Count:N0} 个文件 · 双击结果即可打开"
                : "索引为空 · 按 F5 重建索引";
            return;
        }

        _status.Text = CurrentActivityText();
    }

    private string CurrentActivityText()
    {
        if (_activeGroup == SearchGroup.Direct)
        {
            return string.IsNullOrEmpty(_directStatus) ? "正在搜索文件名…" : _directStatus;
        }

        if (!_aiConnected)
        {
            return "连接网页版 DeepSeek 后，可按语义查找相关文件";
        }

        if (_isAiRunning)
        {
            return "DeepSeek 正在扩展关键词并匹配本地文件…";
        }

        if (!string.IsNullOrEmpty(_aiError))
        {
            return "AI 分析失败：" + _aiError;
        }

        return string.IsNullOrEmpty(_aiStatusText) ? "输入关键词，开始 AI 相关搜索" : _aiStatusText;
    }

    private void UpdateFirstRunEmpty()
    {
        if (_activeGroup != SearchGroup.Direct)
        {
            return;
        }

        if (_searchShell.Box.Text.Trim().Length > 0 || _index.Count > 0)
        {
            return;
        }

        if (_isIndexing)
        {
            _emptyView.SetText(
                "正在为这台电脑建立索引",
                _indexProgress.Count > 0
                    ? $"已收录 {_indexProgress.Count:N0} 个文件，完成后输入文件名即可秒搜"
                    : "首次使用需要扫描本机文件，之后启动会直接读取缓存");
        }
        else
        {
            _emptyView.SetText("输入文件名，立即定位", "支持空格拆分多个关键词，例如“项目 报告”");
        }
    }

    private void ResetEmptyView()
    {
        if (_searchShell.Box.Text.Trim().Length > 0)
        {
            return;
        }

        RefreshResultsView();
    }

    private void DrawResultItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _results.Items.Count)
        {
            return;
        }

        var row = (ResultRow)_results.Items[e.Index];
        Graphics g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        bool selected = (e.State & DrawItemState.Selected) != 0;
        int visibleWidth = Math.Max(0, _listClip.ClientSize.Width - 8);
        var bounds = new Rectangle(0, e.Bounds.Top, visibleWidth, e.Bounds.Height);
        using var background = new SolidBrush(Theme.Background);
        g.FillRectangle(background, bounds);

        var card = new Rectangle(0, bounds.Top + 2, Math.Max(0, bounds.Width - 2), bounds.Height - 5);
        if (selected)
        {
            using GraphicsPath path = Theme.RoundedRectangle(card, 9);
            using var selection = new SolidBrush(Theme.Selection);
            g.FillPath(selection, path);
            using var bar = new SolidBrush(Theme.Accent);
            g.FillRectangle(
                bar,
                card.X + 1,
                card.Top + 15 * DeviceDpi / 96,
                3 * DeviceDpi / 96,
                card.Height - 30 * DeviceDpi / 96);
        }

        int badgeSize = (int)Math.Min(38 * DeviceDpi / 96f, bounds.Height - 20);
        int badgeX = 14 * DeviceDpi / 96;
        int badgeY = bounds.Top + (bounds.Height - badgeSize) / 2;
        string extension = Path.GetExtension(row.Name).TrimStart('.').ToUpperInvariant();
        Color accent = ColorForExtension(extension);
        string badgeText = BadgeTextFor(extension);
        var badge = new Rectangle(badgeX, badgeY, badgeSize, badgeSize);
        using (GraphicsPath badgePath = Theme.RoundedRectangle(badge, 9))
        using (var badgeFill = new SolidBrush(Color.FromArgb(selected ? 44 : 32, accent)))
        using (var badgeBorder = new Pen(Color.FromArgb(selected ? 95 : 58, accent)))
        {
            g.FillPath(badgeFill, badgePath);
            g.DrawPath(badgeBorder, badgePath);
        }

        var badgeTextRect = new Rectangle(badge.X, badge.Y + 1, badge.Width, badge.Height - 2);
        TextRenderer.DrawText(
            g, badgeText, _badgeFont, badgeTextRect, accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        int textX = badge.Right + 14 * DeviceDpi / 96;
        int textWidth = Math.Max(20, bounds.Width - textX - 18 * DeviceDpi / 96);
        var nameRect = new Rectangle(textX, bounds.Top + 9 * DeviceDpi / 96, textWidth, 24 * DeviceDpi / 96);
        TextRenderer.DrawText(
            g, row.Name, selected ? _fileSelectedFont : _fileFont, nameRect,
            selected ? Theme.Text : Color.FromArgb(232, 237, 245),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        var pathRect = new Rectangle(textX, bounds.Top + 32 * DeviceDpi / 96, textWidth, 20 * DeviceDpi / 96);
        TextRenderer.DrawText(
            g, row.Folder, _pathFont, pathRect,
            selected ? Color.FromArgb(166, 185, 214) : Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
    }

    private static string BadgeTextFor(string extension) => extension switch
    {
        "" => "FILE",
        "CSPROJ" or "SLN" or "VBPROJ" or "VCXPROJ" => "VS",
        "DOCX" => "DOC",
        "XLSX" => "XLS",
        "PPTX" => "PPT",
        "JPEG" => "JPG",
        "WEBP" or "HEIC" or "AVIF" => "IMG",
        "HTML" => "HTM",
        _ => extension.Length <= 4 ? extension : extension[..4]
    };

    private static Color ColorForExtension(string extension) => extension switch
    {
        "PDF" => Color.FromArgb(232, 104, 104),
        "DOC" or "DOCX" or "RTF" or "TXT" or "MD" => Color.FromArgb(104, 158, 245),
        "XLS" or "XLSX" or "CSV" => Color.FromArgb(96, 190, 132),
        "PPT" or "PPTX" => Color.FromArgb(236, 146, 92),
        "JPG" or "JPEG" or "PNG" or "GIF" or "WEBP" or "BMP" or "SVG" or "HEIC" or "AVIF" => Color.FromArgb(190, 140, 246),
        "MP4" or "MKV" or "AVI" or "MOV" or "WMV" or "FLV" => Color.FromArgb(232, 120, 170),
        "MP3" or "FLAC" or "WAV" or "M4A" or "AAC" => Color.FromArgb(236, 174, 92),
        "ZIP" or "RAR" or "7Z" or "TAR" or "GZ" => Color.FromArgb(214, 184, 96),
        "EXE" or "MSI" or "BAT" or "CMD" or "PS1" => Color.FromArgb(90, 200, 190),
        "CS" or "JS" or "TS" or "PY" or "JAVA" or "CPP" or "H" or "HTML" or "CSS" or "JSON" or "XML"
            or "CSPROJ" or "SLN" or "VBPROJ" or "VCXPROJ" => Color.FromArgb(98, 180, 240),
        _ => Color.FromArgb(128, 154, 190)
    };

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && _results.Items.Count > 0)
        {
            _results.Focus();
            _results.SelectedIndex = 0;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            OpenSelected(reveal: e.Control);
            e.SuppressKeyPress = true;
        }
    }

    private void Results_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            OpenSelected(reveal: e.Control);
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Up && _results.SelectedIndex <= 0)
        {
            _searchShell.Box.Focus();
            _searchShell.Box.SelectionStart = _searchShell.Box.TextLength;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.C)
        {
            CopySelectedPath();
            e.SuppressKeyPress = true;
        }
    }

    private void Results_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar) || char.IsSurrogate(e.KeyChar))
        {
            return;
        }

        _searchShell.Box.Focus();
        _searchShell.Box.SelectionStart = _searchShell.Box.TextLength;
        _searchShell.Box.SelectedText = e.KeyChar.ToString();
        e.Handled = true;
    }

    private void OpenSelected(bool reveal)
    {
        if (_results.SelectedItem is not ResultRow row)
        {
            return;
        }

        if (!File.Exists(row.Path))
        {
            _status.Text = "文件已不存在，索引可能已过期 · 按 F5 重建索引";
            return;
        }

        try
        {
            var startInfo = reveal
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{row.Path}\"") { UseShellExecute = true }
                : new ProcessStartInfo(row.Path) { UseShellExecute = true };
            Process.Start(startInfo);
            HideToTray();
        }
        catch (Exception ex)
        {
            _status.Text = "无法打开：" + ex.Message;
        }
    }

    private void CopySelectedPath()
    {
        if (_results.SelectedItem is not ResultRow row)
        {
            return;
        }

        try
        {
            Clipboard.SetText(row.Path);
            _status.Text = "已复制完整路径";
        }
        catch (Exception ex)
        {
            _status.Text = "复制失败：" + ex.Message;
        }
    }

    private void CopySelectedName()
    {
        if (_results.SelectedItem is not ResultRow row)
        {
            return;
        }

        try
        {
            Clipboard.SetText(row.Name);
            _status.Text = "已复制文件名";
        }
        catch (Exception ex)
        {
            _status.Text = "复制失败：" + ex.Message;
        }
    }

    private void Header_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, NativeMethods.HTCAPTION, IntPtr.Zero);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            ToggleWindow();
            return;
        }

        if (m.Msg == NativeMethods.WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result == NativeMethods.HTCLIENT && WindowState == FormWindowState.Normal)
            {
                long packed = m.LParam.ToInt64();
                int screenX = unchecked((short)(packed & 0xFFFF));
                int screenY = unchecked((short)((packed >> 16) & 0xFFFF));
                Point point = PointToClient(new Point(screenX, screenY));

                bool left = point.X < ResizeGrip;
                bool right = point.X >= ClientSize.Width - ResizeGrip;
                bool top = point.Y < ResizeGrip;
                bool bottom = point.Y >= ClientSize.Height - ResizeGrip;

                m.Result = (left, top) switch
                {
                    (true, true) => (IntPtr)NativeMethods.HTTOPLEFT,
                    (true, false) when bottom => (IntPtr)NativeMethods.HTBOTTOMLEFT,
                    (true, false) => (IntPtr)NativeMethods.HTLEFT,
                    (false, true) when right => (IntPtr)NativeMethods.HTTOPRIGHT,
                    (false, true) => (IntPtr)NativeMethods.HTTOP,
                    (false, false) when right && bottom => (IntPtr)NativeMethods.HTBOTTOMRIGHT,
                    (false, false) when right => (IntPtr)NativeMethods.HTRIGHT,
                    (false, false) when bottom => (IntPtr)NativeMethods.HTBOTTOM,
                    _ => m.Result
                };
            }

            return;
        }

        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            BeginRebuild();
            return true;
        }

        if (keyData == Keys.Escape)
        {
            HideToTray();
            return true;
        }

        if (keyData == (Keys.Control | Keys.K))
        {
            ShowAndFocus();
            _searchShell.Box.Focus();
            _searchShell.Box.SelectAll();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BeginRebuild()
    {
        if (_isIndexing || _isSaving)
        {
            _status.Text = "正在更新索引，请稍候…";
            return;
        }

        _ = RefreshIndexAsync();
    }

    private void ToggleWindow()
    {
        if (Visible && NativeMethods.GetForegroundWindow() == Handle)
        {
            HideToTray();
        }
        else
        {
            ShowAndFocus();
        }
    }

    private void HideToTray()
    {
        if (!Visible)
        {
            return;
        }

        Hide();

        if (!_balloonShown && _trayIcon.Visible)
        {
            _balloonShown = true;
            try
            {
                _trayIcon.ShowBalloonTip(
                    1800, "闪查仍在运行", $"按 {_hotkeyText} 可随时唤起。", ToolTipIcon.Info);
            }
            catch
            {
                // Notifications may be disabled.
            }
        }
    }

    private void ShowAndFocus()
    {
        if (IsDisposed)
        {
            return;
        }

        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        NativeMethods.SetForegroundWindow(Handle);
        Activate();
        BringToFront();
        _searchShell.Box.Focus();
        _searchShell.Box.SelectAll();
    }

    private void OpenDataDirectory()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _status.Text = "无法打开数据目录：" + ex.Message;
        }
    }

    private void StartShowListener()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _showEvent.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (IsDisposed)
                {
                    break;
                }

                try
                {
                    BeginInvoke(new Action(ShowAndFocus));
                }
                catch (InvalidOperationException)
                {
                    break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "QuickFind.ShowListener"
        };
        thread.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Theme.BorderStrong);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _lifetimeCts.Cancel();
        _indexCts?.Cancel();
        _searchCts?.Cancel();
        _aiCts?.Cancel();
        _trayIcon.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deepSeek?.Dispose();
            _lifetimeCts.Dispose();
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _toolTip.Dispose();
            _indexCts?.Dispose();
            _searchCts?.Dispose();
            _aiCts?.Dispose();
            _fileFont.Dispose();
            _fileSelectedFont.Dispose();
            _pathFont.Dispose();
            _badgeFont.Dispose();
            _appIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private static DateTime GetLastScanUtc()
    {
        try
        {
            string path = File.Exists(LastScanPath) ? LastScanPath : IndexPath;
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static void MarkScanComplete()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(LastScanPath, DateTime.UtcNow.ToString("O"));
        }
        catch
        {
            // If this fails we simply rescan next time.
        }
    }

    private static Icon LoadAppIcon()    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("QuickFind.assets.app.ico");
            if (stream is not null)
            {
                return new Icon(stream);
            }
        }
        catch
        {
            // Fall through to the system icon.
        }

        return SystemIcons.Application;
    }

    private static void LogError(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(
                Path.Combine(DataDirectory, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Ignore logging failures.
        }
    }

    private sealed record ResultRow(string Path, string Name, string Folder);

    private enum SearchGroup
    {
        Direct,
        Ai
    }
}
