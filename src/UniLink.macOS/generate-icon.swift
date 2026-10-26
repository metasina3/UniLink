import Foundation
import AppKit

func createUniLinkAppIcon(size: CGFloat) -> NSImage {
    let img = NSImage(size: NSSize(width: size, height: size))
    img.lockFocus()
    guard let ctx = NSGraphicsContext.current?.cgContext else {
        img.unlockFocus()
        return img
    }

    ctx.setAllowsAntialiasing(true)
    ctx.setShouldAntialias(true)

    let s = size / 1024.0

    // 1. Drop shadow for macOS Squircle
    ctx.saveGState()
    let shadowColor = NSColor(calibratedRed: 0.01, green: 0.02, blue: 0.05, alpha: 0.65).cgColor
    ctx.setShadow(offset: CGSize(width: 0, height: -16 * s), blur: 30 * s, color: shadowColor)

    let squircleRect = CGRect(x: 104 * s, y: 104 * s, width: 816 * s, height: 816 * s)
    let cornerRadius = 184.0 * s
    let squirclePath = CGPath(roundedRect: squircleRect, cornerWidth: cornerRadius, cornerHeight: cornerRadius, transform: nil)
    ctx.addPath(squirclePath)
    ctx.setFillColor(NSColor(calibratedRed: 0.04, green: 0.07, blue: 0.14, alpha: 1.0).cgColor)
    ctx.fillPath()
    ctx.restoreGState()

    // 2. Background Gradient inside squircle
    ctx.saveGState()
    ctx.addPath(squirclePath)
    ctx.clip()

    let colorSpace = CGColorSpaceCreateDeviceRGB()
    let bgColors = [
        NSColor(calibratedRed: 0.09, green: 0.15, blue: 0.28, alpha: 1.0).cgColor, // Deep Royal Navy
        NSColor(calibratedRed: 0.03, green: 0.05, blue: 0.10, alpha: 1.0).cgColor  // Pitch Dark
    ] as CFArray
    if let bgGrad = CGGradient(colorsSpace: colorSpace, colors: bgColors, locations: [0.0, 1.0]) {
        ctx.drawLinearGradient(bgGrad, start: CGPoint(x: 512 * s, y: 920 * s), end: CGPoint(x: 512 * s, y: 104 * s), options: [])
    }

    // Radial Cyan/Blue Ambient Center Light
    let glowColors = [
        NSColor(calibratedRed: 0.06, green: 0.65, blue: 0.98, alpha: 0.35).cgColor,
        NSColor(calibratedRed: 0.02, green: 0.30, blue: 0.75, alpha: 0.0).cgColor
    ] as CFArray
    if let radial = CGGradient(colorsSpace: colorSpace, colors: glowColors, locations: [0.0, 1.0]) {
        ctx.drawRadialGradient(radial, startCenter: CGPoint(x: 512 * s, y: 480 * s), startRadius: 20 * s, endCenter: CGPoint(x: 512 * s, y: 480 * s), endRadius: 400 * s, options: [])
    }

    // Subtle Grid pattern / concentric tech rings in background
    ctx.setLineWidth(1.0 * s)
    ctx.setStrokeColor(NSColor(calibratedWhite: 1.0, alpha: 0.05).cgColor)
    ctx.strokeEllipse(in: CGRect(x: 212 * s, y: 180 * s, width: 600 * s, height: 600 * s))
    ctx.strokeEllipse(in: CGRect(x: 312 * s, y: 280 * s, width: 400 * s, height: 400 * s))

    // Inner highlight border
    let innerBorder = CGPath(roundedRect: squircleRect.insetBy(dx: 1.5 * s, dy: 1.5 * s), cornerWidth: cornerRadius - 1.5 * s, cornerHeight: cornerRadius - 1.5 * s, transform: nil)
    ctx.addPath(innerBorder)
    ctx.setLineWidth(2.5 * s)
    ctx.setStrokeColor(NSColor(calibratedWhite: 1.0, alpha: 0.22).cgColor)
    ctx.strokePath()

    ctx.restoreGState() // unclip

    // ========================================================
    // 3. FUSION: URMIA UNIVERSITY LOGO + WI-FI TRANSMISSION
    // ========================================================
    ctx.saveGState()

    let cyanGlow = NSColor(calibratedRed: 0.06, green: 0.75, blue: 0.98, alpha: 1.0)
    let whitePure = NSColor(calibratedRed: 0.98, green: 0.99, blue: 1.0, alpha: 1.0)

    // Center Wi-Fi Node location
    let nodeCenter = CGPoint(x: 512 * s, y: 450 * s)

    // A. The 3 Glowing Wi-Fi Waves radiating upward from university dome
    // Outer Wave
    let wave3 = CGMutablePath()
    wave3.addArc(center: nodeCenter, radius: 340 * s, startAngle: .pi * 0.28, endAngle: .pi * 0.72, clockwise: false)
    ctx.saveGState()
    ctx.setShadow(offset: .zero, blur: 16 * s, color: cyanGlow.withAlphaComponent(0.6).cgColor)
    ctx.addPath(wave3)
    ctx.setLineCap(.round)
    ctx.setLineWidth(30 * s)
    ctx.setStrokeColor(cyanGlow.cgColor)
    ctx.strokePath()
    ctx.restoreGState()

    // Middle Wave
    let wave2 = CGMutablePath()
    wave2.addArc(center: nodeCenter, radius: 250 * s, startAngle: .pi * 0.27, endAngle: .pi * 0.73, clockwise: false)
    ctx.saveGState()
    ctx.setShadow(offset: .zero, blur: 14 * s, color: cyanGlow.withAlphaComponent(0.5).cgColor)
    ctx.addPath(wave2)
    ctx.setLineCap(.round)
    ctx.setLineWidth(28 * s)
    ctx.setStrokeColor(whitePure.cgColor)
    ctx.strokePath()
    ctx.restoreGState()

    // Inner Wave
    let wave1 = CGMutablePath()
    wave1.addArc(center: nodeCenter, radius: 165 * s, startAngle: .pi * 0.25, endAngle: .pi * 0.75, clockwise: false)
    ctx.saveGState()
    ctx.setShadow(offset: .zero, blur: 12 * s, color: cyanGlow.withAlphaComponent(0.5).cgColor)
    ctx.addPath(wave1)
    ctx.setLineCap(.round)
    ctx.setLineWidth(26 * s)
    ctx.setStrokeColor(cyanGlow.cgColor)
    ctx.strokePath()
    ctx.restoreGState()

    // B. Urmia University Emblem Architecture
    // Draw the iconic Urmia University Emblem
    let logoPath = "/Users/matthew/.hermes/cache/scratch/logo.png"
    if let logoImg = NSImage(contentsOfFile: logoPath) {
        // Draw the university logo with pristine high-DPI scaling and cyan-white tint
        let logoW = 390.0 * s
        let logoH = 450.0 * s
        let logoRect = CGRect(x: 512 * s - logoW / 2.0, y: 195 * s, width: logoW, height: logoH)

        ctx.saveGState()
        ctx.setShadow(offset: CGSize(width: 0, height: 4 * s), blur: 18 * s, color: cyanGlow.withAlphaComponent(0.7).cgColor)

        // Draw tinted logo
        if let cgImg = logoImg.cgImage(forProposedRect: nil, context: nil, hints: nil) {
            // Mask with logo alpha and fill with radiant cyan-white gradient
            ctx.clip(to: logoRect, mask: cgImg)

            let emblemGradColors = [
                NSColor(calibratedRed: 0.95, green: 0.98, blue: 1.0, alpha: 1.0).cgColor,
                NSColor(calibratedRed: 0.06, green: 0.75, blue: 0.98, alpha: 1.0).cgColor
            ] as CFArray
            if let emblemGrad = CGGradient(colorsSpace: colorSpace, colors: emblemGradColors, locations: [0.0, 1.0]) {
                ctx.drawLinearGradient(emblemGrad, start: CGPoint(x: 512 * s, y: 195 * s + logoH), end: CGPoint(x: 512 * s, y: 195 * s), options: [])
            }
        }
        ctx.restoreGState()
    }

    // C. Glowing Wi-Fi Center Dot / Pulse
    let dotRadius = 24.0 * s
    let dotRect = CGRect(x: nodeCenter.x - dotRadius, y: nodeCenter.y - dotRadius, width: dotRadius * 2, height: dotRadius * 2)

    ctx.saveGState()
    ctx.setShadow(offset: .zero, blur: 20 * s, color: cyanGlow.cgColor)
    ctx.setFillColor(cyanGlow.cgColor)
    ctx.fillEllipse(in: dotRect)

    // Inner bright white core
    ctx.setFillColor(NSColor.white.cgColor)
    ctx.fillEllipse(in: dotRect.insetBy(dx: 7 * s, dy: 7 * s))
    ctx.restoreGState()

    ctx.restoreGState() // unclip emblem

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
    let img = createUniLinkAppIcon(size: px)
    guard let tiff = img.tiffRepresentation,
          let rep = NSBitmapImageRep(data: tiff),
          let png = rep.representation(using: .png, properties: [:]) else {
        continue
    }
    let fileUrl = URL(fileURLWithPath: "\(iconsetDir)/\(filename)")
    try png.write(to: fileUrl)
    print("Rendered:", filename, "\(Int(px))x\(Int(px))")
}
print("Successfully generated all iconset resolutions with official Urmia University + Wi-Fi fusion!")
