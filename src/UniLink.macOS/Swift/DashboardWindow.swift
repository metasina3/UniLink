import Foundation
import AppKit
import WebKit

public final class DashboardController: NSObject, WKScriptMessageHandler {
    public static let shared = DashboardController()

    public var window: NSPanel?
    public var webView: WKWebView?

    public override init() {
        super.init()
    }

    public func show() {
        if window == nil {
            buildWindow()
        }
        syncAll()
        // Ensure window comes to front even if app runs in accessory mode
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
        window?.orderFrontRegardless()
    }

    private func buildWindow() {
        let style: NSWindow.StyleMask = [
            .titled,
            .closable,
            .miniaturizable,
            .resizable,
            .fullSizeContentView
        ]

        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 900, height: 640),
            styleMask: style,
            backing: .buffered,
            defer: false
        )
        panel.title = "UniLink — سامانه هوشمند اتصال دانشگاه"
        panel.titlebarAppearsTransparent = true
        panel.isReleasedWhenClosed = false
        panel.minSize = NSSize(width: 780, height: 540)
        panel.center()

        let config = WKWebViewConfiguration()
        let ucc = WKUserContentController()
        ucc.add(self, name: "pybridge")
        config.userContentController = ucc

        let wv = WKWebView(frame: NSRect(x: 0, y: 0, width: 900, height: 640), configuration: config)
        panel.contentView = wv

        self.window = panel
        self.webView = wv

        loadDashboardHtml()
    }

    private func loadDashboardHtml() {
        // Try multiple paths: Resources/gui/index.html or relative
        var htmlUrl: URL?

        if let bundleUrl = Bundle.main.url(forResource: "index", withExtension: "html", subdirectory: "gui") {
            htmlUrl = bundleUrl
        } else if let bundleUrl = Bundle.main.url(forResource: "index", withExtension: "html") {
            htmlUrl = bundleUrl
        } else {
            let possiblePaths = [
                Bundle.main.bundleURL.appendingPathComponent("Contents/Resources/gui/index.html"),
                Bundle.main.bundleURL.appendingPathComponent("Contents/Resources/unilink/gui/index.html"),
                URL(fileURLWithPath: "/Users/matthew/Projects/UniLink/src/UniLink.macOS/unilink/gui/index.html")
            ]
            htmlUrl = possiblePaths.first(where: { FileManager.default.fileExists(atPath: $0.path) })
        }

        if let url = htmlUrl, let html = try? String(contentsOf: url, encoding: .utf8) {
            webView?.loadHTMLString(html, baseURL: url.deletingLastPathComponent())
        }
    }

    public func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let body = message.body as? [String: Any] else { return }
        let action = body["action"] as? String ?? ""
        let payload = body["payload"] as? [String: Any] ?? [:]

        handleAction(action: action, payload: payload)
    }

    private func handleAction(action: String, payload: [String: Any]) {
        switch action {
        case "ready":
            syncAll()
        case "login":
            DispatchQueue.global(qos: .userInitiated).async {
                self.logToUi(tag: "auth", msg: "در حال ارسال درخواست ورود...")
                Watcher.shared.tick()
            }
        case "logout":
            DispatchQueue.global(qos: .userInitiated).async {
                let store = ConfigStore.shared
                if let active = store.activeAccount() {
                    let client = PortalClient(portalUrl: store.config.portalUrl)
                    _ = client.logout(username: active.username)
                    self.logToUi(tag: "auth", msg: "خروج انجام شد")
                }
                Watcher.shared.poke()
            }
        case "recheck":
            Watcher.shared.poke()
            logToUi(tag: "sys", msg: "دستور بررسی مجدد شبکه صادر شد")
        case "probe":
            DispatchQueue.global(qos: .userInitiated).async {
                let store = ConfigStore.shared
                let client = PortalClient(portalUrl: store.config.portalUrl)
                let res = client.probe()
                let msg = "پورتال: \(store.config.portalUrl) | پاسخ: HTTP \(res.status) | سایبروم: \(res.isCyberoam ? "بله" : "خیر")"
                self.logToUi(tag: "sys", msg: msg)
            }
        case "set_active_account":
            if let accId = payload["id"] as? String ?? payload["name"] as? String {
                _ = ConfigStore.shared.setActiveAccount(id: accId)
                syncAccounts()
                Watcher.shared.poke()
                logToUi(tag: "auth", msg: "حساب فعال تغییر یافت: \(accId)")
            }
        case "save_account":
            let accId = payload["id"] as? String ?? payload["old_name"] as? String ?? ""
            let label = payload["label"] as? String ?? payload["name"] as? String ?? ""
            let username = (payload["username"] as? String ?? "").trimmingCharacters(in: .whitespaces)
            let password = payload["password"] as? String ?? ""

            guard !username.isEmpty else { return }

            let store = ConfigStore.shared
            var updated = false
            if !accId.isEmpty {
                updated = store.updateAccount(id: accId) { a in
                    a.label = label.isEmpty ? username : label
                    a.username = username
                    if !password.isEmpty { a.password = password }
                }
            }
            if !updated {
                _ = store.addAccount(label: label, username: username, password: password)
                logToUi(tag: "auth", msg: "حساب جدید «\(label.isEmpty ? username : label)» اضافه شد")
            } else {
                logToUi(tag: "auth", msg: "حساب «\(label.isEmpty ? username : label)» ویرایش شد")
            }
            syncAccounts()
        case "delete_account":
            if let accId = payload["id"] as? String ?? payload["name"] as? String {
                _ = ConfigStore.shared.removeAccount(id: accId)
                syncAccounts()
                logToUi(tag: "auth", msg: "حساب کاربری حذف گردید")
            }
        case "test_account":
            let accId = payload["id"] as? String ?? payload["name"] as? String ?? ""
            DispatchQueue.global(qos: .userInitiated).async {
                let store = ConfigStore.shared
                if let acc = store.accounts.first(where: { $0.id == accId || $0.label == accId || $0.username == accId }) {
                    self.logToUi(tag: "auth", msg: "آزمایش ورود با «\(acc.label)»...")
                    let client = PortalClient(portalUrl: store.config.portalUrl)
                    let reply = client.login(username: acc.username, password: acc.password)
                    let tag = reply.kind == .online ? "ok" : "err"
                    self.logToUi(tag: tag, msg: "نتیجه آزمایش: \(reply.kind.rawValue) — \(reply.message)")
                }
            }
        case "save_settings":
            let store = ConfigStore.shared
            store.updateConfig { cfg in
                if let auto = payload["auto_login"] as? Bool { cfg.autoConnect = auto }
                if let failover = payload["auto_switch_on_failure"] as? Bool { cfg.autoSwitchOnFailure = failover }
                if let interval = payload["check_interval_seconds"] as? Int { cfg.checkIntervalSeconds = interval }
                if let hb = payload["keepalive_seconds"] as? Int { cfg.heartbeatSeconds = hb }
                if let gate = payload["gate_url"] as? String, !gate.isEmpty { cfg.portalUrl = gate }
            }
            logToUi(tag: "sys", msg: "تنظیمات ذخیره شد")
            syncSettings()
            Watcher.shared.poke()
        case "detect_current_wifi":
            let ssid = NetInfo.getWifiSSID()
            evalJs("window.onCurrentWifiDetected(\(jsonString(ssid)));")
        default:
            break
        }
    }

    public func syncAll() {
        syncState()
        syncAccounts()
        syncSettings()
    }

    public func syncState() {
        let snap = Watcher.shared.snapshot()
        let store = ConfigStore.shared
        let active = store.activeAccount()

        let stateDict: [String: Any] = [
            "status": snap.status,
            "message": snap.message,
            "ssid": snap.ssid,
            "ip": snap.ip,
            "campus": snap.campus,
            "online": snap.online,
            "active_account": active?.label ?? "بدون حساب"
        ]
        evalJs("window.updateAppState(\(jsonString(stateDict)));")
    }

    public func syncAccounts() {
        let store = ConfigStore.shared
        let accList = store.accounts.map { a -> [String: Any] in
            [
                "id": a.id,
                "name": a.label,
                "username": a.username,
                "password": a.password,
                "gate_url": "",
                "note": "",
                "enabled": a.enabled,
                "fail_count": a.failCount
            ]
        }
        let activeName = store.activeAccount()?.label ?? ""
        evalJs("window.updateAccounts(\(jsonString(accList)), \(jsonString(activeName)));")
    }

    public func syncSettings() {
        let cfg = ConfigStore.shared.config
        let ssids = cfg.campusMatchers.filter { $0.type == "ssid" }.map { $0.value }
        let dict: [String: Any] = [
            "auto_login": cfg.autoConnect,
            "auto_switch_on_failure": cfg.autoSwitchOnFailure,
            "check_interval_seconds": cfg.checkIntervalSeconds,
            "keepalive_seconds": cfg.heartbeatSeconds,
            "gate_url": cfg.portalUrl,
            "ssids": ssids.isEmpty ? ["UUWN"] : ssids,
            "launch_at_login": false
        ]
        evalJs("window.updateSettings(\(jsonString(dict)));")
    }

    public func logToUi(tag: String, msg: String) {
        evalJs("window.addLogMessage(\(jsonString(tag)), \(jsonString(msg)));")
    }

    private func evalJs(_ script: String) {
        DispatchQueue.main.async {
            self.webView?.evaluateJavaScript(script, completionHandler: nil)
        }
    }

    private func jsonString(_ obj: Any) -> String {
        if let str = obj as? String {
            let escaped = str.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
            return "\"\(escaped)\""
        }
        guard let data = try? JSONSerialization.data(withJSONObject: obj, options: []),
              let str = String(data: data, encoding: .utf8) else {
            return "{}"
        }
        return str
    }
}
