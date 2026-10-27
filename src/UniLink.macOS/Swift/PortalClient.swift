import Foundation

public final class PortalClient: NSObject {
    public let portalUrl: String
    public let interface: String

    public static let quotaPhrases = [
        "your data transfer has been exceeded",
        "data transfer limit",
        "data transfer quota",
        "maximum data transfer",
        "data usage limit",
        "internet usage limit",
        "quota exceeded",
        "quota exhausted",
        "traffic quota exhausted",
        "حجم شما به پایان",
        "حجم اینترنت شما تمام",
        "اتمام حجم",
        "سهمیه شما به پایان"
    ]

    public static let credentialPhrases = [
        "invalid username/password",
        "invalid username or password",
        "incorrect password",
        "wrong password",
        "invalid credentials",
        "نام کاربری یا رمز عبور اشتباه"
    ]

    private static let formAllowedChars: CharacterSet = {
        var set = CharacterSet.alphanumerics
        set.insert(charactersIn: "-_.~")
        return set
    }()

    public init(portalUrl: String, interface: String? = nil) {
        self.portalUrl = portalUrl.hasSuffix("/") ? portalUrl : portalUrl + "/"
        self.interface = interface ?? NetInfo.findPrimaryInterface()
        super.init()
    }

    private func timestamp() -> String {
        String(Int64(Date().timeIntervalSince1970 * 1000))
    }

    private func urlEncode(_ str: String) -> String {
        return str.addingPercentEncoding(withAllowedCharacters: Self.formAllowedChars) ?? str
    }

    public func curlRequest(
        path: String,
        method: String = "GET",
        data: [String: String]? = nil,
        timeout: Double = 6.0
    ) -> (status: Int, body: String, error: String) {
        let fullUrl = portalUrl + path
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/curl")

        var args = [
            "-k", "-s",
            "-m", String(timeout),
            "--interface", interface,
            "-A", "UniLink/1.3.1 (macOS; Swift Native)",
            "-w", "\n%{http_code}"
        ]

        if method == "POST" {
            args.append(contentsOf: ["-X", "POST"])
            if let data = data {
                let body = data.map { "\($0.key)=\(self.urlEncode($0.value))" }.joined(separator: "&")
                args.append(contentsOf: ["-d", body])
            }
        }

        args.append(fullUrl)
        process.arguments = args

        let stdoutPipe = Pipe()
        let stderrPipe = Pipe()
        process.standardOutput = stdoutPipe
        process.standardError = stderrPipe

        do {
            try process.run()
            process.waitUntilExit()
            let outData = stdoutPipe.fileHandleForReading.readDataToEndOfFile()
            let fullStr = String(data: outData, encoding: .utf8) ?? ""
            let errData = stderrPipe.fileHandleForReading.readDataToEndOfFile()
            let errStr = String(data: errData, encoding: .utf8) ?? ""

            var lines = fullStr.components(separatedBy: "\n")
            var statusCode = 0
            if let last = lines.popLast(), let code = Int(last.trimmingCharacters(in: .whitespaces)) {
                statusCode = code
            }
            let body = lines.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
            return (statusCode, body, errStr)
        } catch {
            return (0, "", error.localizedDescription)
        }
    }

    public func probe() -> (ok: Bool, isCyberoam: Bool, status: Int) {
        let res = curlRequest(path: "", method: "GET", timeout: 4.0)
        let isCyber = res.body.lowercased().contains("httpclient.js") ||
                      res.body.lowercased().contains("login.xml") ||
                      res.body.lowercased().contains("mode=191")
        return ((200..<400).contains(res.status), isCyber, res.status)
    }

    public func login(username: String, password: String) -> PortalReply {
        let cleanUser = username.replacingOccurrences(of: "'", with: "''")
        let fields: [String: String] = [
            "mode": "191",
            "username": cleanUser,
            "password": password,
            "a": timestamp(),
            "producttype": "0"
        ]

        let res = curlRequest(path: "login.xml", method: "POST", data: fields, timeout: 7.0)
        if res.status == 0 {
            return PortalReply(kind: .unknown, message: "عدم برقراری ارتباط با پورتال دانشگاه")
        }
        return parseReply(xmlText: res.body)
    }

