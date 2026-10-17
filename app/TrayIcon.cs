using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace ClaudeSessionOverlay;

/// <summary>알림 영역 아이콘: 클릭하면 오버레이를 보이기/숨기기, 메뉴로 종료.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "ClaudeSessionOverlay";

    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _toggleItem;
    private readonly Forms.ToolStripMenuItem _collapseItem;
    private readonly Forms.ToolStripMenuItem _autostartItem;
    private readonly Forms.ToolStripMenuItem _soundItem;
    private readonly Forms.ToolStripMenuItem _voiceItem;

    public TrayIcon(MainWindow window)
    {
        _window = window;

        _toggleItem = new Forms.ToolStripMenuItem("오버레이 숨기기", null, (_, _) => _window.ToggleUserHidden());
        _collapseItem = new Forms.ToolStripMenuItem("목록 접기", null, (_, _) => _window.ToggleCollapsed());
        _autostartItem = new Forms.ToolStripMenuItem("Windows 시작 시 실행", null, (_, _) => ToggleAutostart())
        {
            Checked = IsAutostart(),
        };

        _soundItem = new Forms.ToolStripMenuItem("알림 소리", null, (_, _) => _window.ToggleSound());
        _voiceItem = new Forms.ToolStripMenuItem("음성으로 알려주기 (완료·확인 필요·컨텍스트)", null, (_, _) => _window.ToggleVoice());

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_collapseItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("확인한 세션 다시 보기", null, (_, _) => _window.ShowSeenAgain()));
        menu.Items.Add(new Forms.ToolStripMenuItem("위치 초기화", null, (_, _) => _window.ResetPosition()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_soundItem);
        menu.Items.Add(_voiceItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("알림 들어보기", null, (_, _) => _window.TestNotice()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("종료", null, (_, _) => _window.ExitApp()));
        menu.Opening += (_, _) =>
        {
            _toggleItem.Text = _window.IsUserHidden ? "오버레이 보이기" : "오버레이 숨기기";
            _collapseItem.Text = _window.IsCollapsed ? "목록 펼치기" : "목록 접기";
            _soundItem.Checked = _window.IsSoundOn;
            _voiceItem.Checked = _window.IsVoiceOn;
        };

        _icon = new Forms.NotifyIcon
        {
            Icon = BuildIcon(),
            Text = "Claude 세션 오버레이",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) _window.ToggleUserHidden();
        };

        _window.SummaryChanged += summary => _icon.Text = Truncate($"Claude 세션 — {summary}", 63);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static bool IsAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunName) is string;
    }

    private void ToggleAutostart()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (IsAutostart()) key.DeleteValue(RunName, false);
        else key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
        _autostartItem.Checked = IsAutostart();
    }

    /// <summary>주황 원 안에 흰 별표 — Claude 느낌의 단순한 아이콘을 그려 쓴다</summary>
    private static Icon BuildIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(Color.FromArgb(0xD9, 0x77, 0x57));
            g.FillEllipse(fill, 1, 1, 30, 30);
            using var pen = new Pen(Color.White, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (int i = 0; i < 4; i++)
            {
                double a = Math.PI / 4 * i;
                float dx = (float)(Math.Cos(a) * 9), dy = (float)(Math.Sin(a) * 9);
                g.DrawLine(pen, 16 - dx, 16 - dy, 16 + dx, 16 + dy);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
