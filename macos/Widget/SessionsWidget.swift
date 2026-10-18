import SwiftUI
import WidgetKit

// 알림 센터·바탕화면 위젯(macOS 14+). 오버레이가 쓰는 ~/.claude/session-hud/status.json을 읽어 그린다.
//   작게: 확인할 개수와 5시간 한도   중간: 세션 3개 + 사용량   크게: 세션 7개 + 사용량
//   아주 크게(바탕화면용): 왼쪽에 개수·사용량, 오른쪽에 세션 7개와 폴더
// 제어 센터 컨트롤(macOS 26+)은 SessionsControl.swift.
// 세션을 누르면 데스크톱 앱 세션은 claude://로 바로, 터미널 세션은 오버레이(claude-overlay://)가 연다.

struct BoardEntry: TimelineEntry {
    let date: Date
    let board: Board?
}

struct BoardProvider: TimelineProvider {
    func placeholder(in context: Context) -> BoardEntry { BoardEntry(date: .now, board: .sample) }

    func getSnapshot(in context: Context, completion: @escaping (BoardEntry) -> Void) {
        // 위젯 갤러리 미리보기: 실제 세션이 없으면 예시로 보여 준다
        let board = Board.load()
        completion(BoardEntry(date: .now, board: context.isPreview && (board?.sessions.isEmpty ?? true) ? .sample : board))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<BoardEntry>) -> Void) {
        let board = Board.load()
        let now = Date.now
        // 오버레이가 바뀔 때마다 다시 불러 준다. 그게 끊기면(오버레이 꺼짐) 6분 뒤 항목에서 '꺼져 있어요'로 바뀐다
        let entries = [BoardEntry(date: now, board: board), BoardEntry(date: now.addingTimeInterval(6 * 60), board: board)]
        completion(Timeline(entries: entries, policy: .after(now.addingTimeInterval(15 * 60))))
    }
}

@main
struct ClaudeSessionWidgets: WidgetBundle {
    var body: some Widget {
        SessionsWidget()
        // 제어 센터 컨트롤은 macOS 26 SDK(Xcode 26, Swift 6.2)로 빌드할 때만 들어가고, macOS 26 이상에서만 보인다
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) { SessionsControl() }
        #endif
    }
}

struct SessionsWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "ClaudeSessions", provider: BoardProvider()) { entry in
            SessionsView(entry: entry)
                .containerBackground(.fill.tertiary, for: .widget)
        }
        .configurationDisplayName("Claude Sessions")
        .description("Claude Code 세션 상태와 사용량 · Claude Code sessions and usage")
        .supportedFamilies([.systemSmall, .systemMedium, .systemLarge, .systemExtraLarge])
    }
}

// ── 화면 ────────────────────────────────────────────────────────────────

struct SessionsView: View {
    let entry: BoardEntry
    @Environment(\.widgetFamily) private var family

    var body: some View {
        if let board = entry.board {
            switch family {
            case .systemSmall: SmallView(board: board, isStale: board.isStale(at: entry.date))
            case .systemExtraLarge: ExtraLargeView(board: board, isStale: board.isStale(at: entry.date))
            default: ListView(board: board, isStale: board.isStale(at: entry.date), isLarge: family == .systemLarge)
            }
        } else {
            VStack(alignment: .leading, spacing: 6) {
                Header(title: "Claude", summary: "")
                Spacer()
                Text("오버레이를 실행하면 세션이 보입니다.\nRun the overlay to see sessions.")
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
            }
            .widgetURL(URL(string: "claude-overlay://show"))
        }
    }
}

struct Header: View {
    let title: String
    let summary: String

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "asterisk")
                .font(.system(size: 11, weight: .bold))
                .foregroundStyle(Color(hex: "#D97757"))
            Text(title).font(.system(size: 13, weight: .semibold)).lineLimit(1).minimumScaleFactor(0.8).layoutPriority(1)
            Spacer(minLength: summary.isEmpty ? 0 : 4)
            if !summary.isEmpty {
                Text(summary).font(.system(size: 11)).foregroundStyle(.secondary).lineLimit(1)
            }
        }
    }
}

struct SmallView: View {
    let board: Board
    let isStale: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Header(title: board.label("title", "Claude"), summary: "")
            if isStale {
                Spacer()
                Text(board.label("stale", "The overlay isn't running")).font(.caption).foregroundStyle(.secondary)
                Spacer()
            } else {
                Spacer(minLength: 0)
                Count(value: board.waiting, label: board.label("waiting", "Needs input"), color: board.colors["waiting"])
                Count(value: board.working, label: board.label("working", "Working"), color: board.colors["working"])
                Count(value: board.done, label: board.label("done", "Done"), color: board.colors["done"])
                Spacer(minLength: 0)
            }
            if let five = board.fiveHour {
                UsageBar(label: board.label("session", "Session"), usage: five)
            }
        }
        .widgetURL(URL(string: "claude-overlay://show"))
    }
}

