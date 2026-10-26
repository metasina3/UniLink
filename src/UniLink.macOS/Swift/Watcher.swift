import Foundation
import AppKit

public final class Watcher {
    public static let shared = Watcher()

    public var onStateChange: ((WatcherState) -> Void)?
    private var state = WatcherState()
    private let lock = NSLock()
    private var timer: Timer?
    private var isRunning = false
    private var lastHeartbeat: Double = 0.0
    private var backoffInterval: Double = 0.0

    public init() {}

    public func snapshot() -> WatcherState {
        lock.lock()
        defer { lock.unlock() }
        return state
    }

    private func updateState(_ mutate: (inout WatcherState) -> Void) {
        lock.lock()
        mutate(&state)
        let copy = state
        lock.unlock()

        DispatchQueue.main.async {
            self.onStateChange?(copy)
        }
    }

    public func start() {
        guard !isRunning else { return }
        isRunning = true
        scheduleNext(delay: 0.5)
    }

    public func stop() {
        isRunning = false
        timer?.invalidate()
        timer = nil
    }

    public func poke() {
        DispatchQueue.global(qos: .userInitiated).async {
            self.tick()
        }
    }

    private func scheduleNext(delay: Double) {
        DispatchQueue.main.async {
            self.timer?.invalidate()
            self.timer = Timer.scheduledTimer(withTimeInterval: delay, repeats: false) { [weak self] _ in
                DispatchQueue.global(qos: .utility).async {
                    self?.tick()
                    if let self = self, self.isRunning {
                        let cfg = ConfigStore.shared.config
                        var nextDelay = Double(cfg.checkIntervalSeconds)
                        if self.backoffInterval > 0 {
                            nextDelay = min(nextDelay, self.backoffInterval)
                        }
                        self.scheduleNext(delay: max(2.0, nextDelay))
                    }
                }
            }
        }
    }

    public func tick() {
        let store = ConfigStore.shared
        let cfg = store.config

        guard cfg.autoConnect else {
            updateState { s in
                s.status = "paused"
                s.message = "اتصال خودکار غیرفعال است"
            }
            return
        }

        let ctx = NetInfo.inspect(matchers: cfg.campusMatchers, portalUrl: cfg.portalUrl)
        let active = store.activeAccount()
        let activeLabel = active?.label ?? "بدون حساب"

        updateState { s in
            s.campus = ctx.campus
            s.online = ctx.online
            s.ssid = ctx.ssid
            s.ip = ctx.ip
            s.activeAccount = activeLabel
        }

        if !ctx.campus {
            updateState { s in
                s.status = "idle"
                s.message = "خارج از شبکه دانشگاه"
            }
            backoffInterval = 0.0
            return
        }

        // On campus
        if ctx.online {
            let now = Date().timeIntervalSince1970
            let hbInterval = Double(cfg.heartbeatSeconds)
            if (now - lastHeartbeat >= hbInterval) && active != nil {
                let client = PortalClient(portalUrl: cfg.portalUrl)
                let reply = client.heartbeat(username: active!.username)
                lastHeartbeat = now

                if reply.kind == .signedOut || reply.kind == .quota {
                    updateState { s in
                        s.status = "campus"
                        s.message = "نشست قطع شد: \(reply.message)"
                    }
                    poke()
                    return
                }
            }

            updateState { s in
                s.status = "online"
                s.message = "متصل به اینترنت دانشگاه"
            }
            backoffInterval = 0.0
            return
        }

        // Campus + offline -> trigger auto-login
        updateState { s in
            s.status = "logging-in"
            s.message = "در حال ورود با حساب \(activeLabel)..."
        }

        attemptLogin(portalUrl: cfg.portalUrl)
    }

    private func attemptLogin(portalUrl: String) {
        let store = ConfigStore.shared
        let candidates = store.orderedCandidates()

        if candidates.isEmpty {
            updateState { s in
                s.status = "failed"
                s.message = "هیچ حساب فعالی تعریف نشده است"
            }
            backoffInterval = 45.0
            return
        }

        let toTry = store.config.autoSwitchOnFailure ? candidates : Array(candidates.prefix(1))
        let client = PortalClient(portalUrl: portalUrl)

        for acc in toTry {
            let reply = client.login(username: acc.username, password: acc.password)
            let now = Date().timeIntervalSince1970
            let record = "\(acc.label) (\(acc.username)): \(reply.kind.rawValue) - \(reply.message)"

            updateState { s in
                s.attempts.append(record)
                if s.attempts.count > 12 { s.attempts.removeFirst() }
                s.lastAttempt = now
            }

            if reply.kind == .online {
                _ = store.updateAccount(id: acc.id) { a in
                    a.lastLogin = now
                    a.failCount = 0
                }
                _ = store.setActiveAccount(id: acc.id)
                lastHeartbeat = now
                backoffInterval = 0.0

                updateState { s in
                    s.status = "online"
                    s.online = true
                    s.message = "ورود موفق با \(acc.label)"
                    s.activeAccount = acc.label
                    s.lastOk = now
                }
                notify("UniLink", "ورود موفق با حساب \(acc.label)", "متصل به اینترنت دانشگاه")
                return
            }

            // Failed
            _ = store.updateAccount(id: acc.id) { a in
                a.failCount += 1
            }

            if reply.kind == .quota {
                let formatter = DateFormatter()
                formatter.dateFormat = "yyyy-MM"
                let monthStr = formatter.string(from: Date())
                _ = store.updateAccount(id: acc.id) { a in
                    a.exhaustedMonth = monthStr
                }
                notify("UniLink", "حجم حساب \(acc.label) به پایان رسید", "در حال تغییر به حساب بعدی...")
            }
        }

        // All failed
        backoffInterval = 25.0
        updateState { s in
            s.status = "failed"
            s.message = "ورود ناموفق با تمامی حساب‌ها"
        }
    }

    private func notify(_ title: String, _ message: String, _ subtitle: String = "") {
        let script = "display notification \"\(message)\" with title \"\(title)\" subtitle \"\(subtitle)\""
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        proc.arguments = ["-e", script]
        try? proc.run()
    }
}
