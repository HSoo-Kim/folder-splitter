import AppKit
import QuartzCore
import SwiftUI

// Design system shared by every view. Values are deliberate; reuse them instead of inventing
// new ones so spacing, timing and type stay consistent across slices.

enum Metrics {
    // 4-point grid.
    static let xs: CGFloat = 4
    static let s: CGFloat = 8
    static let m: CGFloat = 12
    static let l: CGFloat = 16
    static let xl: CGFloat = 24

    static let smallRadius: CGFloat = 5     // chips, tab close buttons, row highlights
    static let mediumRadius: CGFloat = 8    // tabs, text fields, popovers' inner content
    static let largeRadius: CGFloat = 12    // overlays, sheets' cards, drop zones

    static let tabBarHeight: CGFloat = 32
    static let tabHeight: CGFloat = 26
    static let tabMinWidth: CGFloat = 64
    static let tabMaxWidth: CGFloat = 220
    static let addressBarHeight: CGFloat = 28
    static let statusBarHeight: CGFloat = 22
    static let activePaneOutline: CGFloat = 2
    static let paneGap: CGFloat = 1          // split view divider thickness

    /// List row height for the user's text size and density.
    static func rowHeight(_ size: TextSize, _ density: RowDensity) -> CGFloat {
        let base: CGFloat
        switch size {
        case .small: base = 18
        case .medium: base = 20
        case .large: base = 24
        }
        return base + (density == .comfortable ? 6 : 2)
    }

    /// List icon edge for the text size.
    static func listIconSize(_ size: TextSize) -> CGFloat {
        switch size {
        case .small: return 16
        case .medium: return 16
        case .large: return 20
        }
    }
}

enum Typography {
    /// File list text. System font; it carries Apple's optical sizes and tracking tables.
    static func list(_ size: TextSize) -> NSFont {
        switch size {
        case .small: return .systemFont(ofSize: 12)
        case .medium: return .systemFont(ofSize: 13)
        case .large: return .systemFont(ofSize: 15)
        }
    }

    /// Secondary columns (date, size, kind) use tabular figures so digits line up.
    static func listSecondary(_ size: TextSize) -> NSFont {
        let font = list(size)
        return NSFont.monospacedDigitSystemFont(ofSize: font.pointSize, weight: .regular)
    }

    static let tabTitle = NSFont.systemFont(ofSize: 12, weight: .regular)
    static let tabTitleSelected = NSFont.systemFont(ofSize: 12, weight: .medium)
    static let address = NSFont.systemFont(ofSize: 13)
    static let status = NSFont.monospacedDigitSystemFont(ofSize: 11, weight: .regular)
    static let sectionHeader = NSFont.systemFont(ofSize: 11, weight: .semibold)
    /// Large headings in sheets; slightly negative tracking as type grows.
    static func title(_ size: CGFloat = 17) -> NSFont { .systemFont(ofSize: size, weight: .semibold) }
    static let titleKern: CGFloat = -0.2
}

@MainActor
enum Palette {
    static func paneHighlight(_ choice: PaneHighlight) -> NSColor {
        switch choice {
        case .accent: return .controlAccentColor
        case .blue: return .systemBlue
        case .green: return .systemGreen
        case .orange: return .systemOrange
        }
    }

    /// Fill behind a drop target or a pressed control. Semantic so it adapts to light/dark.
    static var dropHighlight: NSColor { NSColor.controlAccentColor.withAlphaComponent(0.18) }
    static var dropOutline: NSColor { NSColor.controlAccentColor.withAlphaComponent(0.85) }
    /// Thin separator; heavier when the user asked for more contrast.
    static var separator: NSColor { Motion.increaseContrast ? .labelColor.withAlphaComponent(0.6) : .separatorColor }
}

/// Motion follows Apple's fluid-interface guidance: critically damped springs by default,
/// bounce only after a gesture carried momentum, every animation interruptible and started
/// from the on-screen value, and a gentle cross-fade (or nothing) when Reduce Motion is on.
@MainActor
enum Motion {
    static var reduceMotion: Bool { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }
    static var reduceTransparency: Bool { NSWorkspace.shared.accessibilityDisplayShouldReduceTransparency }
    static var increaseContrast: Bool { NSWorkspace.shared.accessibilityDisplayShouldIncreaseContrast }

