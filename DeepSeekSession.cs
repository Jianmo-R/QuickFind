using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace QuickFind;

/// <summary>
/// Owns the embedded DeepSeek web session. It logs in once, then uses the visible chat
/// composer to ask DeepSeek for related search keywords.
/// </summary>
internal sealed class DeepSeekSession : IDisposable
{
    private const string ComposerCheckScript = """
        (() => {
          const selectors = [
            'textarea#chat-input',
            'textarea[placeholder]',
            '[contenteditable="true"][role="textbox"]',
            'div[contenteditable="true"]'
          ];
          for (const selector of selectors) {
            const nodes = [...document.querySelectorAll(selector)].filter(node => {
              const rect = node.getBoundingClientRect();
              return rect.width > 20 && rect.height > 10;
            });
            if (nodes.length) return true;
          }
          return false;
        })()
        """;

    private const string ReplyCountScript = """
        (() => {
          const nodes = [...document.querySelectorAll('.ds-assistant-message-main-content')];
          if (nodes.length) return nodes.length;
          return [...document.querySelectorAll('.ds-markdown')].length;
        })()
        """;

    private const string ReplyStateScript = """
        (() => {
          let nodes = [...document.querySelectorAll('.ds-assistant-message-main-content')];
          if (!nodes.length) nodes = [...document.querySelectorAll('.ds-markdown')];
          const last = nodes[nodes.length - 1];
          const text = (last?.innerText || '').trim();
          const buttons = [...document.querySelectorAll('button, [role="button"]')];
          const streaming = buttons.some(button => {
            const label = ((button.innerText || '') + ' ' + (button.getAttribute('aria-label') || '')).toLowerCase();
            return label.includes('停止') || label.includes('stop generating') || /(^|\s)stop(\s|$)/.test(label);
          });
          return { count: nodes.length, text: text, streaming: streaming };
        })()
        """;

    private const string SendScriptTemplate = """
        (() => {
          const prompt = __PROMPT__;
          const selectors = [
            'textarea#chat-input',
            'textarea[placeholder]',
            '[contenteditable="true"][role="textbox"]',
            'div[contenteditable="true"]'
          ];
          let input = null;
          for (const selector of selectors) {
            const nodes = [...document.querySelectorAll(selector)].filter(node => {
              const rect = node.getBoundingClientRect();
              return rect.width > 20 && rect.height > 10;
            });
            if (nodes.length) input = nodes[nodes.length - 1];
            if (input) break;
          }
          if (!input) return 'NO_INPUT';

          input.focus();
          if (input.tagName === 'TEXTAREA') {
            const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set;
            setter.call(input, prompt);
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
          } else {
            input.innerText = prompt;
            input.dispatchEvent(new InputEvent('input', {
              bubbles: true, inputType: 'insertText', data: prompt
            }));
          }

          const candidates = [...document.querySelectorAll('button, [role="button"]')]
            .filter(node => {
              const rect = node.getBoundingClientRect();
              return rect.width > 0 && rect.height > 0;
            });
          let send = candidates.find(node => {
            const label = ((node.getAttribute('aria-label') || '') + ' ' +
                           (node.getAttribute('title') || '') + ' ' +
                           (node.innerText || '')).toLowerCase();
            return label.includes('send') || label.includes('发送') || label.includes('送信');
          });

          if (!send) {
            const inputRect = input.getBoundingClientRect();
            send = candidates
              .filter(node => {
                const rect = node.getBoundingClientRect();
                return rect.left > inputRect.left + inputRect.width * 0.6 &&
                       rect.top > inputRect.top - 30 &&
                       rect.top < inputRect.bottom + 20;
              })
              .sort((a, b) => {
                const ra = a.getBoundingClientRect();
                const rb = b.getBoundingClientRect();
                return (rb.right - ra.right) || (rb.bottom - ra.bottom);
              })[0];
          }

          if (send) {
            send.click();
            return 'SENT_BUTTON';
          }

          input.dispatchEvent(new KeyboardEvent('keydown', {
            bubbles: true, cancelable: true, key: 'Enter', code: 'Enter',
            keyCode: 13, which: 13
          }));
          return 'SENT_ENTER';
        })()
        """;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DeepSeekWindow? _window;

    public bool IsWindowOpen => _window is { IsDisposed: false, Visible: true };

    public async Task<bool> EnsureLoggedInAsync(IWin32Window owner, CancellationToken cancellationToken)
    {
        EnsureWindow();
        if (_window is null)
        {
            return false;
        }

        if (await IsComposerPresentAsync())
        {
            return true;
        }

        _window.VerifyLoginAsync = IsComposerPresentAsync;
        bool result = await _window.ShowForLoginAsync(owner).WaitAsync(cancellationToken);
        return result;
    }

    public void ShowWindow(IWin32Window owner)
    {
        EnsureWindow();
        _window?.ShowWindow(owner);
    }

