using System.ComponentModel;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QuickFind;

internal sealed class DeepSeekWindow : Form
{
    public const string StartUrl = "https://chat.deepseek.com/";

    private readonly WebView2 _webView = new();
    private readonly Label _heading = new();
    private readonly Label _hint = new();
    private readonly Panel _topBar = new();
    private readonly Panel _bottomBar = new();
    private readonly Label _status = new();
    private readonly Button _confirmButton = new();
    private readonly Button _cancelButton = new();
    private TaskCompletionSource<bool>? _loginCompletion;
    private Task? _initialization;
    private CancellationTokenSource? _loginPollCts;
    private bool _allowClose;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<Task<bool>>? VerifyLoginAsync { get; set; }

    public bool IsInitialized =>
        _webView.CoreWebView2 is not null && _webView.Source is not null;

    public DeepSeekWindow()
    {
        Text = "网页版 DeepSeek · 闪查";
        ClientSize = new Size(980, 760);
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(9f);
        ShowInTaskbar = true;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // Keep the default icon.
        }

        BuildTopBar();
        BuildBottomBar();

        _webView.Dock = DockStyle.Fill;
        _webView.BackColor = Color.White;
        Controls.Add(_webView);
        Controls.Add(_bottomBar);
        Controls.Add(_topBar);

