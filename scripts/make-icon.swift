import AppKit

// 앱 아이콘을 그린다: 크림색 바탕, 왼쪽 아래에 주황 원 + 흰 별표(Claude 느낌의 마크),
// 거기서 오른쪽 위로 상태 말풍선이 나온다(입력 대기·작업 중·완료 세 점).
//   swift scripts/make-icon.swift            assets/icon/icon-{16…1024}.png, app.ico를 다시 만든다
//   swift scripts/make-icon.swift <폴더>     다른 폴더에
// 32px 이하는 줄여도 알아보게 여백을 줄이고 점·마크를 키운 단순한 그림을 쓴다.

func rgb(_ h: UInt32, _ a: CGFloat = 1) -> NSColor {
    NSColor(srgbRed: CGFloat((h >> 16) & 0xFF) / 255, green: CGFloat((h >> 8) & 0xFF) / 255, blue: CGFloat(h & 0xFF) / 255, alpha: a)
}

let cream = (top: rgb(0xFFF7EA), bottom: rgb(0xF4E2C4))
let ink = rgb(0x2B2D3A)
let accent = rgb(0xD97757)
let dots = [rgb(0xE8B931), rgb(0xD97757), rgb(0x5BB974)]

func oval(_ cx: CGFloat, _ cy: CGFloat, _ r: CGFloat, _ c: NSColor) {
    c.setFill()
    NSBezierPath(ovalIn: NSRect(x: cx - r, y: cy - r, width: r * 2, height: r * 2)).fill()
}

/// 1024×1024 좌표(원점 왼쪽 아래)로 그린다
func draw(simple: Bool) {
    // 바탕
    let inset: CGFloat = simple ? 40 : 100
    let body = NSRect(x: inset, y: inset, width: 1024 - inset * 2, height: 1024 - inset * 2)
    let shape = NSBezierPath(roundedRect: body, xRadius: body.width * 0.225, yRadius: body.width * 0.225)
    if !simple {
        NSGraphicsContext.saveGraphicsState()
        let shadow = NSShadow()
        shadow.shadowColor = NSColor.black.withAlphaComponent(0.25)
        shadow.shadowOffset = NSSize(width: 0, height: -10)
        shadow.shadowBlurRadius = 22
        shadow.set()
        cream.bottom.setFill()
        shape.fill()
        NSGraphicsContext.restoreGraphicsState()
    }
    NSGradient(starting: cream.top, ending: cream.bottom)?.draw(in: shape, angle: -90)

    // 말풍선(오른쪽 위)과 꼬리(왼쪽 아래 마크 쪽)
    let badge = simple ? (c: NSPoint(x: 280, y: 280), r: CGFloat(205)) : (c: NSPoint(x: 305, y: 305), r: CGFloat(165))
    let bubble = simple ? NSRect(x: 250, y: 430, width: 700, height: 470) : NSRect(x: 330, y: 420, width: 570, height: 400)
    let b = NSBezierPath(roundedRect: bubble, xRadius: bubble.height * 0.42, yRadius: bubble.height * 0.42)
    let tipGap = badge.r + (simple ? 18 : 22)
    let tip = NSPoint(x: badge.c.x + cos(.pi / 4) * tipGap, y: badge.c.y + sin(.pi / 4) * tipGap)
    let tail = NSBezierPath()
    tail.move(to: NSPoint(x: bubble.minX + bubble.width * 0.12, y: bubble.minY + bubble.height * 0.30))
    tail.line(to: tip)
    tail.line(to: NSPoint(x: bubble.minX + bubble.width * 0.36, y: bubble.minY + bubble.height * 0.06))
    tail.close()
    ink.setFill()
    b.fill()
    tail.fill()

    // 세 점: 입력 대기·작업 중·완료
    let dotR: CGFloat = simple ? 68 : 52
    let step: CGFloat = simple ? 190 : 140
    for (i, color) in dots.enumerated() {
        oval(bubble.midX + CGFloat(i - 1) * step, bubble.midY, dotR, color)
    }

    // 왼쪽 아래 마크: 바탕색 테두리 → 주황 원 → 흰 별표
    oval(badge.c.x, badge.c.y, badge.r + (simple ? 22 : 26), cream.bottom)
    oval(badge.c.x, badge.c.y, badge.r, accent)
    let star = NSBezierPath()
    star.lineWidth = badge.r * (simple ? 0.26 : 0.21)
    star.lineCapStyle = .round
    let arm = badge.r * 0.58
    for i in 0..<4 {
        let a = Double(i) * .pi / 4
        star.move(to: NSPoint(x: badge.c.x - cos(a) * arm, y: badge.c.y - sin(a) * arm))
        star.line(to: NSPoint(x: badge.c.x + cos(a) * arm, y: badge.c.y + sin(a) * arm))
    }
    NSColor.white.setStroke()
    star.stroke()
}

func png(_ pixels: Int) -> Data {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    let ctx = NSGraphicsContext(bitmapImageRep: rep)!
    ctx.imageInterpolation = .high
    NSGraphicsContext.current = ctx
    let t = NSAffineTransform()
    t.scale(by: CGFloat(pixels) / 1024)
    t.concat()
    draw(simple: pixels <= 32)
    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

/// PNG를 그대로 담는 .ico(Windows Vista 이후 지원)
func ico(_ images: [(size: Int, data: Data)]) -> Data {
    var out = Data()
    func u16(_ v: Int) { out.append(contentsOf: [UInt8(v & 0xFF), UInt8((v >> 8) & 0xFF)]) }
    func u32(_ v: Int) { u16(v & 0xFFFF); u16((v >> 16) & 0xFFFF) }
    u16(0); u16(1); u16(images.count)
    var offset = 6 + 16 * images.count
    for (size, data) in images {
        out.append(UInt8(size >= 256 ? 0 : size)); out.append(UInt8(size >= 256 ? 0 : size))
        out.append(0); out.append(0)
        u16(1); u16(32)
        u32(data.count); u32(offset)
        offset += data.count
    }
    for (_, data) in images { out.append(data) }
    return out
}

let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
let outDir = CommandLine.arguments.count > 1
    ? URL(fileURLWithPath: CommandLine.arguments[1])
    : root.appendingPathComponent("assets/icon")
try FileManager.default.createDirectory(at: outDir, withIntermediateDirectories: true)

var icoImages: [(Int, Data)] = []
for size in [16, 32, 64, 128, 256, 512, 1024] {
    let data = png(size)
    try data.write(to: outDir.appendingPathComponent("icon-\(size).png"))
    if [16, 32, 64, 256].contains(size) || size == 48 { icoImages.append((size, data)) }
}
icoImages.insert((48, png(48)), at: 2)
try ico(icoImages).write(to: outDir.appendingPathComponent("app.ico"))
print("만들었습니다: \(outDir.path)")