    public async Task<IReadOnlyList<string>> ExpandQueryAsync(string query, CancellationToken cancellationToken)
    {
        EnsureWindow();
        DeepSeekWindow window = _window ?? throw new InvalidOperationException("DeepSeek 窗口未创建。");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await window.NavigateHomeAsync(cancellationToken);
            await WaitForComposerAsync(cancellationToken, TimeSpan.FromSeconds(25));

            int before = await GetReplyCountAsync(cancellationToken);
            string prompt = BuildPrompt(query);
            string sendResult = await window.ExecuteScriptAsync(
                SendScriptTemplate.Replace("__PROMPT__", JsonSerializer.Serialize(prompt)));

            if (sendResult.Contains("NO_INPUT", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("没有找到 DeepSeek 聊天输入框，请重新登录。");
            }

            string reply = await WaitForReplyAsync(before, cancellationToken);
            IReadOnlyList<string> keywords = ParseKeywords(reply);
            if (keywords.Count == 0)
            {
                throw new InvalidDataException("DeepSeek 没有返回可用的关键词。");
            }

            return keywords;
        }
        catch (OperationCanceledException)
        {
            // Stop the generation so the next search does not continue the old answer.
            try
            {
                await window.NavigateHomeAsync(CancellationToken.None);
            }
            catch
            {
                // Ignore cleanup failures.
            }

            throw;
        }
        finally
        {
            try
            {
                _gate.Release();
            }
            catch (ObjectDisposedException)
            {
                // The app is shutting down.
            }
        }
    }

    private void EnsureWindow()
    {
        if (_window is { IsDisposed: false })
        {
            return;
        }

        _window = new DeepSeekWindow();
        _window.FormClosed += (_, _) => _window = null;
    }

    private async Task<bool> IsComposerPresentAsync()
    {
        if (_window is null || _window.IsDisposed)
        {
            return false;
        }

        try
        {
            string raw = await _window.ExecuteScriptAsync(ComposerCheckScript);
            return raw.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task WaitForComposerAsync(CancellationToken cancellationToken, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsComposerPresentAsync())
            {
                return;
            }

            await Task.Delay(450, cancellationToken);
        }

        throw new TimeoutException("DeepSeek 聊天输入框未出现，登录可能已失效。");
    }

    private async Task<int> GetReplyCountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_window is null)
        {
            return 0;
        }

        string raw = await _window.ExecuteScriptAsync(ReplyCountScript);
        return int.TryParse(raw, out int count) ? count : 0;
    }

    private async Task<string> WaitForReplyAsync(int beforeCount, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        string previous = string.Empty;
        long stableSince = 0;
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        while (clock.Elapsed < TimeSpan.FromSeconds(120))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_window is null)
            {
                throw new InvalidOperationException("DeepSeek 窗口已关闭。");
            }

            string raw = await _window.ExecuteScriptAsync(ReplyStateScript);
            ReplyState? state = null;
            try
            {
                state = JsonSerializer.Deserialize<ReplyState>(raw, jsonOptions);
            }
            catch (JsonException)
            {
                // The page may be in transition; retry on the next tick.
            }

            if (state is { Count: > 0 } && state.Count > beforeCount &&
                !string.IsNullOrWhiteSpace(state.Text))
            {
                if (state.Text == previous)
                {
                    if (stableSince == 0)
                    {
                        stableSince = clock.ElapsedMilliseconds;
                    }

                    if (!state.Streaming && clock.ElapsedMilliseconds - stableSince >= 1600)
                    {
                        return state.Text;
                    }
                }
                else
                {
                    previous = state.Text;
                    stableSince = 0;
                }
            }

            await Task.Delay(400, cancellationToken);
        }

        throw new TimeoutException("等待 DeepSeek 回复超时。");
    }

    private static string BuildPrompt(string query)
    {
        string safeQuery = query.Replace("\"", "'").Trim();
        if (safeQuery.Length > 200)
        {
            safeQuery = safeQuery[..200];
        }

        return
            "你是本地文件检索助手。用户想找与搜索词相关的文件。\n" +
            $"搜索词：\"{safeQuery}\"\n" +
            "请给出最多 12 个可能出现在相关文件名或文件夹名里的关键词，包括同义词、上下位词、常见文件类型或英文表达。\n" +
            "只输出 JSON 字符串数组，不要 Markdown 代码块，不要解释。\n" +
            "每项 1 到 24 个字符，优先具体词，避免“文件”“资料”等过于宽泛的词。\n" +
            "示例：[\"旅行\",\"行程\",\"机票\",\"酒店\",\"攻略\",\"travel\",\"itinerary\"]";
    }

    internal static IReadOnlyList<string> ParseKeywords(string reply)
    {
        string text = reply.Trim();
        int start = text.IndexOf('[');
        int end = text.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            return Array.Empty<string>();
        }

        string json = text[start..(end + 1)];
        List<string>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }

        if (parsed is null)
        {
            return Array.Empty<string>();
        }

        string[] ignored =
        {
            "文件", "资料", "文档", "文件夹", "相关", "其他", "所有",
            "file", "files", "document", "documents", "folder", "folders", "related"
        };

        return parsed
            .Select(value => value.Trim().Trim('"', '\'', '。', ',', '，'))
            .Where(value => value.Length is >= 1 and <= 24)
            .Where(value => !ignored.Contains(value, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    public void Dispose()
    {
        try
        {
            _window?.Shutdown();
        }
        catch
        {
            // Ignore.
        }

        _window = null;
        _gate.Dispose();
    }

    private sealed class ReplyState
    {
        public int Count { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool Streaming { get; set; }
    }
}
