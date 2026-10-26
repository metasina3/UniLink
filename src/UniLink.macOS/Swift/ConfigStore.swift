import Foundation

public final class ConfigStore {
    public static let shared = ConfigStore()

    public let configUrl: URL
    private var cachedConfig: AppConfig
    private let lock = NSLock()

    public init(customUrl: URL? = nil) {
        if let customUrl = customUrl {
            self.configUrl = customUrl
        } else {
            let appSupport = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
            let dir = appSupport.appendingPathComponent("UniLink")
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            self.configUrl = dir.appendingPathComponent("config.json")
        }

        self.cachedConfig = AppConfig.default
        load()
    }

    public func load() {
        lock.lock()
        defer { lock.unlock() }

        guard FileManager.default.fileExists(atPath: configUrl.path) else {
            self.cachedConfig = AppConfig.default
            saveInternal()
            return
        }

        do {
            let data = try Data(contentsOf: configUrl)
            let decoded = try JSONDecoder().decode(AppConfig.self, from: data)
            self.cachedConfig = decoded
        } catch {
            print("[ConfigStore] Error loading config: \(error), using default.")
            self.cachedConfig = AppConfig.default
        }
    }

    public func save() {
        lock.lock()
        defer { lock.unlock() }
        saveInternal()
    }

    private func saveInternal() {
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            let data = try encoder.encode(cachedConfig)

            let dir = configUrl.deletingLastPathComponent()
            try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)

            let tmpUrl = dir.appendingPathComponent("config.json.tmp-\(UUID().uuidString.prefix(6))")
            try data.write(to: tmpUrl, options: .atomic)

            // chmod 0600
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: tmpUrl.path)
            _ = try? FileManager.default.removeItem(at: configUrl)
            try FileManager.default.moveItem(at: tmpUrl, to: configUrl)
        } catch {
            print("[ConfigStore] Error saving config: \(error)")
        }
    }

    public var config: AppConfig {
        lock.lock()
        defer { lock.unlock() }
        return cachedConfig
    }

    public func updateConfig(_ mutate: (inout AppConfig) -> Void) {
        lock.lock()
        defer { lock.unlock() }
        mutate(&cachedConfig)
        saveInternal()
    }

    public var accounts: [Account] {
        config.accounts
    }

    public func activeAccount() -> Account? {
        let current = config
        if !current.activeAccountId.isEmpty {
            if let found = current.accounts.first(where: { $0.id == current.activeAccountId || $0.username == current.activeAccountId }) {
                return found
            }
        }
        return current.accounts.first(where: { $0.enabled }) ?? current.accounts.first
    }

    public func addAccount(label: String, username: String, password: String) -> Account {
        let acc = Account(label: label, username: username, password: password)
        updateConfig { cfg in
            cfg.accounts.append(acc)
            if cfg.activeAccountId.isEmpty {
                cfg.activeAccountId = acc.id
            }
        }
        return acc
    }

    public func updateAccount(id: String, mutate: (inout Account) -> Void) -> Bool {
        var updated = false
        updateConfig { cfg in
            for i in 0..<cfg.accounts.count {
                if cfg.accounts[i].id == id || cfg.accounts[i].username == id {
                    mutate(&cfg.accounts[i])
                    updated = true
                    break
                }
            }
        }
        return updated
    }

    public func removeAccount(id: String) -> Bool {
        var removed = false
        updateConfig { cfg in
            let initialCount = cfg.accounts.count
            cfg.accounts.removeAll { $0.id == id || $0.username == id }
            if cfg.accounts.count < initialCount {
                removed = true
                if cfg.activeAccountId == id {
                    cfg.activeAccountId = cfg.accounts.first?.id ?? ""
                }
            }
        }
        return removed
    }

    public func setActiveAccount(id: String) -> Bool {
        var found = false
        updateConfig { cfg in
            if let acc = cfg.accounts.first(where: { $0.id == id || $0.username == id }) {
                cfg.activeAccountId = acc.id
                found = true
            }
        }
        return found
    }

    public func orderedCandidates() -> [Account] {
        let all = accounts.filter { $0.enabled }
        guard let active = activeAccount() else { return all }
        var result = [active]
        result.append(contentsOf: all.filter { $0.id != active.id })
        return result
    }
}
