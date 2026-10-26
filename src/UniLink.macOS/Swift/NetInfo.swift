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

    public static func getWifiSSID(interface: String = "en0") -> String {
        // Try CoreWLAN first
        if let iface = CWWiFiClient.shared().interface(withName: interface), let ssid = iface.ssid(), !ssid.isEmpty {
            return ssid
        }
        if let iface = CWWiFiClient.shared().interface(), let ssid = iface.ssid(), !ssid.isEmpty {
            return ssid
        }

        // Fallback: networksetup
        let out = runCommand(["/usr/sbin/networksetup", "-getairportnetwork", interface])
        if let range = out.range(of: "Current Wi-Fi Network: ") {
            let ssid = String(out[range.upperBound...]).trimmingCharacters(in: .whitespacesAndNewlines)
            if !ssid.isEmpty && !ssid.contains("error") {
                return ssid
            }
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
                        if !result.hasPrefix("127.") {
                            return result
                        }
                    }
                }
            }
        }
        return ""
    }

    public static func getDefaultGateway() -> String {
        let out = runCommand(["/sbin/route", "-n", "get", "default"])
        let lines = out.components(separatedBy: .newlines)
        for line in lines {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            if trimmed.hasPrefix("gateway:") {
                let parts = trimmed.components(separatedBy: ":")
                if parts.count >= 2 {
                    return parts[1].trimmingCharacters(in: .whitespaces)
                }
            }
        }
        return ""
    }

    public static func getDNSServers() -> [String] {
        let out = runCommand(["/usr/sbin/scutil", "--dns"])
        var results: [String] = []
        let regex = try? NSRegularExpression(pattern: "nameserver\\[\\d+\\]\\s*:\\s*(\\S+)")
        let nsRange = NSRange(out.startIndex..<out.endIndex, in: out)
        if let matches = regex?.matches(in: out, range: nsRange) {
            for m in matches {
                if let r = Range(m.range(at: 1), in: out) {
                    results.append(String(out[r]))
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

    public static func checkInternet(timeout: TimeInterval = 3.0) -> Bool {
        let probes = [
            "http://captive.apple.com/hotspot-detect.html",
            "http://connectivitycheck.gstatic.com/generate_204",
            "http://detectportal.firefox.com/success.txt"
        ]
        let group = DispatchGroup()
        var success = false

        for urlString in probes {
            guard let url = URL(string: urlString) else { continue }
            group.enter()
            var req = URLRequest(url: url)
            req.timeoutInterval = timeout
            req.cachePolicy = .reloadIgnoringLocalAndRemoteCacheData

            let session = URLSession(configuration: .ephemeral)
            let task = session.dataTask(with: req) { data, response, _ in
                defer { group.leave() }
                if let http = response as? HTTPURLResponse {
                    if urlString.contains("apple.com"), let data = data, let text = String(data: data, encoding: .utf8), text.contains("Success") {
                        success = true
                    } else if urlString.contains("gstatic.com") && http.statusCode == 204 {
                        success = true
                    } else if (200..<400).contains(http.statusCode) && !success {
                        if let data = data, let text = String(data: data, encoding: .utf8)?.lowercased(), !text.contains("captive") && !text.contains("cyberoam") {
                            success = true
                        }
                    }
                }
            }
            task.resume()
        }

        _ = group.wait(timeout: .now() + timeout + 0.5)
        return success
    }

    public static func isHostReachable(host: String, port: Int, timeout: TimeInterval = 2.0) -> Bool {
        var hints = addrinfo(
            ai_flags: AI_NUMERICSERV,
            ai_family: AF_INET,
            ai_socktype: SOCK_STREAM,
            ai_protocol: 0,
            ai_addrlen: 0,
            ai_canonname: nil,
            ai_addr: nil,
            ai_next: nil
        )
        var res: UnsafeMutablePointer<addrinfo>?
        let portStr = String(port)
        if getaddrinfo(host, portStr, &hints, &res) != 0 || res == nil {
            return false
        }
        defer { freeaddrinfo(res) }

        let sock = socket(res!.pointee.ai_family, res!.pointee.ai_socktype, res!.pointee.ai_protocol)
        if sock < 0 { return false }
        defer { close(sock) }

        // Set non-blocking for timeout
        let flags = fcntl(sock, F_GETFL, 0)
        _ = fcntl(sock, F_SETFL, flags | O_NONBLOCK)

        _ = connect(sock, res!.pointee.ai_addr, res!.pointee.ai_addrlen)

        var fdSet = fd_set()
        fdSet.zero()
        fdSet.set(sock)

        var tv = timeval(tv_sec: Int(timeout), tv_usec: 0)
        let ret = select(sock + 1, nil, &fdSet, nil, &tv)
        return ret > 0
    }

    public static func inspect(matchers: [CampusMatcher], portalUrl: String) -> NetContext {
        var ctx = NetContext()
        ctx.portal = portalUrl
        ctx.ssid = getWifiSSID()
        ctx.ip = getLocalIPv4()
        ctx.gateway = getDefaultGateway()
        ctx.dns = getDNSServers()

        var matched: [String] = []
        for m in matchers {
            if m.type == "ssid" && !ctx.ssid.isEmpty {
                if ctx.ssid.lowercased() == m.value.lowercased() {
                    matched.append("ssid=\(m.value)")
                }
            } else if m.type == "ssid_re" && !ctx.ssid.isEmpty {
                if (try? NSRegularExpression(pattern: m.value, options: [.caseInsensitive]))?.firstMatch(in: ctx.ssid, range: NSRange(ctx.ssid.startIndex..<ctx.ssid.endIndex, in: ctx.ssid)) != nil {
                    matched.append("ssid_re=\(m.value)")
                }
            } else if m.type == "dns" {
                if ctx.dns.contains(where: { $0.hasPrefix(m.value) }) {
                    matched.append("dns=\(m.value)")
                }
            }
        }

        ctx.campus = !matched.isEmpty
        ctx.reasons = matched

        if let url = URL(string: portalUrl), let host = url.host {
            let port = url.port ?? (url.scheme == "https" ? 443 : 80)
            let reachable = isHostReachable(host: host, port: port, timeout: 1.5)
            ctx.portalReachable = reachable
            if reachable && !ctx.campus {
                ctx.campus = true
                ctx.reasons.append("portal_reachable")
            }
        }

        ctx.online = checkInternet(timeout: 2.0)
        return ctx
    }
}

private extension fd_set {
    mutating func zero() {
        self = fd_set()
    }
    mutating func set(_ fd: Int32) {
        let intOffset = Int(fd / 32)
        let bitOffset = fd % 32
        withUnsafeMutableBytes(of: &self) { raw in
            let ptr = raw.bindMemory(to: Int32.self)
            ptr[intOffset] |= (1 << bitOffset)
        }
    }
}
