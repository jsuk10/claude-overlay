import AppKit
import WidgetKit

// 오버레이(C#, Avalonia) 프로세스 안에 올라가는 작은 Swift 라이브러리(libClaudeOverlayMac.dylib).
//   메뉴 막대: 상태 항목(아이콘 + 상태별 개수)과 그 메뉴(세션 목록, 사용량, 오버레이 명령)
//   위젯: WidgetCenter로 타임라인을 다시 불러 달라고 한다(Swift에서만 부를 수 있는 API)
// C#은 cso_* 함수만 부른다. 모두 주 스레드(Avalonia UI 스레드)에서 불린다.

typealias ActionCallback = @convention(c) (UnsafePointer<CChar>) -> Void

/// 메뉴 맨 아래 오버레이 명령(보이기/숨기기, 알림, 로그인 시 열기, 종료 …). 글자·체크는 C#이 정한다
struct MenuCommand: Codable {
    var id: String?
    var title: String?
    var checked: Bool?
    var separator: Bool?
}

struct MenuPayload: Codable {
    var board: Board
    var commands: [MenuCommand] = []
}

@MainActor
final class StatusBar: NSObject, NSMenuDelegate {
    static var shared: StatusBar?

    private let item: NSStatusItem
    private let menu = NSMenu()
    private let callback: ActionCallback
    private var payload = MenuPayload(board: Board())
    private var lastTitleKey = ""

    init(callback: @escaping ActionCallback) {
        self.callback = callback
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        super.init()
        menu.delegate = self
        menu.autoenablesItems = false
        item.menu = menu
        if let button = item.button {
            button.image = StatusBar.icon
            button.imagePosition = .imageLeft
        }
        render()
    }

    func remove() { NSStatusBar.system.removeStatusItem(item) }

    func update(_ json: String) {
        guard let data = json.data(using: .utf8),
              let next = try? JSONDecoder().decode(MenuPayload.self, from: data) else { return }
        payload = next
        render()
    }

    // ── 메뉴 막대 글자: ✻ ●1 ●2 ●1 ───────────────────────────────────

    private func render() {
        let board = payload.board
        let key = "\(board.waiting)|\(board.working)|\(board.done)|\(board.fiveHour.map { Int($0.percent) } ?? -1)|\(board.summary)"
        guard key != lastTitleKey, let button = item.button else { return }
        lastTitleKey = key

        let font = NSFont.monospacedDigitSystemFont(ofSize: NSFont.systemFontSize(for: .regular) - 0.5, weight: .medium)
        let title = NSMutableAttributedString()
        func add(_ count: Int, _ colorKey: String) {
            guard count > 0 else { return }
            title.append(NSAttributedString(string: " ●", attributes: [
                .font: NSFont.systemFont(ofSize: 9), .foregroundColor: color(board.colors[colorKey]) ?? .labelColor,
                .baselineOffset: 1.5,
            ]))
            title.append(NSAttributedString(string: "\(count)", attributes: [.font: font, .foregroundColor: NSColor.labelColor]))
        }
        // 사람이 볼 차례인 것부터: 입력 대기 → 작업 중 → 완료
        add(board.waiting, "waiting")
        add(board.working, "working")
        add(board.done, "done")
        // 5시간 한도가 경고 수준이면 메뉴 막대에도 보인다
        if let five = board.fiveHour, five.percent >= 75 {
            title.append(NSAttributedString(string: " \(Int(five.percent))%", attributes: [
                .font: font, .foregroundColor: color(five.color) ?? .labelColor,
            ]))
        }
        button.attributedTitle = title
        button.toolTip = "\(board.label("title", "Claude")) — \(board.summary)"
    }

    // ── 메뉴: 열 때마다 최신 내용으로 채운다 ──────────────────────────

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let board = payload.board

        let header = NSMenuItem(title: "", action: nil, keyEquivalent: "")
        header.attributedTitle = NSAttributedString(string: "\(board.label("title", "Claude"))  ·  \(board.summary)", attributes: [
            .font: NSFont.boldSystemFont(ofSize: NSFont.systemFontSize),
        ])
        header.isEnabled = false
        menu.addItem(header)
        menu.addItem(.separator())

        if board.sessions.isEmpty {
            let empty = NSMenuItem(title: board.label("empty", "Nothing to check"), action: nil, keyEquivalent: "")
            empty.isEnabled = false
            menu.addItem(empty)
        }
        for session in board.sessions.prefix(15) { menu.addItem(sessionItem(session)) }

        if let usage = usageLine(board) {
            menu.addItem(.separator())
            let item = NSMenuItem(title: "", action: nil, keyEquivalent: "")
            item.attributedTitle = usage
            item.isEnabled = false
            menu.addItem(item)
        }

        if !payload.commands.isEmpty { menu.addItem(.separator()) }
        for command in payload.commands {
            if command.separator == true { menu.addItem(.separator()); continue }
            let item = NSMenuItem(title: command.title ?? "", action: #selector(clicked(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = command.id
            if let checked = command.checked { item.state = checked ? .on : .off }
            menu.addItem(item)
        }
    }

