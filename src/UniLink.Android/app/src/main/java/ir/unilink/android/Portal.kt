package ir.unilink.android

import android.net.Network
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.ConnectionPool
import okhttp3.ConnectionSpec
import okhttp3.Dns
import okhttp3.FormBody
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.IOException
import java.net.Inet4Address
import java.net.InetAddress
import java.net.Proxy
import java.net.UnknownHostException
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager

internal class Capture {
    @Volatile var fingerprint: String? = null
    @Volatile var subject: String? = null
}

/** Accepts only the certificate whose SHA-256 fingerprint was approved by the user. */
internal class PinTrust(private val pin: String?, private val cap: Capture) : X509TrustManager {
    override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {
        throw CertificateException("Client certificates are not supported")
    }

    override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
        val leaf = chain?.firstOrNull() ?: throw CertificateException("Empty certificate chain")
        val fp = sha256Hex(leaf.encoded)
        cap.fingerprint = fp
        cap.subject = leaf.subjectX500Principal.name
        if (pin == null || !fp.equals(pin, ignoreCase = true)) {
            throw CertificateException("Certificate is not pinned")
        }
    }

    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
}

fun sha256Hex(bytes: ByteArray): String =
    MessageDigest.getInstance("SHA-256").digest(bytes).joinToString(":") { "%02X".format(it) }

/**
 * Talks to the portal strictly over the given Wi-Fi [Network]: sockets and DNS are bound to it,
 * the system proxy is ignored, redirects are refused and the certificate must match the pin.
 */
class PortalClient(
    private val network: Network,
    private val pin: String?,
    /** Probe mode: short timeouts for a quick "is anything answering?" question. */
    private val fast: Boolean = false,
) {
    /** Element names of the last reply (diagnostics only; no values). */
    @Volatile var replyFields: String? = null
        private set

    companion object {
        const val VPN_BLOCKED =
            "VPN blocks UniLink from reaching the Wi-Fi (EPERM). In your VPN app, exclude UniLink from the tunnel or allow the local network."
    }

    private fun buildClient(cap: Capture, bound: Boolean): OkHttpClient {
        val trust = PinTrust(pin, cap)
        val ssl = SSLContext.getInstance("TLS").apply {
            init(null, arrayOf<TrustManager>(trust), SecureRandom())
        }
        val dns = object : Dns {
            override fun lookup(hostname: String): List<InetAddress> {
                val v4 = network.getAllByName(hostname).filterIsInstance<Inet4Address>()
                if (v4.isEmpty()) throw UnknownHostException(hostname)
                return v4
            }
        }
        val builder = OkHttpClient.Builder()
        if (bound) builder.socketFactory(network.socketFactory).dns(dns)
        return builder
            .proxy(Proxy.NO_PROXY)
            .followRedirects(false)
            .followSslRedirects(false)
            .retryOnConnectionFailure(false)
            .connectionPool(ConnectionPool(0, 1, TimeUnit.NANOSECONDS))
            .connectionSpecs(listOf(ConnectionSpec.COMPATIBLE_TLS))
            .sslSocketFactory(ssl.socketFactory, trust)
            .hostnameVerifier { _, _ -> true } // identity is established by the pinned fingerprint
            .connectTimeout(if (fast) 3 else 6, TimeUnit.SECONDS)
            .readTimeout(if (fast) 4 else 15, TimeUnit.SECONDS)
            .callTimeout(if (fast) 8 else 25, TimeUnit.SECONDS)
            .build()
    }

    /**
     * Normally every request is bound to the Wi-Fi network. Some VPN setups make Android refuse that
     * (EPERM); then we try once over the default route, which works when the VPN lets local
     * addresses through. This is safe: the portal certificate is pinned and the TLS handshake
     * happens before any password is sent, so a wrong host can never receive credentials.
     */
    private fun execute(request: Request, isLogin: Boolean, extra: List<String>): PortalResult {
        val first = runOnce(request, isLogin, extra, bound = true)
        if (first is PortalResult.Failure && first.reason.contains("EPERM")) {
            return when (val second = runOnce(request, isLogin, extra, bound = false)) {
                is PortalResult.Failure, is PortalResult.CertReview -> PortalResult.Failure(VPN_BLOCKED)
                else -> second
            }
        }
        return first
    }

    private fun runOnce(request: Request, isLogin: Boolean, extra: List<String>, bound: Boolean): PortalResult {
        val cap = Capture()
        return try {
            buildClient(cap, bound).newCall(request).execute().use { resp ->
                if (resp.isRedirect) return PortalResult.Failure("Redirect rejected by policy")
                val body = resp.peekBody(65536).string()
                replyFields = PortalParser.fieldNames(body)
                val result = PortalParser.classify(body, isLogin, extra)
                if (result is PortalResult.Failure && !resp.isSuccessful) {
                    PortalResult.Failure("HTTP ${resp.code}")
                } else result
            }
        } catch (e: IOException) {
            val fp = cap.fingerprint
            if (fp != null && !fp.equals(pin, ignoreCase = true)) {
                PortalResult.CertReview(fp, cap.subject.orEmpty(), changed = pin != null)
            } else {
                PortalResult.Failure(e.message ?: e.javaClass.simpleName)
            }
        } catch (e: Exception) {
            PortalResult.Failure(e.message ?: e.javaClass.simpleName)
        }
    }

    private fun endpoint(base: String, path: String) =
        (base.trim().trimEnd('/') + "/" + path).toHttpUrlOrNull()

    suspend fun login(base: String, username: String, password: String, extra: List<String>): PortalResult =
        withContext(Dispatchers.IO) {
            val url = endpoint(base, "login.xml") ?: return@withContext PortalResult.Failure("Invalid portal URL")
            val body = FormBody.Builder()
                .add("mode", "191")
                .add("username", username)
                .add("password", password)
                .add("a", System.currentTimeMillis().toString())
                .add("producttype", "0")
                .build()
            execute(Request.Builder().url(url).post(body).build(), isLogin = true, extra = extra)
        }

    suspend fun keepalive(base: String, username: String, extra: List<String>): PortalResult =
        withContext(Dispatchers.IO) {
            val url = endpoint(base, "live")?.newBuilder()
                ?.addQueryParameter("mode", "192")
                ?.addQueryParameter("username", username)
                ?.addQueryParameter("a", System.currentTimeMillis().toString())
                ?.addQueryParameter("producttype", "0")
                ?.build()
                ?: return@withContext PortalResult.Failure("Invalid portal URL")
            execute(Request.Builder().url(url).get().build(), isLogin = false, extra = extra)
        }

    suspend fun logout(base: String, username: String): Boolean = withContext(Dispatchers.IO) {
        val url = endpoint(base, "logout.xml") ?: return@withContext false
        val body = FormBody.Builder()
            .add("mode", "193")
            .add("username", username)
            .add("a", System.currentTimeMillis().toString())
            .add("producttype", "0")
            .build()
        execute(Request.Builder().url(url).post(body).build(), isLogin = false, extra = emptyList()) !is PortalResult.Failure
    }
}
