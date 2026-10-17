import Foundation

// 오버레이(C#)가 만드는 요약판: core/StatusBoard.cs의 BoardDoc과 같은 모양.
// 메뉴 막대(같은 프로세스)는 JSON 문자열로 받고, 위젯(샌드박스)은 ~/.claude/session-hud/status.json을 읽는다.

struct Board: Codable {
    var schema: Int = 1
    var updatedAt: Double = 0
    var language: String = "ko"
    var summary: String = ""
    var isQuiet: Bool = true
    var waiting: Int = 0
    var working: Int = 0
    var done: Int = 0
    var weekly: BoardUsage?
    var fiveHour: BoardUsage?
    var sessions: [BoardSession] = []
    var labels: [String: String] = [:]
    var colors: [String: String] = [:]

    func label(_ key: String, _ fallback: String) -> String { labels[key] ?? fallback }

    var updatedDate: Date { Date(timeIntervalSince1970: updatedAt / 1000) }

    /// 오버레이가 1분마다 다시 쓰므로, 이보다 오래되면 꺼진 것으로 본다
    func isStale(at now: Date) -> Bool { now.timeIntervalSince(updatedDate) > 5 * 60 }

    static func decode(_ data: Data) -> Board? { try? JSONDecoder().decode(Board.self, from: data) }

    /// 실제 홈 폴더(샌드박스 안에서는 NSHomeDirectory가 컨테이너를 가리킨다)
    static var realHome: String {
        if let pw = getpwuid(getuid()), let dir = pw.pointee.pw_dir { return String(cString: dir) }
        return NSHomeDirectory()
    }

    static var fileURL: URL {
        URL(fileURLWithPath: realHome).appendingPathComponent(".claude/session-hud/status.json")
    }

    static func load() -> Board? {
        guard let data = try? Data(contentsOf: fileURL) else { return nil }
        return decode(data)
    }
}

struct BoardUsage: Codable {
    var percent: Double = 0
    var color: String = ""
    var text: String = ""
    var resetsAt: String?

    var resetDate: Date? {
        guard let resetsAt else { return nil }
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f.date(from: resetsAt) { return d }
        f.formatOptions = [.withInternetDateTime]
        return f.date(from: resetsAt)
    }
}

struct BoardSession: Codable, Identifiable {
    var id: String = ""
    var title: String = ""
    var state: String = ""
    var stateLabel: String = ""
    var statusText: String = ""
    var since: Double?
    var detail: String = ""
    var folder: String = ""
    var model: String = ""
    var color: String = ""
    var context: Double?
    var contextColor: String?
    var isFavorite: Bool = false
    var tasks: Int = 0
    var openUrl: String = ""

    var sinceDate: Date? { since.map { Date(timeIntervalSince1970: $0 / 1000) } }
}

/// "#RRGGBB" → (r, g, b) 0…1
func rgb(_ hex: String) -> (Double, Double, Double)? {
    var s = hex
    if s.hasPrefix("#") { s.removeFirst() }
    guard s.count == 6, let v = UInt32(s, radix: 16) else { return nil }
    return (Double((v >> 16) & 0xFF) / 255, Double((v >> 8) & 0xFF) / 255, Double(v & 0xFF) / 255)
}
