package ir.unilink.android

// Pure logic with no Android or network dependencies, so it can be unit-tested on a plain JVM.

sealed class PortalResult {
    data class Success(val message: String) : PortalResult()
    /** The portal says the monthly data allowance is used up. */
    data class Quota(val message: String) : PortalResult()
    data class BadCredentials(val message: String) : PortalResult()
    /** Parsed portal reply that is negative but not recognised (not quota, not a password error). */
    data class Rejected(val message: String) : PortalResult()
    /** The server certificate is not pinned yet, or differs from the pinned one. No password was sent. */
    data class CertReview(val fingerprint: String, val subject: String, val changed: Boolean) : PortalResult()
    /** Transport problem, timeout, HTTP error or a non-portal reply. */
    data class Failure(val reason: String) : PortalResult()
}

/** Pure parsing/classification of Sophos / Cyberoam portal XML replies. No Android dependencies. */
object PortalParser {
    const val BUILTIN_QUOTA = "Your data transfer has been exceeded"
    private val BAD_CREDENTIALS = listOf(
        "could not log you on", "password is correct", "invalid user", "authentication failed"
    )

    fun tag(xml: String, name: String): String? {
        val re = Regex(
            "<$name>\\s*(?:<!\\[CDATA\\[(.*?)\\]\\]>|([^<]*))\\s*</$name>",
            setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL)
        )
        val m = re.find(xml) ?: return null
        return (m.groups[1]?.value ?: m.groups[2]?.value ?: "").trim()
    }

    /** Names (never values) of the XML elements in a reply, to learn what the portal reports. */
    fun fieldNames(xml: String): String =
        Regex("<([A-Za-z_][A-Za-z0-9_.:-]*)").findAll(xml)
            .map { it.groupValues[1] }.distinct().take(25).joinToString(", ")

    fun classify(xml: String, isLogin: Boolean, extraQuotaPhrases: List<String>): PortalResult {
        val status = tag(xml, "status").orEmpty()
        val ack = tag(xml, "ack").orEmpty()
        val message = tag(xml, "message").orEmpty()

        // A genuine positive status always wins over any message text.
        if (status.equals("LIVE", ignoreCase = true) || (!isLogin && ack.equals("ack", ignoreCase = true))) {
            return PortalResult.Success(message.ifBlank { "OK" })
        }
        if (status.isBlank() && ack.isBlank() && message.isBlank()) {
            return PortalResult.Failure("Unrecognized reply (not portal XML)")
        }
        val phrases = listOf(BUILTIN_QUOTA) + extraQuotaPhrases.filter { it.isNotBlank() }
        if (phrases.any { xml.contains(it, ignoreCase = true) }) {
            return PortalResult.Quota(message.ifBlank { BUILTIN_QUOTA })
        }
        if (isLogin && BAD_CREDENTIALS.any { xml.contains(it, ignoreCase = true) }) {
            return PortalResult.BadCredentials(message)
        }
        return PortalResult.Rejected(message.ifBlank { status.ifBlank { ack } })
    }
}

/** Pure HTML parsing of the Sophos user-portal home page. No Android dependencies. */
object UsageParser {
    private val ROW_IDS = listOf(
        "Language.CycleDataTrasfer", "Language.CycleDownloadedData",
        "Language.DataTrasfer", "Language.DownloadedData",
    )

    private fun clean(html: String): String =
        html.replace(Regex("<[^>]*>"), " ").replace("&nbsp;", " ").replace(Regex("\\s+"), " ").trim()

    /** "3159.02 MB" -> 3159.02 (MB). Returns null for "N.A." or anything without a number. */
    fun megabytes(text: String): Double? {
        val m = Regex("([0-9][0-9,]*(?:\\.[0-9]+)?)\\s*(KB|MB|GB|TB)?", RegexOption.IGNORE_CASE).find(text) ?: return null
        val n = m.groupValues[1].replace(",", "").toDoubleOrNull() ?: return null
        return when (m.groupValues[2].uppercase()) {
            "KB" -> n / 1024.0
            "GB" -> n * 1024.0
            "TB" -> n * 1024.0 * 1024.0
            else -> n
        }
    }

    private fun cells(html: String, labelId: String): List<String>? {
        // The raw server page uses single quotes (id='...'); the browser-saved copy uses double quotes.
        val at = Regex("id=[\"']" + Regex.escape(labelId) + "[\"']").find(html)?.range?.first ?: return null
        val start = html.lastIndexOf("<tr", at)
        val end = html.indexOf("</tr>", at)
        if (start < 0 || end < 0) return null
        return Regex("<td[^>]*>(.*?)</td>", setOf(RegexOption.DOT_MATCHES_ALL, RegexOption.IGNORE_CASE))
            .findAll(html.substring(start, end)).map { clean(it.groupValues[1]) }.toList()
    }

    /** Looks for the cycle (monthly) rows first, then the lifetime rows. */
    fun parse(html: String, now: Long): UsageInfo? {
        var best: UsageInfo? = null
        for (id in ROW_IDS) {
            val c = cells(html, id) ?: continue
            if (c.size < 6) continue
            val allotted = megabytes(c[1])
            val used = megabytes(c[4]) ?: continue
            val remaining = megabytes(c[5])
            val info = UsageInfo(allotted, used, remaining, renewal(html), now)
            if (allotted != null) return info // a real limit wins
            if (best == null) best = info      // otherwise keep the first unlimited reading
        }
        return best
    }

    fun renewal(html: String): String {
        val c = cells(html, "Language.DataTransferRenewal") ?: return ""
        return c.getOrNull(1)?.takeIf { Regex("^[0-9/.-]{6,12}$").matches(it) } ?: ""
    }

    fun looksLikeLogin(html: String) = html.contains("name=\"password\"") && html.contains("name=\"username\"")
}


object Versions {
    /** True when [latest] (like "v1.8.1") is a higher version than [current] (like "1.8.0"). */
    fun isNewer(latest: String, current: String): Boolean {
        fun parts(v: String) = v.removePrefix("v").split('.', '-', '+')
            .map { it.takeWhile(Char::isDigit).toIntOrNull() ?: 0 }
        val a = parts(latest)
        val b = parts(current)
        for (i in 0 until maxOf(a.size, b.size)) {
            val x = a.getOrElse(i) { 0 }
            val y = b.getOrElse(i) { 0 }
            if (x != y) return x > y
        }
        return false
    }
}
