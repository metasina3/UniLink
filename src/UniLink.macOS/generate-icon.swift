import Foundation
import AppKit

func renderIcon(size: CGFloat) -> NSImage {
    let img = NSImage(size: NSSize(width: size, height: size))
    img.lockFocus()
    guard let ctx = NSGraphicsContext.current?.cgContext else {
        img.unlockFocus()
        return img
    }

    ctx.setAllowsAntialiasing(true)
    ctx.setShouldAntialias(true)

    let scale = size / 1024.0

    // Coordinate system: (0,0) at bottom-left in Cocoa lockFocus
    // 1. Drop shadow for the squircle
    ctx.saveGState()
    let shadowColor = NSColor(calibratedRed: 0.02, green: 0.04, blue: 0.08, alpha: 0.55).cgColor
    ctx.setShadow(offset: CGSize(width: 0, height: -14 * scale), blur: 28 * scale, color: shadowColor)

    // Squircle rect
    let squircleRect = CGRect(x: 104 * scale, y: 104 * scale, width: 816 * scale, height: 816 * scale)
    let cornerRadius = 184.0 * scale
    let squirclePath = CGPath(roundedRect: squircleRect, cornerWidth: cornerRadius, cornerHeight: cornerRadius, transform: nil)
    ctx.addPath(squirclePath)
    ctx.setFillColor(NSColor(calibratedRed: 0.08, green: 0.11, blue: 0.18, alpha: 1.0).cgColor)
    ctx.fillPath()
    ctx.restoreGState()

    // 2. Base Gradient Fill inside squircle
    ctx.saveGState()
    ctx.addPath(squirclePath)
    ctx.clip()

    let colorSpace = CGColorSpaceCreateDeviceRGB()
    let gradColors = [
        NSColor(calibratedRed: 0.12, green: 0.18, blue: 0.32, alpha: 1.0).cgColor, // Deep navy
        NSColor(calibratedRed: 0.06, green: 0.09, blue: 0.16, alpha: 1.0).cgColor  // Dark slate
    ] as CFArray
    if let gradient = CGGradient(colorsSpace: colorSpace, colors: gradColors, locations: [0.0, 1.0]) {
        ctx.drawLinearGradient(
            gradient,
            start: CGPoint(x: 512 * scale, y: 920 * scale),
            end: CGPoint(x: 512 * scale, y: 104 * scale),
            options: []
        )
    }

    // Radial ambient glow in center
    let glowColors = [
        NSColor(calibratedRed: 0.22, green: 0.45, blue: 0.95, alpha: 0.32).cgColor,
        NSColor(calibratedRed: 0.06, green: 0.25, blue: 0.70, alpha: 0.0).cgColor
    ] as CFArray
    if let radialGrad = CGGradient(colorsSpace: colorSpace, colors: glowColors, locations: [0.0, 1.0]) {
        ctx.drawRadialGradient(
            radialGrad,
            startCenter: CGPoint(x: 512 * scale, y: 560 * scale),
            startRadius: 20 * scale,
            endCenter: CGPoint(x: 512 * scale, y: 560 * scale),
            endRadius: 360 * scale,
            options: []
        )
    }

    // Inner highlight border
    let innerBorderPath = CGPath(roundedRect: squircleRect.insetBy(dx: 1.5 * scale, dy: 1.5 * scale), cornerWidth: cornerRadius - 1.5 * scale, cornerHeight: cornerRadius - 1.5 * scale, transform: nil)
    ctx.addPath(innerBorderPath)
    ctx.setLineWidth(2.5 * scale)
    ctx.setStrokeColor(NSColor(calibratedWhite: 1.0, alpha: 0.18).cgColor)
    ctx.strokePath()

    ctx.restoreGState() // unclip

    // 3. Central Gateway / Portal Arch
    // Draw inverted U-shape arch
    ctx.saveGState()
    let gatePath = CGMutablePath()
    let leftX = 330.0 * scale
    let rightX = 694.0 * scale
    let bottomY = 280.0 * scale
    let archTopY = 530.0 * scale
    let archRadius = (rightX - leftX) / 2.0

    gatePath.move(to: CGPoint(x: leftX, y: bottomY))
    gatePath.addLine(to: CGPoint(x: leftX, y: archTopY))
    gatePath.addArc(
        center: CGPoint(x: 512 * scale, y: archTopY),
        radius: archRadius,
        startAngle: .pi,
        endAngle: 0,
        clockwise: true
    )
    gatePath.addLine(to: CGPoint(x: rightX, y: bottomY))

    // Gate subtle glow
    ctx.setShadow(
        offset: CGSize(width: 0, height: 0),
        blur: 16 * scale,
        color: NSColor(calibratedRed: 0.06, green: 0.71, blue: 0.98, alpha: 0.45).cgColor
    )

    ctx.addPath(gatePath)
    ctx.setLineCap(.round)
    ctx.setLineJoin(.round)
    ctx.setLineWidth(52 * scale)

    let gateColors = [
        NSColor(calibratedRed: 0.02, green: 0.71, blue: 0.98, alpha: 1.0).cgColor, // Cyan
        NSColor(calibratedRed: 0.23, green: 0.51, blue: 0.96, alpha: 1.0).cgColor  // Bright Blue
    ] as CFArray
    if let gateGrad = CGGradient(colorsSpace: colorSpace, colors: gateColors, locations: [0.0, 1.0]) {
        ctx.replacePathWithStrokedPath()
        ctx.clip()
        ctx.drawLinearGradient(
            gateGrad,
            start: CGPoint(x: 330 * scale, y: 280 * scale),
            end: CGPoint(x: 694 * scale, y: 720 * scale),
            options: []
        )
    }
    ctx.restoreGState()

    // 4. Wi-Fi Signal Arcs
    ctx.saveGState()
    ctx.setLineCap(.round)
    ctx.setShadow(offset: CGSize(width: 0, height: -2 * scale), blur: 6 * scale, color: NSColor.black.withAlphaComponent(0.3).cgColor)

    // Outer Wi-Fi Arc
    let outerArc = CGMutablePath()
    let wifiCenter = CGPoint(x: 512 * scale, y: 390 * scale)
    outerArc.addArc(center: wifiCenter, radius: 260 * scale, startAngle: .pi * 0.25, endAngle: .pi * 0.75, clockwise: false)
    ctx.addPath(outerArc)
    ctx.setLineWidth(32 * scale)
    ctx.setStrokeColor(NSColor(calibratedRed: 0.96, green: 0.97, blue: 0.99, alpha: 0.95).cgColor)
    ctx.strokePath()

    // Middle Wi-Fi Arc
    let midArc = CGMutablePath()
    midArc.addArc(center: wifiCenter, radius: 175 * scale, startAngle: .pi * 0.27, endAngle: .pi * 0.73, clockwise: false)
    ctx.addPath(midArc)
    ctx.setLineWidth(30 * scale)
    ctx.setStrokeColor(NSColor(calibratedRed: 0.88, green: 0.92, blue: 0.98, alpha: 0.90).cgColor)
    ctx.strokePath()

    // 5. Center Connection Node (Dot)
    let nodeRadius = 26.0 * scale
    let nodeRect = CGRect(
        x: wifiCenter.x - nodeRadius,
        y: wifiCenter.y - nodeRadius + 30 * scale,
        width: nodeRadius * 2,
        height: nodeRadius * 2
    )

    ctx.setShadow(
        offset: CGSize(width: 0, height: 0),
        blur: 14 * scale,
        color: NSColor(calibratedRed: 0.06, green: 0.71, blue: 0.98, alpha: 0.7).cgColor
    )
    ctx.setFillColor(NSColor(calibratedRed: 0.15, green: 0.65, blue: 0.98, alpha: 1.0).cgColor)
    ctx.fillEllipse(in: nodeRect)

    // Inner white highlight in dot
    let flareRadius = 10.0 * scale
    let flareRect = CGRect(
        x: wifiCenter.x - flareRadius,
        y: wifiCenter.y - flareRadius + 30 * scale,
        width: flareRadius * 2,
        height: flareRadius * 2
    )
    ctx.setFillColor(NSColor.white.cgColor)
    ctx.fillEllipse(in: flareRect)

    ctx.restoreGState()

    img.unlockFocus()
    return img
}

