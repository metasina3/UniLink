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
        menu.autoenablesItems = false

        let itemDashboard = NSMenuItem(title: "🖥️ داشبورد و کنترل‌پنل...", action: #selector(openDashboard), keyEquivalent: "d")
        itemDashboard.target = self
        itemDashboard.isEnabled = true
        menu.addItem(itemDashboard)
        menu.addItem(NSMenuItem.separator())

        itemStatus = NSMenuItem(title: "در حال بررسی...", action: nil, keyEquivalent: "")
        itemStatus.isEnabled = false
        itemNet = NSMenuItem(title: "شبکه: —", action: nil, keyEquivalent: "")
        itemNet.isEnabled = false
        itemAccount = NSMenuItem(title: "حساب: —", action: nil, keyEquivalent: "")
        itemAccount.isEnabled = false

        menu.addItem(itemStatus)
        menu.addItem(itemNet)
        menu.addItem(itemAccount)
        menu.addItem(NSMenuItem.separator())

        let itemLogin = NSMenuItem(title: "ورود به اینترنت (اتصال)", action: #selector(doLogin), keyEquivalent: "l")
        itemLogin.target = self
        itemLogin.isEnabled = true
        menu.addItem(itemLogin)

        let itemLogout = NSMenuItem(title: "خروج از پورتال (قطع اتصال)", action: #selector(doLogout), keyEquivalent: "o")
        itemLogout.target = self
        itemLogout.isEnabled = true
        menu.addItem(itemLogout)

        itemAccountsMenu = NSMenuItem(title: "انتخاب و تغییر حساب", action: nil, keyEquivalent: "")
        itemAccountsMenu.isEnabled = true
        let accountsSubmenu = NSMenu()
        accountsSubmenu.autoenablesItems = false
        itemAccountsMenu.submenu = accountsSubmenu
        menu.addItem(itemAccountsMenu)

        itemToggleAuto = NSMenuItem(title: "اتصال خودکار", action: #selector(toggleAuto), keyEquivalent: "")
        itemToggleAuto.target = self
        itemToggleAuto.isEnabled = true
        menu.addItem(itemToggleAuto)

        menu.addItem(NSMenuItem.separator())

        let itemProbe = NSMenuItem(title: "بررسی وضعیت پورتال (Probe)", action: #selector(doProbe), keyEquivalent: "p")
        itemProbe.target = self
        itemProbe.isEnabled = true
        menu.addItem(itemProbe)

        menu.addItem(NSMenuItem.separator())

        let itemQuit = NSMenuItem(title: "خروج از UniLink", action: #selector(quitApp), keyEquivalent: "q")
        itemQuit.target = self
        itemQuit.isEnabled = true
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
        case "online": statusFa = "متصل به اینترنت دانشگاه (سبز)"
        case "campus": statusFa = "شبکه دانشگاه — آماده ورود"
        case "logging-in": statusFa = "در حال ورود..."
        case "failed": statusFa = "ورود ناموفق"
        case "paused": statusFa = "غیرفعال"
        default: statusFa = "خارج از شبکه دانشگاه"
        }

        itemStatus.title = "● وضعیت: \(statusFa)"
        let ssid = snap.ssid.isEmpty ? "بدون اتصال Wi-Fi" : snap.ssid
        let ip = snap.ip.isEmpty ? "—" : snap.ip
        itemNet.title = "شبکه: \(ssid) (\(ip))"
        itemAccount.title = "حساب فعال: \(active?.label ?? "تعریف نشده")"
        itemToggleAuto.state = store.config.autoConnect ? .on : .off

        // Refresh accounts submenu
        if let submenu = itemAccountsMenu.submenu {
            submenu.removeAllItems()
            let all = store.accounts
            if all.isEmpty {
                let emptyItem = NSMenuItem(title: "هیچ حسابی ثبت نشده", action: nil, keyEquivalent: "")
                emptyItem.isEnabled = false
                submenu.addItem(emptyItem)
            } else {
                for a in all {
                    let mark = (active?.id == a.id) ? "✓ " : "   "
                    let item = NSMenuItem(title: "\(mark)\(a.label) (\(a.username))", action: #selector(switchAccount(_:)), keyEquivalent: "")
                    item.target = self
                    item.representedObject = a.id
                    item.isEnabled = true
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
        let store = ConfigStore.shared
        guard let active = store.activeAccount() else {
            DashboardController.shared.show()
            DashboardController.shared.logToUi(tag: "err", msg: "هیچ حسابی برای ورود انتخاب نشده است")
            return
        }
        DashboardController.shared.logToUi(tag: "auth", msg: "درخواست ورود با حساب «\(active.label)» صادر شد")
        Watcher.shared.forceLoginNow()
    }

    @objc private func doLogout() {
        let store = ConfigStore.shared
        if let active = store.activeAccount() {
            DispatchQueue.global(qos: .userInitiated).async {
                let client = PortalClient(portalUrl: store.config.portalUrl)
                _ = client.logout(username: active.username)
                Watcher.shared.logoutNow()
                DashboardController.shared.logToUi(tag: "auth", msg: "خروج از حساب «\(active.label)» انجام شد")
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
        Watcher.shared.forceLoginNow()
    }

    @objc private func openDashboard() {
        DashboardController.shared.show()
    }

    @objc private func doProbe() {
        DispatchQueue.global(qos: .userInitiated).async {
            let store = ConfigStore.shared
            let client = PortalClient(portalUrl: store.config.portalUrl)
            let res = client.probe()
            let msg = "پورتال: \(store.config.portalUrl) | کد پاسخ: HTTP \(res.status) | سایبروم: \(res.isCyberoam ? "بله" : "خیر")"
            DashboardController.shared.logToUi(tag: "sys", msg: msg)
            let notifyMsg = res.ok ? "پورتال دانشگاه در دسترس است (کد \(res.status))" : "پورتال در دسترس نیست (کد \(res.status))"
            Watcher.shared.notify("UniLink Probe", notifyMsg, store.config.portalUrl)
            Watcher.shared.poke()
        }
    }

    @objc private func quitApp() {
        Watcher.shared.stop()
        NSApp.terminate(nil)
    }
}