        FormClosing += OnFormClosing;
        Shown += (_, _) => _ = InitializeAsync();
    }

    private void BuildTopBar()
    {
        _topBar.Dock = DockStyle.Top;
        _topBar.Height = 72;
        _topBar.BackColor = Theme.Header;
        _topBar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);
        };

        _heading.Text = "网页版 DeepSeek";
        _heading.Font = Theme.UiFont(12.5f, FontStyle.Bold);
        _heading.ForeColor = Theme.Text;
        _heading.AutoSize = true;
        _heading.Location = new Point(20, 12);
        _topBar.Controls.Add(_heading);

        _hint.Text = "首次使用请登录 DeepSeek。登录状态只保存在本机；搜索词会发给 DeepSeek 用于扩展相关关键词。";
        _hint.Font = Theme.UiFont(8.7f);
        _hint.ForeColor = Theme.TextMuted;
        _hint.AutoSize = true;
        _hint.Location = new Point(20, 41);
        _topBar.Controls.Add(_hint);
    }

    private void BuildBottomBar()
    {
        _bottomBar.Dock = DockStyle.Bottom;
        _bottomBar.Height = 60;
        _bottomBar.BackColor = Theme.Header;
        _bottomBar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, _bottomBar.Width, 0);
        };

        _status.Text = "正在加载 DeepSeek 网页…";
        _status.Font = Theme.UiFont(8.8f);
        _status.ForeColor = Theme.TextMuted;
        _status.AutoSize = false;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Location = new Point(20, 10);
        _status.Size = new Size(560, 40);
        _bottomBar.Controls.Add(_status);

        _cancelButton.Text = "取消";
        _cancelButton.Font = Theme.UiFont(9f);
        _cancelButton.FlatStyle = FlatStyle.Flat;
        _cancelButton.FlatAppearance.BorderColor = Theme.BorderStrong;
        _cancelButton.BackColor = Theme.Surface;
        _cancelButton.ForeColor = Theme.Text;
        _cancelButton.Size = new Size(88, 34);
        _cancelButton.Location = new Point(746, 13);
        _cancelButton.Cursor = Cursors.Hand;
        _cancelButton.Click += (_, _) => Complete(false);
        _bottomBar.Controls.Add(_cancelButton);

        _confirmButton.Text = "完成并开始分析";
        _confirmButton.Font = Theme.UiFont(9f, FontStyle.Bold);
        _confirmButton.FlatStyle = FlatStyle.Flat;
        _confirmButton.FlatAppearance.BorderSize = 0;
        _confirmButton.BackColor = Theme.Accent;
        _confirmButton.ForeColor = Color.FromArgb(12, 20, 34);
        _confirmButton.Size = new Size(170, 34);
        _confirmButton.Location = new Point(844, 13);
        _confirmButton.Cursor = Cursors.Hand;
        _confirmButton.Click += ConfirmButton_Click;
        _bottomBar.Controls.Add(_confirmButton);

        _bottomBar.Resize += (_, _) =>
        {
            _confirmButton.Location = new Point(_bottomBar.ClientSize.Width - 20 - _confirmButton.Width, 13);
            _cancelButton.Location = new Point(_confirmButton.Left - 10 - _cancelButton.Width, 13);
            _status.Width = Math.Max(100, _cancelButton.Left - 32);
        };
    }

    private async void ConfirmButton_Click(object? sender, EventArgs e)
    {
        _confirmButton.Enabled = false;
        _cancelButton.Enabled = false;
        _status.ForeColor = Theme.TextMuted;
        _status.Text = "正在检测登录状态…";

        try
        {
            bool loggedIn = VerifyLoginAsync is null || await VerifyLoginAsync();
            if (loggedIn)
            {
                _status.Text = "已连接 DeepSeek";
                Complete(true);
                return;
            }

            _status.ForeColor = Color.FromArgb(222, 156, 90);
            _status.Text = "还没有检测到聊天输入框，请先完成登录。";
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.FromArgb(222, 120, 120);
            _status.Text = "检测失败：" + ex.Message;
        }
        finally
        {
            _confirmButton.Enabled = true;
            _cancelButton.Enabled = true;
        }
    }

    public async Task<bool> ShowForLoginAsync(IWin32Window owner)
    {
        if (_loginCompletion is not null)
        {
            return await _loginCompletion.Task;
        }

        _loginCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _status.ForeColor = Theme.TextMuted;
        _status.Text = "正在加载 DeepSeek 网页…";
        _confirmButton.Enabled = true;
        _cancelButton.Enabled = true;

        Show(owner);
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
        _ = InitializeAsync();

        _loginPollCts?.Cancel();
        _loginPollCts = new CancellationTokenSource();
        _ = AutoDetectLoginAsync(_loginPollCts.Token);

        return await _loginCompletion.Task;
    }

    private async Task AutoDetectLoginAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _loginCompletion is not null)
            {
                await Task.Delay(700, cancellationToken);
                if (VerifyLoginAsync is not null && await VerifyLoginAsync())
                {
                    _status.ForeColor = Theme.TextMuted;
                    _status.Text = "已连接 DeepSeek";
                    Complete(true);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Login window was closed or completed.
        }
        catch
        {
            // The page may still be loading; the manual button remains available.
        }
    }

    public async Task InitializeAsync()
    {
        if (_webView.CoreWebView2 is not null)
        {
            return;
        }

        if (_initialization is not null)
        {
            await _initialization;
            return;
        }

        _initialization = InitializeCoreAsync();
        await _initialization;
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            WebView2Bootstrap.EnsureLoader();
            AppPaths.EnsureDataDirectory();
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: AppPaths.WebView2DataDirectory);

            await _webView.EnsureCoreWebView2Async(environment);
            CoreWebView2 core = _webView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.ProcessFailed += (_, e) =>
            {
                if (!IsDisposed)
                {
                    _status.ForeColor = Color.FromArgb(222, 120, 120);
                    _status.Text = "DeepSeek 网页进程异常：" + e.ProcessFailedKind;
                }
            };
            core.NavigationCompleted += (_, e) =>
            {
                if (IsDisposed)
                {
                    return;
                }

                _status.ForeColor = Theme.TextMuted;
                _status.Text = e.IsSuccess
                    ? "请登录 DeepSeek。登录后能看到聊天输入框，再点右侧按钮。"
                    : "网页加载失败，请检查网络后重试。";
            };
            core.Navigate(StartUrl);
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.FromArgb(222, 120, 120);
            _status.Text = "无法启动网页版 DeepSeek：" + ex.Message;
            _loginCompletion?.TrySetResult(false);
        }
    }

    public async Task NavigateHomeAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync();
        if (_webView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("DeepSeek 网页还没有准备好。");
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            completion.TrySetResult(e.IsSuccess);
        }

        _webView.CoreWebView2.NavigationCompleted += Handler;
        try
        {
            _webView.CoreWebView2.Navigate(StartUrl);
            await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _webView.CoreWebView2.NavigationCompleted -= Handler;
        }
    }

    public async Task<string> ExecuteScriptAsync(string script)
    {
        await InitializeAsync();
        if (_webView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("DeepSeek 网页还没有准备好。");
        }

        return await _webView.ExecuteScriptAsync(script);
    }

    public void ShowWindow(IWin32Window? owner = null)
    {
        if (IsDisposed)
        {
            return;
        }

        if (!Visible)
        {
            if (owner is not null)
            {
                Show(owner);
            }
            else
            {
                Show();
            }
        }

        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
        _ = InitializeAsync();
    }

    public void Complete(bool result)
    {
        _loginPollCts?.Cancel();
        _loginPollCts = null;
        TaskCompletionSource<bool>? completion = _loginCompletion;
        _loginCompletion = null;
        Hide();
        completion?.TrySetResult(result);
    }

    public void Shutdown()
    {
        _allowClose = true;
        _loginPollCts?.Cancel();
        _loginPollCts = null;
        _loginCompletion?.TrySetResult(false);
        _loginCompletion = null;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose || e.CloseReason == CloseReason.WindowsShutDown)
        {
            return;
        }

        e.Cancel = true;
        Complete(false);
    }
}