    public func heartbeat(username: String) -> PortalReply {
        let ts = timestamp()
        let encodedUser = urlEncode(username)

        // 1. Cyberoam /live endpoint
        let path1 = "live?mode=192&username=\(encodedUser)&a=\(ts)&producttype=0"
        let res1 = curlRequest(path: path1, method: "GET", timeout: 5.0)
        if (200..<400).contains(res1.status) && !res1.body.isEmpty {
            return parseReply(xmlText: res1.body)
        }

        // 2. Fallback to /live.xml
        let path2 = "live.xml?mode=192&username=\(encodedUser)&a=\(ts)&producttype=0"
        let res2 = curlRequest(path: path2, method: "GET", timeout: 5.0)
        if (200..<400).contains(res2.status) && !res2.body.isEmpty {
            return parseReply(xmlText: res2.body)
        }

        return parseReply(xmlText: res1.body)
    }

    public func logout(username: String) -> PortalReply {
        let fields: [String: String] = [
            "mode": "193",
            "username": username,
            "a": timestamp(),
            "producttype": "0"
        ]
        let res = curlRequest(path: "logout.xml", method: "POST", data: fields, timeout: 5.0)
        if (200..<400).contains(res.status) && !res.body.isEmpty {
            return parseReply(xmlText: res.body)
        }
        return PortalReply(kind: .signedOut, message: "خروج از حساب انجام شد")
    }

    public func parseReply(xmlText: String) -> PortalReply {
        let cleaned = xmlText.trimmingCharacters(in: .whitespacesAndNewlines)
        var status = ""
        var message = ""

        // Extract status or ack
        if let sMatch = findXmlTag(tag: "status", in: cleaned) ?? findXmlTag(tag: "ack", in: cleaned) {
            status = sMatch
        }
        // Extract message or livemessage
        if let mMatch = findXmlTag(tag: "message", in: cleaned) ?? findXmlTag(tag: "livemessage", in: cleaned) {
            message = mMatch
        }

        let stUpper = status.uppercased()
        let msgLower = message.lowercased()

        // 1. Quota check
        for phrase in Self.quotaPhrases {
            if msgLower.contains(phrase) {
                return PortalReply(kind: .quota, message: message.isEmpty ? "حجم اینترنت به پایان رسیده است" : message, status: status, raw: cleaned)
            }
        }

        // 2. Credential error check
        for phrase in Self.credentialPhrases {
            if msgLower.contains(phrase) {
                return PortalReply(kind: .credentials, message: message.isEmpty ? "نام کاربری یا رمز عبور اشتباه است" : message, status: status, raw: cleaned)
            }
        }

        // 3. Online
        if stUpper == "LIVE" || stUpper == "ACK" {
            return PortalReply(kind: .online, message: message.isEmpty ? "متصل به اینترنت" : message, status: status, raw: cleaned)
        }

        // 4. Challenge
        if stUpper == "CHALLENGE" {
            return PortalReply(kind: .challenge, message: message.isEmpty ? "نیاز به تأیید دومرحله‌ای" : message, status: status, raw: cleaned)
        }

        // 5. Signed Out
        if stUpper == "LOGIN" || stUpper == "NACK" || stUpper == "LOGOUT" {
            return PortalReply(kind: .signedOut, message: message.isEmpty ? "خارج از حساب" : message, status: status, raw: cleaned)
        }

        return PortalReply(kind: .unknown, message: message.isEmpty ? (status.isEmpty ? "پاسخ نامشخص" : status) : message, status: status, raw: cleaned)
    }

    private func findXmlTag(tag: String, in text: String) -> String? {
        let pattern = "<\(tag)>(?:<!\\[CDATA\\[)?(.*?)(?:\\]\\]>)?</\(tag)>"
        guard let regex = try? NSRegularExpression(pattern: pattern, options: [.dotMatchesLineSeparators, .caseInsensitive]) else {
            return nil
        }
        let range = NSRange(text.startIndex..<text.endIndex, in: text)
        if let match = regex.firstMatch(in: text, range: range), let targetRange = Range(match.range(at: 1), in: text) {
            return String(text[targetRange]).trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return nil
    }
}
