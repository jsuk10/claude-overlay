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

        _toggleItem = new Forms.ToolStripMenuItem("", null, (_, _) => _window.ToggleUserHidden());
        _collapseItem = new Forms.ToolStripMenuItem("", null, (_, _) => _window.ToggleCollapsed());
        _autostartItem = new Forms.ToolStripMenuItem("", null, (_, _) => ToggleAutostart())
        {
            Checked = IsAutostart(),
        };

        _soundItem = new Forms.ToolStripMenuItem("", null, (_, _) => _window.ToggleSound());
        _voiceItem = new Forms.ToolStripMenuItem("", null, (_, _) => _window.ToggleVoice());

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_collapseItem);
        var showSeenItem = new Forms.ToolStripMenuItem("", null, (_, _) => _window.ShowSeenAgain());
        var resetItem = new Forms.ToolStripMenuItem("", null, (_, _) => _window.ResetPosition());
        var testItem = new Forms.ToolStripMenuItem("", null, (_, _) => _window.TestNotice());
        var quitItem = new Forms.ToolStripMenuItem("", null, (_, _) => _window.ExitApp());
        menu.Items.Add(showSeenItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_soundItem);
        menu.Items.Add(_voiceItem);
        menu.Items.Add(testItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(quitItem);
        menu.Opening += (_, _) =>
        {
            // 언어가 바뀌었을 수 있으니 열 때마다 문구를 채운다
            _toggleItem.Text = _window.IsUserHidden ? Loc.T("오버레이 보이기", "Show overlay") : Loc.T("오버레이 숨기기", "Hide overlay");
            _collapseItem.Text = _window.IsCollapsed ? Loc.T("목록 펼치기", "Expand list") : Loc.T("목록 접기", "Collapse list");
            showSeenItem.Text = Loc.T("확인한 세션 다시 보기", "Show seen sessions again");
            resetItem.Text = Loc.T("위치 초기화", "Reset position");
            _soundItem.Text = Loc.T("알림 소리", "Notification sound");
            _voiceItem.Text = Loc.T("음성으로 알려주기 (완료·확인 필요·컨텍스트)", "Voice alerts (done · needs input · context)");
            testItem.Text = Loc.T("알림 들어보기", "Test notification");
            _autostartItem.Text = Loc.T("Windows 시작 시 실행", "Start with Windows");
            quitItem.Text = Loc.T("종료", "Quit");
            _soundItem.Checked = _window.IsSoundOn;
            _voiceItem.Checked = _window.IsVoiceOn;
        };

        _icon = new Forms.NotifyIcon
        {
            Icon = BuildIcon(),
            Text = Loc.T("Claude 세션 오버레이", "Claude Session Overlay"),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) _window.ToggleUserHidden();
        };

        _window.SummaryChanged += summary => _icon.Text = Truncate(Loc.T($"Claude 세션 — {summary}", $"Claude Sessions — {summary}"), 63);
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