    /// Default UI spring (move/reposition): damping 1.0, response 0.35 s.
    nonisolated static let standardResponse = 0.35
    /// Snappier spring for small things (tabs, chips, highlights).
    nonisolated static let quickResponse = 0.25
    /// Bounce used only after a flick or drag release (damping 0.8).
    nonisolated static let momentumDamping = 0.8

    /// Animates `animator()` changes made in `changes` with a spring. Interruptible: starting a
    /// new animation on the same property retargets from the current presentation value.
    /// With Reduce Motion the change happens with a short ease-out fade-length duration instead.
    static func animate(response: Double = standardResponse, dampingFraction: Double = 1.0,
                        _ changes: () -> Void, completion: (() -> Void)? = nil) {
        if reduceMotion {
            NSAnimationContext.runAnimationGroup({ context in
                context.duration = 0.12
                context.timingFunction = CAMediaTimingFunction(name: .easeOut)
                context.allowsImplicitAnimation = true
                changes()
            }, completionHandler: completion)
            return
        }
        NSAnimationContext.animate(.spring(response: response, dampingFraction: dampingFraction),
                                   changes: changes, completion: completion)
    }

    /// Applies `changes` without animation (for state restored on launch, live drags, etc.).
    static func withoutAnimation(_ changes: () -> Void) {
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0
            context.allowsImplicitAnimation = false
            changes()
        }
    }

    /// Layer spring for `keyPath`, starting from the layer's presentation value so an
    /// interruption never jumps. `initialVelocity` is in units of the property per second and
    /// is normalized by the remaining distance as Core Animation expects.
    static func spring(_ layer: CALayer, keyPath: String, to value: CGFloat,
                       response: Double = standardResponse, dampingFraction: Double = 1.0,
                       velocity: CGFloat = 0) {
        let current = (layer.presentation()?.value(forKeyPath: keyPath) as? CGFloat)
            ?? (layer.value(forKeyPath: keyPath) as? CGFloat) ?? value
        layer.setValue(value, forKeyPath: keyPath)
        guard !reduceMotion, current != value else {
            layer.removeAnimation(forKey: keyPath)
            return
        }
        let animation = CASpringAnimation(keyPath: keyPath)
        let omega = 2 * Double.pi / response
        animation.mass = 1
        animation.stiffness = omega * omega
        animation.damping = 2 * dampingFraction * omega
        let distance = value - current
        animation.initialVelocity = distance == 0 ? 0 : Double(velocity / distance)
        animation.fromValue = current
        animation.toValue = value
        animation.duration = animation.settlingDuration
        animation.isRemovedOnCompletion = true
        layer.add(animation, forKey: keyPath)
    }

    /// Opacity fade used to materialize/dematerialize overlays (Reduce Motion keeps fades).
    static func fade(_ layer: CALayer, to opacity: Float, duration: Double = 0.18) {
        let current = layer.presentation()?.opacity ?? layer.opacity
        layer.opacity = opacity
        let animation = CABasicAnimation(keyPath: "opacity")
        animation.fromValue = current
        animation.toValue = opacity
        animation.duration = duration
        animation.timingFunction = CAMediaTimingFunction(name: .easeOut)
        layer.add(animation, forKey: "opacity")
    }

    /// Progressive resistance past a boundary (Apple's rubber-band constant 0.55).
    static func rubberband(_ overshoot: CGFloat, dimension: CGFloat, constant: CGFloat = 0.55) -> CGFloat {
        guard dimension > 0 else { return 0 }
        return (overshoot * dimension * constant) / (dimension + constant * abs(overshoot))
    }

    /// Where a flick comes to rest (UIScrollView-style exponential deceleration).
    static func project(velocity: CGFloat, decelerationRate: CGFloat = 0.998) -> CGFloat {
        (velocity / 1000) * decelerationRate / (1 - decelerationRate)
    }
}
