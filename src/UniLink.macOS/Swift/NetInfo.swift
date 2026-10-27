import Foundation
import CoreWLAN

public enum NetInfo {
    public static func runCommand(_ args: [String], timeout: TimeInterval = 3.0) -> String {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: args[0])
        process.arguments = Array(args.dropFirst())

        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = Pipe()

        do {
            try process.run()
            process.waitUntilExit()
            let data = pipe.fileHandleForReading.readDataToEndOfFile()
            return String(data: data, encoding: .utf8) ?? ""
        } catch {
            return ""
        }
    }

    public static func findPrimaryInterface() -> String {
        let out = runCommand(["/usr/sbin/networksetup", "-listallhardwareports"])
        var currentPort = ""
        for line in out.components(separatedBy: .newlines) {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            if trimmed.hasPrefix("Hardware Port:") {
                currentPort = trimmed
            } else if trimmed.hasPrefix("Device:") {
                let dev = trimmed.replacingOccurrences(of: "Device:", with: "").trimmingCharacters(in: .whitespaces)
                if currentPort.lowercased().contains("wi-fi") || currentPort.lowercased().contains("airport") {
                    let ip = runCommand(["/usr/sbin/ipconfig", "getifaddr", dev]).trimmingCharacters(in: .whitespacesAndNewlines)
                    if !ip.isEmpty && ip.contains(".") {
                        return dev
                    }
                }
            }
        }

        // Secondary check: en0, en1
        for candidate in ["en0", "en1", "en2"] {
            let ip = runCommand(["/usr/sbin/ipconfig", "getifaddr", candidate]).trimmingCharacters(in: .whitespacesAndNewlines)
            if !ip.isEmpty && ip.contains(".") {
                return candidate
            }
        }
        return "en0"
    }

    public static func getWifiSSID(interface: String = "en0") -> String {
        // 1. Try CoreWLAN
        if let iface = CWWiFiClient.shared().interface(withName: interface), let ssid = iface.ssid(), !ssid.isEmpty {
            return ssid
        }
        if let iface = CWWiFiClient.shared().interface(), let ssid = iface.ssid(), !ssid.isEmpty {
            return ssid
        }

        // 2. Try networksetup
        let netOut = runCommand(["/usr/sbin/networksetup", "-getairportnetwork", interface])
        if let range = netOut.range(of: "Current Wi-Fi Network: ") {
            let ssid = String(netOut[range.upperBound...]).trimmingCharacters(in: .whitespacesAndNewlines)
            if !ssid.isEmpty && !ssid.contains("error") && !ssid.contains("You are not associated") {
                return ssid
            }
        }

        // 3. Try ipconfig getsummary
        let sumOut = runCommand(["/usr/sbin/ipconfig", "getsummary", interface])
        if let regex = try? NSRegularExpression(pattern: "^\\s*SSID\\s*:\\s*(.+)$", options: [.anchorsMatchLines]),
           let match = regex.firstMatch(in: sumOut, range: NSRange(sumOut.startIndex..<sumOut.endIndex, in: sumOut)),
           let r = Range(match.range(at: 1), in: sumOut) {
            let s = String(sumOut[r]).trimmingCharacters(in: .whitespacesAndNewlines)
            if !s.isEmpty && !s.contains("<redacted>") {
                return s
            }
        }

        // 4. Fallback based on DHCP domain
        let domain = getDomainName(interface: interface)
        if domain.lowercased().contains("urmia.ac.ir") {
            return "UUWN (دانشگاه ارومیه)"
        }

        return ""
    }

    public static func getLocalIPv4(interface: String = "en0") -> String {
        let ip = runCommand(["/usr/sbin/ipconfig", "getifaddr", interface]).trimmingCharacters(in: .whitespacesAndNewlines)
        if !ip.isEmpty && ip.contains(".") {
            return ip
        }

        // Fallback: getifaddrs
        var ifaddr: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&ifaddr) == 0, let firstAddr = ifaddr else { return "" }
        defer { freeifaddrs(ifaddr) }

        for ptr in sequence(first: firstAddr, next: { $0.pointee.ifa_next }) {
            let flags = Int32(ptr.pointee.ifa_flags)
            let addr = ptr.pointee.ifa_addr.pointee
            if (flags & (IFF_UP | IFF_RUNNING)) != 0 && addr.sa_family == UInt8(AF_INET) {
                let name = String(cString: ptr.pointee.ifa_name)
                if name == interface || (name.hasPrefix("en") && !name.contains("bridge")) {
                    var hostname = [CChar](repeating: 0, count: Int(NI_MAXHOST))
                    var sa = ptr.pointee.ifa_addr.pointee
                    if getnameinfo(&sa, socklen_t(addr.sa_len), &hostname, socklen_t(hostname.count), nil, 0, NI_NUMERICHOST) == 0 {
                        let result = String(cString: hostname)
                        if !result.hasPrefix("127.") && !result.hasPrefix("169.254.") {
                            return result
                        }
                    }
                }
            }
        }
        return ""
    }

    public static func getDefaultGateway(interface: String = "en0") -> String {
        // Direct physical DHCP router option (immune to VPN utun)
        let gw = runCommand(["/usr/sbin/ipconfig", "getoption", interface, "router"]).trimmingCharacters(in: .whitespacesAndNewlines)
        if !gw.isEmpty && gw.contains(".") {
            return gw
        }

        // Fallback: route get
        let out = runCommand(["/sbin/route", "-n", "get", "default"])
        let lines = out.components(separatedBy: .newlines)
        for line in lines {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            if trimmed.hasPrefix("gateway:") {
                let parts = trimmed.components(separatedBy: ":")
                if parts.count >= 2 {
                    let g = parts[1].trimmingCharacters(in: .whitespaces)
                    if !g.isEmpty { return g }
                }
            }
        }
        return ""
    }

    public static func getDomainName(interface: String = "en0") -> String {
        return runCommand(["/usr/sbin/ipconfig", "getoption", interface, "domain_name"]).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    public static func getDNSServers(interface: String = "en0") -> [String] {
        var results: [String] = []
        let dhcpDns = runCommand(["/usr/sbin/ipconfig", "getoption", interface, "domain_name_server"]).trimmingCharacters(in: .whitespacesAndNewlines)
        if !dhcpDns.isEmpty {
            for item in dhcpDns.components(separatedBy: CharacterSet(charactersIn: " ,;\t\n")) {
                let clean = item.trimmingCharacters(in: .whitespaces)
                if clean.contains(".") && !results.contains(clean) {
                    results.append(clean)
                }
            }
        }

        let out = runCommand(["/usr/sbin/scutil", "--dns"])
        let regex = try? NSRegularExpression(pattern: "nameserver\\[\\d+\\]\\s*:\\s*(\\S+)")
        let nsRange = NSRange(out.startIndex..<out.endIndex, in: out)
        if let matches = regex?.matches(in: out, range: nsRange) {
            for m in matches {
                if let r = Range(m.range(at: 1), in: out) {
                    let server = String(out[r])
                    if !results.contains(server) && !server.hasPrefix("127.") {
                        results.append(server)
                    }
                }
            }
        }
        return results
    }

    public static func isFakeIP(_ ip: String) -> Bool {
        if ip.hasPrefix("198.18.") || ip.hasPrefix("198.19.") || ip.hasPrefix("240.") || ip.hasPrefix("100.64.") {
            return true
        }
        return false
    }

    public static func checkInternet(interface: String = "en0", timeout: TimeInterval = 2.0) -> Bool {
        let probes = [
            "http://captive.apple.com/hotspot-detect.html",
            "http://connectivitycheck.gstatic.com/generate_204"
        ]

        for url in probes {
            let out = runCommand([
                "/usr/bin/curl", "-s", "-m", String(timeout), "--interface", interface, url
            ], timeout: timeout + 0.5)

            if url.contains("apple.com") && out.contains("Success") {
                return true
            }
            if url.contains("gstatic.com") && out.isEmpty {
                // generate_204 returns empty body with HTTP 204
                let code = runCommand([
                    "/usr/bin/curl", "-s", "-o", "/dev/null", "-w", "%{http_code}", "-m", String(timeout), "--interface", interface, url
                ], timeout: timeout + 0.5).trimmingCharacters(in: .whitespacesAndNewlines)
                if code == "204" {
                    return true
                }
            }
        }
        return false
    }

    public static func isHostReachable(host: String, port: Int, interface: String = "en0", timeout: TimeInterval = 2.0) -> Bool {
        let testUrl = "https://\(host):\(port)/"
        let code = runCommand([
            "/usr/bin/curl", "-k", "-s", "-o", "/dev/null", "-w", "%{http_code}", "-m", String(timeout), "--interface", interface, testUrl
        ], timeout: timeout + 0.5).trimmingCharacters(in: .whitespacesAndNewlines)

        if let status = Int(code), status > 0 {
            return true
        }
        return false
    }

    public static func inspect(matchers: [CampusMatcher], portalUrl: String) -> NetContext {
        let iface = findPrimaryInterface()
        var ctx = NetContext()
        ctx.interface = iface
        ctx.portal = portalUrl
        ctx.ip = getLocalIPv4(interface: iface)
        ctx.gateway = getDefaultGateway(interface: iface)
        ctx.dns = getDNSServers(interface: iface)
        ctx.domain = getDomainName(interface: iface)
        ctx.ssid = getWifiSSID(interface: iface)

        var matched: [String] = []

        // 1. SSID matchers
        if !ctx.ssid.isEmpty {
            for m in matchers {
                if m.type == "ssid" {
                    if ctx.ssid.lowercased() == m.value.lowercased() {
                        matched.append("ssid=\(m.value)")
                    }
                } else if m.type == "ssid_re" {
                    if (try? NSRegularExpression(pattern: m.value, options: [.caseInsensitive]))?.firstMatch(in: ctx.ssid, range: NSRange(ctx.ssid.startIndex..<ctx.ssid.endIndex, in: ctx.ssid)) != nil {
                        matched.append("ssid_re=\(m.value)")
                    }
                }
            }
        }

        // 2. Domain matcher
        if !ctx.domain.isEmpty {
            if ctx.domain.lowercased().contains("urmia.ac.ir") {
                matched.append("domain=urmia.ac.ir")
            }
            for m in matchers where m.type == "domain" {
                if ctx.domain.lowercased().contains(m.value.lowercased()) {
                    matched.append("domain=\(m.value)")
                }
            }
        }

        // 3. DNS matcher
        for m in matchers where m.type == "dns" {
            if ctx.dns.contains(where: { $0.hasPrefix(m.value) }) {
                matched.append("dns=\(m.value)")
            }
        }

        // 4. Portal reachable matcher directly via physical interface
        if let url = URL(string: portalUrl), let host = url.host {
            let port = url.port ?? (url.scheme == "https" ? 443 : 80)
            let reachable = isHostReachable(host: host, port: port, interface: iface, timeout: 1.5)
            ctx.portalReachable = reachable
            if reachable {
                matched.append("portal_reachable")
            }
        }

        ctx.campus = !matched.isEmpty
        ctx.reasons = matched
        ctx.online = checkInternet(interface: iface, timeout: 2.0)

        return ctx
    }
}
