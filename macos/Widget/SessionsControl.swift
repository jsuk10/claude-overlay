// 제어 센터·메뉴 막대 컨트롤(macOS 26 이상).
// 입력을 기다리는 세션 수를 보여 주고, 누르면 그 세션(없으면 오버레이)을 연다.
// 컨트롤 API는 macOS 26 SDK(Xcode 26, Swift 6.2)에만 있어서 그보다 낮은 컴파일러에서는 이 파일이 통째로 빠진다.
#if compiler(>=6.2)
import AppIntents
import SwiftUI
import WidgetKit

@available(macOS 26.0, *)
struct ControlStatus: Sendable {
    var title: String
    var symbol: String
    var url: URL

    static let overlay = URL(string: "claude-overlay://show")!

    init(title: String, symbol: String, url: URL) {
        self.title = title
        self.symbol = symbol
        self.url = url
    }

    /// 사람이 볼 차례인 것부터: 입력 대기 → 작업 중 → 완료 → 조용함
    init(board: Board?) {
        guard let board, !board.isStale(at: .now) else {
            self.init(title: "Claude", symbol: "asterisk", url: Self.overlay)
            return
        }
        let firstWaiting = board.sessions.first { $0.state == "waiting" }
        if board.waiting > 0 {
            self.init(title: "\(board.label("waiting", "Needs input")) \(board.waiting)",
                      symbol: "exclamationmark.bubble.fill",
                      url: firstWaiting.flatMap { URL(string: $0.openUrl) } ?? Self.overlay)
        } else if board.working > 0 {
            self.init(title: "\(board.label("working", "Working")) \(board.working)", symbol: "ellipsis.bubble", url: Self.overlay)
        } else if board.done > 0 {
            self.init(title: "\(board.label("done", "Done")) \(board.done)", symbol: "checkmark.bubble", url: Self.overlay)
        } else {
            self.init(title: board.label("title", "Claude"), symbol: "asterisk", url: Self.overlay)
        }
    }
}

@available(macOS 26.0, *)
struct SessionsControlProvider: ControlValueProvider {
    var previewValue: ControlStatus {
        ControlStatus(title: "입력 대기 1", symbol: "exclamationmark.bubble.fill", url: ControlStatus.overlay)
    }

    func currentValue() async throws -> ControlStatus { ControlStatus(board: Board.load()) }
}

@available(macOS 26.0, *)
struct SessionsControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "ClaudeSessionsControl", provider: SessionsControlProvider()) { status in
            ControlWidgetButton(action: OpenURLIntent(status.url)) {
                Label(status.title, systemImage: status.symbol)
            }
        }
        .displayName("Claude 세션")
        .description("입력을 기다리는 세션 수 · 누르면 그 세션이나 오버레이를 엽니다")
    }
}
#endif
