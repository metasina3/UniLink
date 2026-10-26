import Foundation
import AppKit

public final class MenuBarController: NSObject {
    public static let shared = MenuBarController()

    private var statusItem: NSStatusItem!
    private var menu: NSMenu!
    private var itemStatus: NSMenuItem!
    private var itemNet: NSMenuItem!
    private var itemAccount: NSMenuItem!
    private var itemToggleAuto: NSMenuItem!
    private var itemAccountsMenu: NSMenuItem!

    public override init() {
        super.init()
    }

    public func setup() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        buildMenu()
        updateIcon(status: "idle")

        Watcher.shared.onStateChange = { [weak self] _ in
            self?.refreshMenu()
        }
        refreshMenu()
    }

    private func buildMenu() {
        menu = NSMenu()

        itemStatus = NSMenuItem(title: "در حال بررسی...", action: nil, keyEquivalent: "")
        itemNet = NSMenuItem(title: "شبکه: —", action: nil, keyEquivalent: "")
        itemAccount = NSMenuItem(title: "حساب: —", action: nil, keyEquivalent: "")

        menu.addItem(itemStatus)
        menu.addItem(itemNet)
        menu.addItem(itemAccount)
        menu.addItem(NSMenuItem.separator())

        let itemLogin = NSMenuItem(title: "ورود همین حالا", action: #selector(doLogin), keyEquivalent: "")
        itemLogin.target = self
        menu.addItem(itemLogin)

        let itemLogout = NSMenuItem(title: "خروج از پورتال", action: #selector(doLogout), keyEquivalent: "")
        itemLogout.target = self
        menu.addItem(itemLogout)

        itemAccountsMenu = NSMenuItem(title: "تغییر حساب", action: nil, keyEquivalent: "")
        let accountsSubmenu = NSMenu()
        itemAccountsMenu.submenu = accountsSubmenu
        menu.addItem(itemAccountsMenu)

        itemToggleAuto = NSMenuItem(title: "اتصال خودکار", action: #selector(toggleAuto), keyEquivalent: "")
        itemToggleAuto.target = self
        menu.addItem(itemToggleAuto)

        menu.addItem(NSMenuItem.separator())

        let itemDashboard = NSMenuItem(title: "داشبورد و تنظیمات UniLink...", action: #selector(openDashboard), keyEquivalent: ",")
        itemDashboard.target = self
        menu.addItem(itemDashboard)

        let itemProbe = NSMenuItem(title: "بررسی وضعیت پورتال (Probe)", action: #selector(doProbe), keyEquivalent: "")
        itemProbe.target = self
        menu.addItem(itemProbe)

        menu.addItem(NSMenuItem.separator())

        let itemQuit = NSMenuItem(title: "خروج از UniLink", action: #selector(quitApp), keyEquivalent: "q")
        itemQuit.target = self
        menu.addItem(itemQuit)

        statusItem.menu = menu
    }

    public func updateIcon(status: String) {
        let symbolName: String
        switch status {
        case "online":
            symbolName = "wifi"
        case "campus":
            symbolName = "wifi.exclamationmark"
        case "logging-in":
            symbolName = "arrow.triangle.2.circlepath"
        case "failed":
            symbolName = "wifi.slash"
        default:
            symbolName = "wifi.slash"
        }

        if let button = statusItem.button {
            if let image = NSImage(systemSymbolName: symbolName, accessibilityDescription: "UniLink") {
                let config = NSImage.SymbolConfiguration(pointSize: 14, weight: .regular)
                button.image = image.withSymbolConfiguration(config)
                button.title = ""
            } else {
                button.image = nil
                button.title = status == "online" ? "●" : "○"
            }
        }
    }

    public func refreshMenu() {
        let snap = Watcher.shared.snapshot()
        let store = ConfigStore.shared
        let active = store.activeAccount()

        updateIcon(status: snap.status)

        let statusFa: String
        switch snap.status {
        case "online": statusFa = "متصل به اینترنت دانشگاه"
        case "campus": statusFa = "شبکه دانشگاه — نیاز به ورود"
        case "logging-in": statusFa = "در حال ورود..."
        case "failed": statusFa = "ورود ناموفق"
        case "paused": statusFa = "غیرفعال"
        default: statusFa = "خارج از شبکه دانشگاه"
        }

        itemStatus.title = "وضعیت: \(statusFa)"
        let ssid = snap.ssid.isEmpty ? "بدون اتصال Wi-Fi" : snap.ssid
        let ip = snap.ip.isEmpty ? "—" : snap.ip
        itemNet.title = "شبکه: \(ssid) (\(ip))"
        itemAccount.title = "حساب: \(active?.label ?? "تعریف نشده")"
        itemToggleAuto.state = store.config.autoConnect ? .on : .off

        // Refresh accounts submenu
        if let submenu = itemAccountsMenu.submenu {
            submenu.removeAllItems()
            let all = store.accounts
            if all.isEmpty {
                submenu.addItem(NSMenuItem(title: "هیچ حسابی ثبت نشده", action: nil, keyEquivalent: ""))
            } else {
                for a in all {
                    let item = NSMenuItem(title: "\(a.label) (\(a.username))", action: #selector(switchAccount(_:)), keyEquivalent: "")
                    item.target = self
                    item.representedObject = a.id
                    if active?.id == a.id {
                        item.state = .on
                    }
                    submenu.addItem(item)
                }
            }
        }

        DashboardController.shared.syncState()
    }

    @objc private func doLogin() {
        Watcher.shared.poke()
    }

    @objc private func doLogout() {
        let store = ConfigStore.shared
        if let active = store.activeAccount() {
            DispatchQueue.global(qos: .userInitiated).async {
                let client = PortalClient(portalUrl: store.config.portalUrl)
                _ = client.logout(username: active.username)
                Watcher.shared.poke()
            }
        }
    }

    @objc private func toggleAuto() {
        let store = ConfigStore.shared
        let cur = store.config.autoConnect
        store.updateConfig { $0.autoConnect = !cur }
        refreshMenu()
        Watcher.shared.poke()
    }

    @objc private func switchAccount(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String else { return }
        _ = ConfigStore.shared.setActiveAccount(id: id)
        refreshMenu()
        Watcher.shared.poke()
    }

    @objc private func openDashboard() {
        DashboardController.shared.show()
    }

    @objc private func doProbe() {
        Watcher.shared.poke()
    }

    @objc private func quitApp() {
        Watcher.shared.stop()
        NSApp.terminate(nil)
    }
}
