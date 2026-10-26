import Foundation
import AppKit

let args = CommandLine.arguments

if args.count > 1 {
    let cmd = args[1]
    let store = ConfigStore.shared

    switch cmd {
    case "status":
        let cfg = store.config
        let ctx = NetInfo.inspect(matchers: cfg.campusMatchers, portalUrl: cfg.portalUrl)
        print("UniLink (Swift Native) Status:")
        print("  Wi-Fi SSID:   \(ctx.ssid.isEmpty ? "—" : ctx.ssid)")
        print("  Local IP:     \(ctx.ip.isEmpty ? "—" : ctx.ip)")
        print("  Gateway:      \(ctx.gateway.isEmpty ? "—" : ctx.gateway)")
        print("  Campus Net:   \(ctx.campus ? "Yes" : "No") (\(ctx.reasons.joined(separator: ", ")))")
        print("  Portal:       \(ctx.portalReachable ? "Reachable" : "Unreachable")")
        print("  Internet:     \(ctx.online ? "Online" : "Offline")")
        let act = store.activeAccount()
        print("  Active User:  \(act != nil ? "\(act!.label) (\(act!.username))" : "None")")
        exit(0)

    case "probe":
        let cfg = store.config
        let client = PortalClient(portalUrl: cfg.portalUrl)
        let res = client.probe()
        print("Portal URL:   \(cfg.portalUrl)")
        print("HTTP Status:  \(res.status)")
        print("Cyberoam:     \(res.isCyberoam ? "Yes" : "No")")
        print("Internet:     \(NetInfo.checkInternet() ? "Online" : "Offline")")
        exit(0)

    case "login":
        let cfg = store.config
        guard let act = store.activeAccount() else {
            print("Error: No active account found.")
            exit(1)
        }
        let client = PortalClient(portalUrl: cfg.portalUrl)
        print("Logging in with \(act.label) (\(act.username))...")
        let reply = client.login(username: act.username, password: act.password)
        print("[\(reply.kind.rawValue)] \(reply.message)")
        exit(reply.kind == .online ? 0 : 1)

    case "logout":
        let cfg = store.config
        guard let act = store.activeAccount() else {
            print("Error: No active account found.")
            exit(1)
        }
        let client = PortalClient(portalUrl: cfg.portalUrl)
        let reply = client.logout(username: act.username)
        print("[\(reply.kind.rawValue)] \(reply.message)")
        exit(0)

    case "accounts":
        let sub = args.count > 2 ? args[2] : "list"
        if sub == "list" {
            let accs = store.accounts
            let act = store.activeAccount()
            print("Accounts:")
            for a in accs {
                let mark = act?.id == a.id ? "✓" : " "
                print(" \(mark) \(a.label) (\(a.username))")
            }
        }
        exit(0)

    case "gui":
        // Fall through to show dashboard window
        break

    default:
        print("Usage: UniLink [status|probe|login|logout|accounts|gui]")
        exit(0)
    }
}

// GUI Menu Bar App Execution
class AppDelegate: NSObject, NSApplicationDelegate {
    var openDashboardImmediately: Bool = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        MenuBarController.shared.setup()
        Watcher.shared.start()
        if openDashboardImmediately {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) {
                DashboardController.shared.show()
            }
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        Watcher.shared.stop()
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
let wantsGui = args.contains("gui") || args.contains("--gui")
delegate.openDashboardImmediately = wantsGui
app.delegate = delegate
app.setActivationPolicy(wantsGui ? .regular : .accessory)
app.run()