let iconsetDir = "/Users/matthew/.hermes/cache/scratch/UniLink.iconset"
try? FileManager.default.removeItem(atPath: iconsetDir)
try FileManager.default.createDirectory(atPath: iconsetDir, withIntermediateDirectories: true)

let targets: [(String, CGFloat)] = [
    ("icon_16x16.png", 16),
    ("icon_16x16@2x.png", 32),
    ("icon_32x32.png", 32),
    ("icon_32x32@2x.png", 64),
    ("icon_128x128.png", 128),
    ("icon_128x128@2x.png", 256),
    ("icon_256x256.png", 256),
    ("icon_256x256@2x.png", 512),
    ("icon_512x512.png", 512),
    ("icon_512x512@2x.png", 1024)
]

for (filename, px) in targets {
    let img = renderIcon(size: px)
    guard let tiff = img.tiffRepresentation,
          let rep = NSBitmapImageRep(data: tiff),
          let png = rep.representation(using: .png, properties: [:]) else {
        print("Failed to encode", filename)
        continue
    }
    let fileUrl = URL(fileURLWithPath: "\(iconsetDir)/\(filename)")
    try png.write(to: fileUrl)
    print("Rendered:", filename, "\(Int(px))x\(Int(px))")
}
print("All icon images successfully rendered to iconset!")