    private func sessionItem(_ s: BoardSession) -> NSMenuItem {
        let item = NSMenuItem(title: s.title, action: #selector(clicked(_:)), keyEquivalent: "")
        item.target = self
        item.representedObject = "open:" + s.id
        item.image = StatusBar.dot(color(s.color) ?? .secondaryLabelColor)

        let text = NSMutableAttributedString(string: (s.isFavorite ? "★ " : "") + truncate(s.title, 46), attributes: [
            .font: NSFont.menuFont(ofSize: 0),
        ])
        let small = NSFont.systemFont(ofSize: NSFont.smallSystemFontSize)
        text.append(NSAttributedString(string: "\n" + s.statusText, attributes: [
            .font: small, .foregroundColor: color(s.color) ?? NSColor.secondaryLabelColor,
        ]))
        var rest = [s.model, s.folder].filter { !$0.isEmpty }.joined(separator: " · ")
        if s.tasks > 0 { rest += "  ▸ \(s.tasks)" }
        if !rest.isEmpty {
            text.append(NSAttributedString(string: " · " + truncate(rest, 40), attributes: [
                .font: small, .foregroundColor: NSColor.secondaryLabelColor,
            ]))
        }
        if let context = s.context {
            text.append(NSAttributedString(string: "  \(Int(context))%", attributes: [
                .font: NSFont.monospacedDigitSystemFont(ofSize: NSFont.smallSystemFontSize, weight: .semibold),
                .foregroundColor: color(s.contextColor) ?? NSColor.secondaryLabelColor,
            ]))
        }
        item.attributedTitle = text
        return item
    }

    private func usageLine(_ board: Board) -> NSAttributedString? {
        guard board.weekly != nil || board.fiveHour != nil else { return nil }
        let line = NSMutableAttributedString()
        let font = NSFont.menuFont(ofSize: 0)
        func part(_ label: String, _ usage: BoardUsage) {
            if line.length > 0 { line.append(NSAttributedString(string: "   ", attributes: [.font: font])) }
            line.append(NSAttributedString(string: label + " ", attributes: [.font: font, .foregroundColor: NSColor.secondaryLabelColor]))
            line.append(NSAttributedString(string: usage.text, attributes: [
                .font: NSFont.monospacedDigitSystemFont(ofSize: NSFont.systemFontSize, weight: .medium),
                .foregroundColor: color(usage.color) ?? NSColor.labelColor,
            ]))
        }
        if let week = board.weekly { part(board.label("week", "Week"), week) }
        if let five = board.fiveHour { part(board.label("session", "Session"), five) }
        return line
    }

    @objc private func clicked(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String else { return }
        id.withCString { callback($0) }
    }

    // ── 그림 ─────────────────────────────────────────────────────────────

    /// 오버레이 트레이 아이콘과 같은 별표. 템플릿이라 메뉴 막대 밝기에 맞춰 칠해진다
    static let icon: NSImage = {
        let image = NSImage(size: NSSize(width: 16, height: 16), flipped: false) { _ in
            let path = NSBezierPath()
            path.lineWidth = 1.9
            path.lineCapStyle = .round
            for i in 0..<4 {
                let a = Double(i) * .pi / 4
                let dx = cos(a) * 6.3, dy = sin(a) * 6.3
                path.move(to: NSPoint(x: 8 - dx, y: 8 - dy))
                path.line(to: NSPoint(x: 8 + dx, y: 8 + dy))
            }
            NSColor.black.setStroke()
            path.stroke()
            return true
        }
        image.isTemplate = true
        return image
    }()

    static func dot(_ color: NSColor) -> NSImage {
        NSImage(size: NSSize(width: 10, height: 10), flipped: false) { rect in
            color.setFill()
            NSBezierPath(ovalIn: rect.insetBy(dx: 1, dy: 1)).fill()
            return true
        }
    }
}

private func color(_ hex: String?) -> NSColor? {
    guard let hex, let (r, g, b) = rgb(hex) else { return nil }
    return NSColor(srgbRed: r, green: g, blue: b, alpha: 1)
}

private func truncate(_ text: String, _ max: Int) -> String {
    text.count <= max ? text : String(text.prefix(max - 1)) + "…"
}

private func onMain(_ work: @escaping @MainActor () -> Void) {
    if Thread.isMainThread { MainActor.assumeIsolated(work) } else { DispatchQueue.main.async { work() } }
}

// ── C에서 부르는 함수 ───────────────────────────────────────────────────

@_cdecl("cso_status_start")
public func cso_status_start(_ callback: @escaping @convention(c) (UnsafePointer<CChar>) -> Void) {
    onMain {
        if StatusBar.shared == nil { StatusBar.shared = StatusBar(callback: callback) }
    }
}

@_cdecl("cso_status_update")
public func cso_status_update(_ json: UnsafePointer<CChar>) {
    let text = String(cString: json)
    onMain { StatusBar.shared?.update(text) }
}

@_cdecl("cso_status_stop")
public func cso_status_stop() {
    onMain {
        StatusBar.shared?.remove()
        StatusBar.shared = nil
    }
}

/// 위젯 타임라인과 제어 센터 컨트롤(macOS 26+)을 다시 불러 달라고 한다(시스템이 횟수를 조절한다)
@_cdecl("cso_widgets_reload")
public func cso_widgets_reload() {
    WidgetCenter.shared.reloadAllTimelines()
    #if compiler(>=6.2)
    if #available(macOS 26.0, *) { ControlCenter.shared.reloadAllControls() }
    #endif
}
