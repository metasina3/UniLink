import Foundation

public enum ReplyKind: String, Codable {
    case online = "Online"
    case signedOut = "SignedOut"
    case quota = "Quota"
    case credentials = "Credentials"
    case challenge = "Challenge"
    case unknown = "Unknown"
}

public struct PortalReply {
    public let kind: ReplyKind
    public let message: String
    public let status: String
    public let raw: String

    public init(kind: ReplyKind, message: String = "", status: String = "", raw: String = "") {
        self.kind = kind
        self.message = message
        self.status = status
        self.raw = raw
    }
}

public struct Account: Codable, Identifiable {
    public var id: String
    public var label: String
    public var username: String
    public var password: String
    public var enabled: Bool
    public var exhaustedMonth: String
    public var failCount: Int
    public var lastLogin: Double

    public init(
        id: String = UUID().uuidString.prefix(12).lowercased(),
        label: String = "",
        username: String = "",
        password: String = "",
        enabled: Bool = true,
        exhaustedMonth: String = "",
        failCount: Int = 0,
        lastLogin: Double = 0.0
    ) {
        self.id = id
        self.label = label.isEmpty ? username : label
        self.username = username
        self.password = password
        self.enabled = enabled
        self.exhaustedMonth = exhaustedMonth
        self.failCount = failCount
        self.lastLogin = lastLogin
    }
}

public struct CampusMatcher: Codable {
    public var type: String
    public var value: String

    public init(type: String, value: String) {
        self.type = type
        self.value = value
    }
}

public struct AppConfig: Codable {
    public var version: Int
    public var portalUrl: String
    public var activeAccountId: String
    public var autoConnect: Bool
    public var autoSwitchOnFailure: Bool
    public var heartbeatSeconds: Int
    public var checkIntervalSeconds: Int
    public var campusMatchers: [CampusMatcher]
    public var accounts: [Account]

    public static var `default`: AppConfig {
        AppConfig(
            version: 1,
            portalUrl: "https://192.168.100.40:8090/",
            activeAccountId: "",
            autoConnect: true,
            autoSwitchOnFailure: true,
            heartbeatSeconds: 120,
            checkIntervalSeconds: 20,
            campusMatchers: [
                CampusMatcher(type: "ssid", value: "UUWN"),
                CampusMatcher(type: "ssid_re", value: "(?i)urmia|uuwn|univ|uni[-_ ]?wifi|eduroam|student"),
                CampusMatcher(type: "dns", value: "78.38."),
                CampusMatcher(type: "probe", value: "192.168.100.40:8090"),
                CampusMatcher(type: "probe", value: "gate1.urmia.ac.ir:8090")
            ],
            accounts: []
        )
    }

    enum CodingKeys: String, CodingKey {
        case version
        case portalUrl = "portal_url"
        case activeAccountId = "active_account_id"
        case autoConnect = "auto_connect"
        case autoSwitchOnFailure = "auto_switch_on_failure"
        case heartbeatSeconds = "heartbeat_seconds"
        case checkIntervalSeconds = "check_interval_seconds"
        case campusMatchers = "campus_matchers"
        case accounts
    }
}

public struct NetContext {
    public var ssid: String = ""
    public var ip: String = ""
    public var gateway: String = ""
    public var dns: [String] = []
    public var online: Bool = false
    public var portalReachable: Bool = false
    public var campus: Bool = false
    public var reasons: [String] = []
    public var portal: String = ""

    public var summary: String {
        var parts: [String] = []
        parts.append("wifi=\(ssid.isEmpty ? "—" : ssid)")
        if !ip.isEmpty { parts.append("ip=\(ip)") }
        if !gateway.isEmpty { parts.append("gw=\(gateway)") }
        parts.append(online ? "online" : "offline")
        parts.append(campus ? "campus" : "not-campus")
        return parts.joined(separator: " ")
    }
}

public struct WatcherState {
    public var campus: Bool = false
    public var online: Bool = false
    public var ssid: String = ""
    public var ip: String = ""
    public var status: String = "idle" // idle | campus | logging-in | online | failed | paused | error
    public var message: String = "آماده"
    public var activeAccount: String = ""
    public var lastAttempt: Double = 0.0
    public var lastOk: Double = 0.0
    public var attempts: [String] = []

    public init() {}
}
