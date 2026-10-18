import AppKit

// 앱 아이콘(.iconset)을 그린다: 주황 둥근 사각형 안에 흰 별표(메뉴 막대·트레이 아이콘과 같은 모양).
//   swift macos/make-icon.swift <출력.iconset>   →   iconutil -c icns <출력.iconset>

let output = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "AppIcon.iconset")
try? FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)

func draw(_ pixels: Int) -> Data? {
    let size = CGFloat(pixels)
    guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8,
                                     samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                                     bytesPerRow: 0, bitsPerPixel: 0) else { return nil }
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)

    // macOS 아이콘 격자: 1024 안에 824 몸통
    let inset = size * 100 / 1024
    let body = NSRect(x: inset, y: inset, width: size - inset * 2, height: size - inset * 2)
    let shape = NSBezierPath(roundedRect: body, xRadius: body.width * 0.225, yRadius: body.width * 0.225)

    let shadow = NSShadow()
    shadow.shadowColor = NSColor.black.withAlphaComponent(0.25)
    shadow.shadowOffset = NSSize(width: 0, height: -size * 0.01)
    shadow.shadowBlurRadius = size * 0.02
    NSGraphicsContext.saveGraphicsState()
    shadow.set()
    NSColor(srgbRed: 0.85, green: 0.47, blue: 0.34, alpha: 1).setFill()
    shape.fill()
    NSGraphicsContext.restoreGraphicsState()

    NSGradient(starting: NSColor(srgbRed: 0.91, green: 0.56, blue: 0.43, alpha: 1),
               ending: NSColor(srgbRed: 0.78, green: 0.38, blue: 0.25, alpha: 1))?.draw(in: shape, angle: -90)

    let star = NSBezierPath()
    star.lineWidth = body.width * 0.085
    star.lineCapStyle = .round
    let c = NSPoint(x: size / 2, y: size / 2)
    let r = body.width * 0.27
    for i in 0..<4 {
        let a = Double(i) * .pi / 4
        star.move(to: NSPoint(x: c.x - cos(a) * r, y: c.y - sin(a) * r))
        star.line(to: NSPoint(x: c.x + cos(a) * r, y: c.y + sin(a) * r))
    }
    NSColor.white.setStroke()
    star.stroke()

    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])
}

for base in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let name = scale == 1 ? "icon_\(base)x\(base).png" : "icon_\(base)x\(base)@2x.png"
        try? draw(base * scale)?.write(to: output.appendingPathComponent(name))
    }
}
