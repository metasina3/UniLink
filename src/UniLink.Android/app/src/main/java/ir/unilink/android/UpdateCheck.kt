package ir.unilink.android

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONObject
import java.util.concurrent.TimeUnit

/**
 * Manual "Check for update": asks GitHub for the latest release of this project.
 * It only runs when the user taps the button, sends no account data, and never installs anything;
 * it just reports the newest version and the release page to open.
 */
object UpdateCheck {
    const val REPO_URL = "https://github.com/Mrzzan05/UniLink-Android"
    const val OWNER = "Mrzzan05"
    private const val API = "https://api.github.com/repos/Mrzzan05/UniLink-Android/releases/latest"

    sealed interface Result {
        data class UpToDate(val current: String) : Result
        data class Available(val latest: String, val url: String) : Result
        data class Failed(val reason: String) : Result
    }

    private val client by lazy {
        OkHttpClient.Builder()
            .connectTimeout(10, TimeUnit.SECONDS)
            .readTimeout(10, TimeUnit.SECONDS)
            .callTimeout(20, TimeUnit.SECONDS)
            .build()
    }

    suspend fun check(current: String): Result = withContext(Dispatchers.IO) {
        try {
            val req = Request.Builder()
                .url(API)
                .header("Accept", "application/vnd.github+json")
                .header("User-Agent", "UniLink-Android/$current")
                .build()
            client.newCall(req).execute().use { resp ->
                if (!resp.isSuccessful) return@withContext Result.Failed("HTTP ${resp.code}")
                val json = JSONObject(resp.body?.string().orEmpty())
                val tag = json.optString("tag_name").trim()
                if (tag.isEmpty()) return@withContext Result.Failed("No release found")
                val url = json.optString("html_url").ifBlank { "$REPO_URL/releases" }
                // Only ever open a page on the project's own GitHub repository.
                val safeUrl = if (url.startsWith("$REPO_URL/")) url else "$REPO_URL/releases"
                if (isNewer(tag, current)) Result.Available(tag.removePrefix("v"), safeUrl)
                else Result.UpToDate(current)
            }
        } catch (e: Exception) {
            Result.Failed(e.message ?: e.javaClass.simpleName)
        }
    }

    fun isNewer(latest: String, current: String): Boolean = Versions.isNewer(latest, current)
}
