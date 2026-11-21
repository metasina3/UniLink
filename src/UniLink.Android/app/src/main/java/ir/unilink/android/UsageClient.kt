package ir.unilink.android

import android.net.Network
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.Cookie
import okhttp3.CookieJar
import okhttp3.ConnectionPool
import okhttp3.ConnectionSpec
import okhttp3.Dns
import okhttp3.FormBody
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONObject
import java.io.IOException
import java.net.Inet4Address
import java.net.InetAddress
import java.net.Proxy
import java.net.UnknownHostException
import java.security.SecureRandom
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager

sealed class UsageResult {
    data class Ok(val info: UsageInfo) : UsageResult()
    data class BadCredentials(val message: String) : UsageResult()
    data class CertReview(val fingerprint: String, val subject: String, val changed: Boolean) : UsageResult()
    data class Failure(val reason: String) : UsageResult()
}

/**
 * Reads the data allowance from the Sophos user portal with the account's own user name and password.
 * Same safety rules as [PortalClient]: pinned certificate (checked before anything is sent), proxy
 * ignored, redirects refused, and credentials only ever go to the configured host.
 */
class UsageClient(private val network: Network?, private val pin: String?) {
    private val jar = object : CookieJar {
        private val store = mutableListOf<Cookie>()
        override fun saveFromResponse(url: HttpUrl, cookies: List<Cookie>) {
            cookies.forEach { c -> store.removeAll { it.name == c.name } ; store.add(c) }
        }
        override fun loadForRequest(url: HttpUrl): List<Cookie> = store.toList()
    }

    private fun client(cap: Capture, bound: Boolean): OkHttpClient {
        val trust = PinTrust(pin, cap)
        val ssl = SSLContext.getInstance("TLS").apply { init(null, arrayOf<TrustManager>(trust), SecureRandom()) }
        val b = OkHttpClient.Builder()
        if (bound && network != null) {
            b.socketFactory(network.socketFactory).dns(object : Dns {
                override fun lookup(hostname: String): List<InetAddress> {
                    val v4 = network.getAllByName(hostname).filterIsInstance<Inet4Address>()
                    if (v4.isEmpty()) throw UnknownHostException(hostname)
                    return v4
                }
            })
        }
        return b.proxy(Proxy.NO_PROXY)
            .cookieJar(jar)
            .followRedirects(false).followSslRedirects(false)
            .retryOnConnectionFailure(false)
            .connectionPool(ConnectionPool(0, 1, TimeUnit.NANOSECONDS))
            .connectionSpecs(listOf(ConnectionSpec.COMPATIBLE_TLS))
            .sslSocketFactory(ssl.socketFactory, trust)
            .hostnameVerifier { _, _ -> true } // identity is the pinned fingerprint
            .connectTimeout(8, TimeUnit.SECONDS).readTimeout(15, TimeUnit.SECONDS).callTimeout(40, TimeUnit.SECONDS)
            .build()
    }

    suspend fun fetch(base: String, username: String, password: String): UsageResult =
        withContext(Dispatchers.IO) {
            var first = run(base, username, password, bound = true)
            if (first is UsageResult.Failure && first.reason.contains("resolve host")) {
                kotlinx.coroutines.delay(1_500) // DNS right after Wi-Fi/portal sign-in can lag a moment
                first = run(base, username, password, bound = true)
            }
            if (first is UsageResult.Failure && first.reason.contains("EPERM") && network != null) {
                return@withContext when (val second = run(base, username, password, bound = false)) {
                    is UsageResult.Failure, is UsageResult.CertReview -> UsageResult.Failure(PortalClient.VPN_BLOCKED)
                    else -> second
                }
            }
            first
        }

    private fun snippet(text: String?): String =
        (text ?: "-").replace(Regex("[A-Za-z0-9+/=_-]{20,}"), "…").replace(Regex("\\s+"), " ").trim().take(150)

    private companion object {
        const val UA = "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36"
    }