struct Count: View {
    let value: Int
    let label: String
    let color: String?

    var body: some View {
        HStack(spacing: 6) {
            Circle().fill(Color(hex: color)).frame(width: 7, height: 7)
            Text(label).font(.system(size: 12)).foregroundStyle(value > 0 ? .primary : .secondary)
            Spacer()
            Text("\(value)").font(.system(size: 15, weight: .semibold).monospacedDigit())
                .foregroundStyle(value > 0 ? .primary : .tertiary)
        }
    }
}

/// 중간: 세션 3개 + 한 줄 사용량   크게: 세션 7개 + 사용량 바
struct ListView: View {
    let board: Board
    let isStale: Bool
    let isLarge: Bool

    private var limit: Int { isLarge ? 7 : 3 }
    private var more: Int { max(0, board.sessions.count - limit) }

    var body: some View {
        VStack(alignment: .leading, spacing: isLarge ? 8 : 5) {
            Header(title: board.label("title", "Claude"), summary: isStale ? "" : board.summary)
            if isStale {
                Spacer()
                Text(board.label("stale", "The overlay isn't running")).font(.callout).foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity)
                Spacer()
            } else if board.sessions.isEmpty {
                Spacer()
                Text(board.label("empty", "Nothing to check")).font(.callout).foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity)
                Spacer()
            } else {
                ForEach(board.sessions.prefix(limit)) { session in
                    Link(destination: URL(string: session.openUrl) ?? URL(string: "claude-overlay://show")!) {
                        SessionLine(session: session)
                    }
                }
                if isLarge && more > 0 { MoreText(board: board, count: more) }
                Spacer(minLength: 0)
            }
            if isLarge {
                if board.weekly != nil || board.fiveHour != nil {
                    HStack(spacing: 12) {
                        if let week = board.weekly { UsageBar(label: board.label("week", "Week"), usage: week) }
                        if let five = board.fiveHour { UsageBar(label: board.label("session", "Session"), usage: five) }
                    }
                }
            } else {
                // 중간 크기는 높이가 모자라 사용량을 한 줄 글자로
                HStack(spacing: 10) {
                    if let week = board.weekly { UsageText(label: board.label("week", "Week"), usage: week) }
                    if let five = board.fiveHour { UsageText(label: board.label("session", "Session"), usage: five) }
                    Spacer(minLength: 0)
                    if more > 0 { MoreText(board: board, count: more) }
                }
            }
        }
    }
}

struct MoreText: View {
    let board: Board
    let count: Int

    var body: some View {
        Text(String(format: board.label("more", "+%d more"), count)).font(.system(size: 10)).foregroundStyle(.secondary)
    }
}

struct UsageText: View {
    let label: String
    let usage: BoardUsage

    var body: some View {
        HStack(spacing: 3) {
            Text(label).foregroundStyle(.secondary)
            Text("\(Int(usage.percent))%").fontWeight(.semibold).foregroundStyle(Color(hex: usage.color))
        }
        .font(.system(size: 10).monospacedDigit())
    }
}

/// 아주 크게(바탕화면): 왼쪽은 작은 위젯처럼 개수와 사용량, 오른쪽은 세션 목록(폴더까지)
struct ExtraLargeView: View {
    let board: Board
    let isStale: Bool

    var body: some View {
        HStack(alignment: .top, spacing: 18) {
            VStack(alignment: .leading, spacing: 9) {
                Header(title: board.label("title", "Claude"), summary: "")
                if !isStale && !board.isQuiet {
                    Text(board.summary).font(.system(size: 11)).foregroundStyle(.secondary).lineLimit(1)
                }
                Spacer(minLength: 0)
                Count(value: board.waiting, label: board.label("waiting", "Needs input"), color: board.colors["waiting"])
                Count(value: board.working, label: board.label("working", "Working"), color: board.colors["working"])
                Count(value: board.done, label: board.label("done", "Done"), color: board.colors["done"])
                Spacer(minLength: 0)
                if let week = board.weekly { UsageBar(label: board.label("week", "Week"), usage: week) }
                if let five = board.fiveHour { UsageBar(label: board.label("session", "Session"), usage: five) }
            }
            .frame(width: 190)
            .widgetURL(URL(string: "claude-overlay://show"))

            Divider()

            VStack(alignment: .leading, spacing: 9) {
                if isStale {
                    Spacer()
                    Text(board.label("stale", "The overlay isn't running")).font(.callout).foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity)
                    Spacer()
                } else if board.sessions.isEmpty {
                    Spacer()
                    Text(board.label("empty", "Nothing to check")).font(.callout).foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity)
                    Spacer()
                } else {
                    ForEach(board.sessions.prefix(7)) { session in
                        Link(destination: URL(string: session.openUrl) ?? URL(string: "claude-overlay://show")!) {
                            SessionLine(session: session, showsFolder: true)
                        }
                    }
                    if board.sessions.count > 7 { MoreText(board: board, count: board.sessions.count - 7) }
                    Spacer(minLength: 0)
                }
            }
        }
    }
}

