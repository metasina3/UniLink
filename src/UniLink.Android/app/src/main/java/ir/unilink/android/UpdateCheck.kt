package ir.unilink.android

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONArray
import java.util.concurrent.TimeUnit

/**
 * Finds the newest stable Android release in the multi-platform repository.
 */
object UpdateCheck {
    const val REPO_URL = "https://github.com/metasina3/UniLink"
    const val OWNER = "Mrzzan05"
    private const val API = "https://api.github.com/repos/metasina3/UniLink/releases"
    private const val PAGE_SIZE = 100

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
            var page = 1
            var latest: String? = null
            do {
                val request = Request.Builder()
                    .url("$API?per_page=$PAGE_SIZE&page=$page")
                    .header("Accept", "application/vnd.github+json")
                    .header("User-Agent", "UniLink-Android/$current")
                    .build()
                val releases = client.newCall(request).execute().use { response ->
                    if (!response.isSuccessful) return@withContext Result.Failed("HTTP ${response.code}")
                    JSONArray(response.body?.string().orEmpty())
                }
                for (index in 0 until releases.length()) {
                    val release = releases.optJSONObject(index) ?: continue
                    if (release.optBoolean("draft") || release.optBoolean("prerelease")) continue
                    val version = Versions.androidReleaseVersion(release.optString("tag_name")) ?: continue
                    if (latest == null || isNewer(version, latest)) latest = version
                }
                page++
            } while (releases.length() == PAGE_SIZE)

            val version = latest ?: return@withContext Result.Failed("No Android release found")
            if (isNewer(version, current)) {
                Result.Available(version, "$REPO_URL/releases/tag/android-v$version")
            } else {
                Result.UpToDate(current)
            }
        } catch (e: Exception) {
            Result.Failed(e.message ?: e.javaClass.simpleName)
        }
    }

    fun isNewer(latest: String, current: String): Boolean = Versions.isNewer(latest, current)
}