    private fun run(base: String, username: String, password: String, bound: Boolean): UsageResult {
        val root = base.trim().trimEnd('/').toHttpUrlOrNull() ?: return UsageResult.Failure("Invalid portal URL")
        if (!root.isHttps) return UsageResult.Failure("The usage portal must use https")
        val cap = Capture()
        val c = client(cap, bound)
        // path may carry a query string ("?popup=0&t=..."), so build from text, not encodedPath
        fun url(path: String): HttpUrl = (root.toString().trimEnd('/') + path).toHttpUrlOrNull()
            ?: throw IOException("Invalid usage URL")
        var loggedIn = false
        return try {
            val tokens = CsrfFinder()
            fun getText(path: String, headers: Map<String, String> = emptyMap(), limit: Long = 2_000_000): String? {
                val rb = Request.Builder().url(url(path)).get().header("User-Agent", UA).header("Accept", "*/*")
                headers.forEach { (k, v) -> rb.header(k, v) }
                return c.newCall(rb.build()).execute().use { r ->
                    tokens.fromHeaders(r.headers)
                    if (r.isRedirect) return@use null
                    r.body?.source()?.let { src -> src.request(limit); src.buffer.clone().readUtf8() }
                        ?.also { tokens.fromBody(it) }
                }
            }

            // 1) Session cookie (no credentials involved).
            getText("/userportal/webpages/myaccount/login.jsp")

            // 2) Log in.
            val json = JSONObject().put("username", username).put("password", password).put("languageid", "1")
            val form = FormBody.Builder()
                .add("mode", "451").add("json", json.toString())
                .add("__RequestType", "ajax").add("t", System.currentTimeMillis().toString()).build()
            val loginBody = c.newCall(
                Request.Builder().url(url("/userportal/Controller")).post(form)
                    .header("User-Agent", UA).header("Accept", "text/plain, */*; q=0.01")
                    .header("Origin", root.toString().trimEnd('/'))
                    .header("X-Requested-With", "XMLHttpRequest")
                    .header("Referer", url("/").toString()).build()
            ).execute().use { r ->
                tokens.fromHeaders(r.headers)
                if (r.isRedirect) return UsageResult.Failure("Redirect rejected by policy")
                if (!r.isSuccessful) return UsageResult.Failure("HTTP ${r.code}")
                r.peekBody(65536).string()
            }
            val reply = runCatching { JSONObject(loginBody) }.getOrNull()
                ?: return UsageResult.Failure("Unrecognized login reply")
            val status = reply.optInt("status", -1)
            if (status != 200) {
                val msg = reply.optString("message", "").take(160)
                return UsageResult.BadCredentials(msg.ifBlank { "status $status" })
            }
            loggedIn = true
            tokens.fromJson(reply)

            // 3) The home page is only a shell; it carries the anti-forgery token.
            val home = getText("/userportal/webpages/myaccount/index.jsp")
                ?: return UsageResult.Failure("Portal did not keep the session")
            UsageParser.parse(home, System.currentTimeMillis())?.let { return UsageResult.Ok(it) }

            // 4) The usage tables are loaded by the page's script from AccountStatus.jsp.
            val ajax = buildMap<String, String> {
                put("X-Requested-With", "XMLHttpRequest")
                put("Referer", url("/userportal/webpages/myaccount/index.jsp").toString())
                tokens.value?.let { put("X-CSRF-Token", it) }
            }
            val frag = getText(
                "/userportal/webpages/myaccount/AccountStatus.jsp?popup=0&t=${System.currentTimeMillis()}", ajax
            )
            val info = frag?.let { UsageParser.parse(it, System.currentTimeMillis()) }
            when {
                info != null -> UsageResult.Ok(info)
                frag != null && UsageParser.looksLikeLogin(frag) -> UsageResult.Failure("Portal asked to sign in again")
                else -> UsageResult.Failure(
                    "Usage table not found (home ${home.length} B, page ${frag?.length ?: -1} B, token ${tokens.describe()}, reply \"${snippet(frag)}\")"
                )
            }
        } catch (e: IOException) {
            val fp = cap.fingerprint
            if (fp != null && !fp.equals(pin, ignoreCase = true))
                UsageResult.CertReview(fp, cap.subject.orEmpty(), changed = pin != null)
            else UsageResult.Failure(e.message ?: e.javaClass.simpleName)
        } catch (e: Exception) {
            UsageResult.Failure(e.message ?: e.javaClass.simpleName)
        } finally {
            if (loggedIn) runCatching {
                c.newCall(Request.Builder().url(url("/userportal/webpages/logout.jsp").newBuilder()
                    .addQueryParameter("webclient", "myaccount").build())
                    .get().build()).execute().use { it.body?.close() }
            }
        }
    }
}

/** Finds the anti-forgery token the portal page hands to its own scripts (value never logged). */
internal class CsrfFinder {
    var value: String? = null
        private set
    private var source = "none"

    private fun take(v: String?, from: String) {
        if (value == null && !v.isNullOrBlank()) { value = v; source = from }
    }

    fun fromHeaders(h: okhttp3.Headers) {
        take(h["X-CSRF-Token"] ?: h["X-XSRF-Token"] ?: h["CSRF-Token"], "header")
    }

    fun fromJson(o: JSONObject) {
        for (k in o.keys()) if (k.contains("csrf", true) || k.contains("xsrf", true)) take(o.optString(k), "login-json")
    }

    fun fromBody(html: String) {
        val patterns = listOf(
            // Sophos: Cyberoam.c$rFt0k3n = '...';  ([$] avoids Kotlin string templates)
            Regex("""c[$]rFt0k3n\s*=\s*["']([A-Za-z0-9\-_+/=.]{8,})["']"""),
            Regex("""<meta[^>]+name=["']?_?csrf[-_a-z]*["']?[^>]*content=["']([^"']{8,})["']""", RegexOption.IGNORE_CASE),
            Regex("""X-CSRF-Token["']?\s*[,:=]\s*["']([A-Za-z0-9\-_+/=.]{8,})["']""", RegexOption.IGNORE_CASE),
            Regex("""csrf[A-Za-z_\-]*["']?\s*[:=]\s*["']([A-Za-z0-9\-_+/=.]{8,})["']""", RegexOption.IGNORE_CASE),
            Regex("""name=["']?csrf[A-Za-z_\-]*["']?[^>]*value=["']([^"']{8,})["']""", RegexOption.IGNORE_CASE),
        )
        for ((i, re) in patterns.withIndex()) {
            re.find(html)?.let { take(it.groupValues[1], "page#" + i); return }
        }
    }

    fun describe() = if (value == null) "none" else "found($source)"

    companion object {
        /** Names (never values) in the page that look token-related, to help locate it. */
        fun hints(html: String): String =
            Regex("""[A-Za-z_\-]*(?:csrf|xsrf|token)[A-Za-z_\-]*""", RegexOption.IGNORE_CASE)
                .findAll(html).map { it.value }.distinct().take(6).joinToString(",").ifEmpty { "-" }
    }
}
