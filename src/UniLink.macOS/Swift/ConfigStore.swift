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
            deduplicateAccountsInternal()
        } catch {
            print("[ConfigStore] Error loading config: \(error), using default.")
            self.cachedConfig = AppConfig.default
        }
    }

    public func deduplicateAccounts() {
        lock.lock()
        defer { lock.unlock() }
        deduplicateAccountsInternal()
    }

    private func deduplicateAccountsInternal() {
        var seen = Set<String>()
        var unique: [Account] = []
        var hadDuplicates = false
        for a in cachedConfig.accounts {
            let u = a.username.trimmingCharacters(in: .whitespaces)
            guard !u.isEmpty else { continue }
            if !seen.contains(u) {
                seen.insert(u)
                unique.append(a)
            } else {
                hadDuplicates = true
            }
        }
        if hadDuplicates {
            cachedConfig.accounts = unique
            if !cachedConfig.accounts.contains(where: { $0.id == cachedConfig.activeAccountId }) {
                cachedConfig.activeAccountId = cachedConfig.accounts.first?.id ?? ""
            }
            saveInternal()
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
        let cleanUser = username.trimmingCharacters(in: .whitespaces)
        let cleanLabel = label.trimmingCharacters(in: .whitespaces)

        // If an account with this username already exists, update it instead of creating duplicate
        if let existing = accounts.first(where: { $0.username == cleanUser }) {
            _ = updateAccount(id: existing.id) { a in
                a.label = cleanLabel.isEmpty ? cleanUser : cleanLabel
                if !password.isEmpty { a.password = password }
            }
            return accounts.first(where: { $0.id == existing.id }) ?? existing
        }

        let acc = Account(label: cleanLabel.isEmpty ? cleanUser : cleanLabel, username: cleanUser, password: password)
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
                if cfg.accounts[i].id == id || cfg.accounts[i].username == id || cfg.accounts[i].label == id {
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
            cfg.accounts.removeAll { $0.id == id || $0.username == id || $0.label == id }
            if cfg.accounts.count < initialCount {
                removed = true
                if cfg.activeAccountId == id || !cfg.accounts.contains(where: { $0.id == cfg.activeAccountId }) {
                    cfg.activeAccountId = cfg.accounts.first?.id ?? ""
                }
            }
        }
        return removed
    }

    public func setActiveAccount(id: String) -> Bool {
        var found = false
        updateConfig { cfg in
            if let acc = cfg.accounts.first(where: { $0.id == id || $0.username == id || $0.label == id }) {
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
