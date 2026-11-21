package ir.unilink.android

import java.time.Instant
import java.time.YearMonth
import java.time.ZoneId
import java.util.UUID

/** One university account. The first account in a group's list is its default. */
data class Account(
    val id: String = UUID.randomUUID().toString(),
    val name: String,
    val username: String,
    val password: String,
    val enabled: Boolean = true,
    /** "yyyy-MM" (in the group's time zone) of the month the quota ran out, or null. */
    val exhaustedMonth: String? = null,
    val exhaustedAt: Long = 0L,
    val passwordRejected: Boolean = false,
    /** Last data-allowance reading from the user portal, or null if never fetched. */
    val usage: UsageInfo? = null,
)

/** Data allowance of one account for the current cycle (all sizes in MB). */
data class UsageInfo(
    /** Allowed MB per cycle, or null when the portal says it is unlimited. */
    val allottedMb: Double?,
    val usedMb: Double,
    val remainingMb: Double?,
    /** Renewal date exactly as the portal prints it (dd/MM/yyyy), or empty. */
    val renewal: String,
    val fetchedAt: Long,
) {
    /** 0..1 share of the allowance already used, or null when unlimited/unknown. */
    val fraction: Float?
        get() = allottedMb?.takeIf { it > 0 }?.let { (usedMb / it).coerceIn(0.0, 1.0).toFloat() }
}

data class NetworkGroup(
    val id: String = UUID.randomUUID().toString(),
    val name: String,
    val ssids: List<String>,
    val portalUrls: List<String>,
    /** Also match when the Wi-Fi name is unknown or different, relying on the pinned portal. */
    val portalOnly: Boolean = false,
    val keepaliveSec: Int = 180,
    val timeZone: String = DEFAULT_TZ,
    val quotaPhrases: List<String> = emptyList(),
    /** portal base URL -> SHA-256 fingerprint of the approved certificate. */
    val pins: Map<String, String> = emptyMap(),
    val accounts: List<Account> = emptyList(),
    /** Base URL of the portal that reports data usage (empty = feature off for this group). */
    val usageUrl: String = "",
) {
    companion object {
        const val DEFAULT_TZ = "Asia/Tehran"
        const val URMIA_USAGE_URL = "https://gate1.urmia.ac.ir:8450/"
    }

    fun zone(): ZoneId = runCatching { ZoneId.of(timeZone) }.getOrDefault(ZoneId.of(DEFAULT_TZ))
}

data class Settings(
    val autoSignIn: Boolean = true,
    val startOnBoot: Boolean = true,
    val holdWakeLock: Boolean = true,
    val holdWifiLock: Boolean = true,
    /** Quiet notification when a newer release exists (once per version, never when up to date). */
    val updateNotify: Boolean = true,
    val notifiedVersion: String = "",
    val lastUpdateCheck: Long = 0L,
    /** Try the account with the most data left first (needs usage readings), not the list order. */
    val preferMostData: Boolean = false,
    /** One notification when no account can sign in (out of data, disabled, wrong password). */
    val notifyProblems: Boolean = true,
    /** The first-run tour was finished or skipped. */
    val tourDone: Boolean = false,
    /** Choosing "Use this account" also jumps to the Connection tab and signs in with it at once. */
    val switchOnUse: Boolean = true,
)

data class AppData(
    val groups: List<NetworkGroup>,
    val settings: Settings = Settings(),
) {
    companion object {
        fun default() = AppData(
            groups = listOf(
                NetworkGroup(
                    name = "Urmia University",
                    ssids = listOf("UUWN"),
                    portalUrls = listOf("https://192.168.100.40:8090/"),
                    usageUrl = NetworkGroup.URMIA_USAGE_URL,
                )
            )
        )
    }
}

/** True while the account is excluded because its monthly quota ran out. */
fun Account.isExhaustedNow(group: NetworkGroup, now: Long): Boolean {
    val month = exhaustedMonth ?: return false
    val zone = group.zone()
    val current = YearMonth.from(Instant.ofEpochMilli(now).atZone(zone)).toString()
    if (month != current) return false // new month: limit lifted automatically
    // Day-one probe: if it was marked on the 1st and it is still the 1st, allow an hourly re-check
    // (the portal may not have renewed the quota yet).
    val markedDay = Instant.ofEpochMilli(exhaustedAt).atZone(zone).dayOfMonth
    val nowDay = Instant.ofEpochMilli(now).atZone(zone).dayOfMonth
    return !(markedDay == 1 && nowDay == 1 && now - exhaustedAt >= 3_600_000L)
}

fun Account.isUsable(group: NetworkGroup, now: Long): Boolean =
    enabled && !passwordRejected && !isExhaustedNow(group, now)

fun <T> List<T>.moved(from: Int, to: Int): List<T> {
    if (from !in indices || to !in indices || from == to) return this
    val m = toMutableList()
    val item = m.removeAt(from)
    m.add(to, item)
    return m
}

/** "1.5 GB" / "840 MB" (always Latin digits so it lines up in both languages). */
fun formatMb(mb: Double): String =
    if (mb >= 1024.0) String.format(java.util.Locale.US, "%.1f GB", mb / 1024.0)
    else String.format(java.util.Locale.US, "%.0f MB", mb)

/** Decides which accounts to try first when signing in. Pure, so it can be unit-tested. */
object LoginOrder {
    /** Below this many MB left an account is tried last. */
    const val LOW_MB = 200.0
    /** Older readings are ignored: the monthly quota may have renewed since. */
    const val FRESH_MS = 10L * 24 * 3_600_000

    private fun remaining(a: Account, now: Long): Double? {
        val u = a.usage ?: return null
        if (now - u.fetchedAt > FRESH_MS) return null
        return u.remainingMb ?: Double.POSITIVE_INFINITY // unlimited
    }

    /**
     * Accounts that are known to be nearly empty always go last. With [preferMostData], the rest are
     * ordered by data left (unknown readings keep their place among the known ones); otherwise the
     * list order chosen by the user is kept.
     */
    fun order(accounts: List<Account>, now: Long, preferMostData: Boolean): List<Account> {
        val indexed = accounts.mapIndexed { i, a -> Triple(i, a, remaining(a, now)) }
        val (low, ok) = indexed.partition { (_, _, r) -> r != null && r < LOW_MB }
        val head = if (preferMostData)
            ok.sortedWith(compareByDescending<Triple<Int, Account, Double?>> { it.third ?: (Double.MAX_VALUE / 2) }.thenBy { it.first })
        else ok
        return (head + low).map { it.second }
    }
}