struct SessionLine: View {
    let session: BoardSession
    var showsFolder = false

    var body: some View {
        HStack(alignment: .center, spacing: 7) {
            Circle().fill(Color(hex: session.color)).frame(width: 7, height: 7)
            VStack(alignment: .leading, spacing: 1) {
                Text((session.isFavorite ? "★ " : "") + session.title)
                    .font(.system(size: 12, weight: .medium)).lineLimit(1)
                HStack(spacing: 3) {
                    StatusLabel(session: session)
                    if !session.model.isEmpty {
                        Text("· " + session.model).foregroundStyle(.secondary)
                    }
                    if showsFolder && !session.folder.isEmpty {
                        Text("· " + session.folder).foregroundStyle(.tertiary).truncationMode(.head)
                    }
                }
                .font(.system(size: 10))
                .lineLimit(1)
            }
            Spacer(minLength: 4)
            if let context = session.context {
                Text("\(Int(context))%")
                    .font(.system(size: 11, weight: .semibold).monospacedDigit())
                    .foregroundStyle(Color(hex: session.contextColor))
            }
        }
    }
}

/// "작업 중 12:03"처럼 위젯이 스스로 시계를 돌린다(다시 불러오지 않아도 흐른다)
struct StatusLabel: View {
    let session: BoardSession

    var body: some View {
        let color = Color(hex: session.color)
        if session.state == "working", let since = session.sinceDate {
            (Text(session.stateLabel + " ") + Text(since, style: .timer)).foregroundStyle(color)
        } else if session.state == "done", let since = session.sinceDate {
            (Text(session.stateLabel + " · ") + Text(since, style: .relative)).foregroundStyle(color)
        } else {
            Text(session.stateLabel).foregroundStyle(color)
        }
    }
}

struct UsageBar: View {
    let label: String
    let usage: BoardUsage

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: 4) {
                Text(label).foregroundStyle(.secondary)
                Spacer(minLength: 2)
                Text("\(Int(usage.percent))%").fontWeight(.semibold).foregroundStyle(Color(hex: usage.color))
            }
            .font(.system(size: 10).monospacedDigit())
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule().fill(.quaternary)
                    Capsule().fill(Color(hex: usage.color))
                        .frame(width: max(3, geo.size.width * min(1, usage.percent / 100)))
                }
            }
            .frame(height: 4)
        }
    }
}

extension Color {
    init(hex: String?) {
        if let hex, let (r, g, b) = rgb(hex) { self = Color(.sRGB, red: r, green: g, blue: b) } else { self = .secondary }
    }
}

extension Board {
    /// 위젯 갤러리 미리보기용
    static var sample: Board {
        var b = Board()
        b.updatedAt = Date.now.timeIntervalSince1970 * 1000
        b.summary = "대기 1 · 작업 2"
        b.waiting = 1; b.working = 2; b.done = 1
        b.colors = ["waiting": "#E8B931", "working": "#D97757", "done": "#5BB974"]
        b.labels = ["title": "Claude 세션", "waiting": "입력 대기", "working": "작업 중", "done": "완료", "week": "주간", "session": "세션 한도"]
        b.weekly = BoardUsage(percent: 42, color: "#2A78D6", text: "42%")
        b.fiveHour = BoardUsage(percent: 65, color: "#2A78D6", text: "65%")
        let start = (Date.now.timeIntervalSince1970 - 745) * 1000
        b.sessions = [
            BoardSession(id: "1", title: "Migrate database schema", state: "waiting", stateLabel: "입력 대기", model: "opus-5-5", color: "#E8B931", context: 92, contextColor: "#D03B3B"),
            BoardSession(id: "2", title: "Build checkout flow", state: "working", stateLabel: "작업 중", since: start, model: "opus-5-5", color: "#D97757", context: 46, contextColor: "#2A78D6", isFavorite: true),
            BoardSession(id: "3", title: "Fix login redirect", state: "done", stateLabel: "완료", since: start, model: "sonnet-5-5", color: "#5BB974", context: 23, contextColor: "#2A78D6"),
        ]
        return b
    }
}
