import Foundation

public final class PortalClient: NSObject, URLSessionDelegate {
    public let portalUrl: String
    private lazy var session: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 10.0
        config.timeoutIntervalForResource = 15.0
        config.httpShouldSetCookies = true
        config.httpCookieAcceptPolicy = .always
        return URLSession(configuration: config, delegate: self, delegateQueue: nil)
    }()

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

    public init(portalUrl: String) {
        self.portalUrl = portalUrl.hasSuffix("/") ? portalUrl : portalUrl + "/"
        super.init()
    }

    public func urlSession(
        _ session: URLSession,
        didReceive challenge: URLAuthenticationChallenge,
        completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void
    ) {
        // Automatically trust university campus self-signed certificates
        if challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
           let trust = challenge.protectionSpace.serverTrust {
            completionHandler(.useCredential, URLCredential(trust: trust))
        } else {
            completionHandler(.performDefaultHandling, nil)
        }
    }

    private func timestamp() -> String {
        String(Int64(Date().timeIntervalSince1970 * 1000))
    }

    public func probe() -> (ok: Bool, isCyberoam: Bool, status: Int) {
        guard let url = URL(string: portalUrl) else { return (false, false, 0) }
        var req = URLRequest(url: url)
        req.timeoutInterval = 5.0
        let sema = DispatchSemaphore(value: 0)
        var result = (ok: false, isCyberoam: false, status: 0)

        session.dataTask(with: req) { data, response, _ in
            defer { sema.signal() }
            if let http = response as? HTTPURLResponse {
                result.status = http.statusCode
                result.ok = (200..<400).contains(http.statusCode)
                if let data = data, let text = String(data: data, encoding: .utf8)?.lowercased() {
                    result.isCyberoam = text.contains("httpclient.js") || text.contains("login.xml") || text.contains("mode=191")
                }
            }
        }.resume()

        _ = sema.wait(timeout: .now() + 6.0)
        return result
    }

    public func login(username: String, password: String) -> PortalReply {
        guard let url = URL(string: portalUrl + "login.xml") else {
            return PortalReply(kind: .unknown, message: "آدرس پورتال نامعتبر است")
        }

        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        req.setValue("UniLink/1.2.1 (macOS; Swift Native)", forHTTPHeaderField: "User-Agent")

        let cleanUser = username.replacingOccurrences(of: "'", with: "''")
        let fields: [String: String] = [
            "mode": "191",
            "username": cleanUser,
            "password": password,
            "a": timestamp(),
            "producttype": "0"
        ]

        let bodyString = fields.map { "\($0.key)=\($0.value.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? $0.value)" }.joined(separator: "&")
        req.httpBody = bodyString.data(using: .utf8)

        let sema = DispatchSemaphore(value: 0)
        var reply = PortalReply(kind: .unknown, message: "پاسخی از سرور دریافت نشد")

        session.dataTask(with: req) { data, _, error in
            defer { sema.signal() }
            if let error = error {
                reply = PortalReply(kind: .unknown, message: "خطای ارتباط: \(error.localizedDescription)")
                return
            }
            guard let data = data, let text = String(data: data, encoding: .utf8) else {
                reply = PortalReply(kind: .unknown, message: "پاسخ خالی")
                return
            }
            reply = self.parseReply(xmlText: text)
        }.resume()

        _ = sema.wait(timeout: .now() + 10.0)
        return reply
    }

    public func heartbeat(username: String) -> PortalReply {
        let ts = timestamp()
        let encodedUser = username.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? username
        let urlStr = portalUrl + "live.xml?mode=192&username=\(encodedUser)&a=\(ts)&producttype=0"
        guard let url = URL(string: urlStr) else {
            return PortalReply(kind: .unknown, message: "آدرس نامعتبر است")
        }

        var req = URLRequest(url: url)
        req.setValue("UniLink/1.2.1 (macOS; Swift Native)", forHTTPHeaderField: "User-Agent")

        let sema = DispatchSemaphore(value: 0)
        var reply = PortalReply(kind: .unknown, message: "بررسی نشست انجام نشد")

        session.dataTask(with: req) { data, _, _ in
            defer { sema.signal() }
            if let data = data, let text = String(data: data, encoding: .utf8) {
                reply = self.parseReply(xmlText: text)
            }
        }.resume()

        _ = sema.wait(timeout: .now() + 8.0)
        return reply
    }

    public func logout(username: String) -> PortalReply {
        guard let url = URL(string: portalUrl + "logout.xml") else {
            return PortalReply(kind: .unknown, message: "آدرس نامعتبر است")
        }

        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")

        let fields: [String: String] = [
            "mode": "193",
            "username": username,
            "a": timestamp(),
            "producttype": "0"
        ]
        let bodyString = fields.map { "\($0.key)=\($0.value.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? $0.value)" }.joined(separator: "&")
        req.httpBody = bodyString.data(using: .utf8)

        let sema = DispatchSemaphore(value: 0)
        var reply = PortalReply(kind: .signedOut, message: "خروج انجام شد")

        session.dataTask(with: req) { data, _, _ in
            defer { sema.signal() }
            if let data = data, let text = String(data: data, encoding: .utf8) {
                reply = self.parseReply(xmlText: text)
            }
        }.resume()

        _ = sema.wait(timeout: .now() + 6.0)
        return reply
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

        return PortalReply(kind: .unknown, message: message.isEmpty ? status : message, status: status, raw: cleaned)
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
