package ir.unilink.android

import android.content.Context
import androidx.annotation.StringRes
import android.net.Network
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import java.text.SimpleDateFormat
import java.time.Instant
import java.time.YearMonth
import java.util.Date
import java.util.Locale

/** Keepalive health since the service started: used to judge battery settings with real numbers. */
data class KeepaliveStats(
    val ok: Int = 0,
    val failed: Int = 0,
    val late: Int = 0,
    val maxLateSec: Long = 0L,
    /** Wi-Fi/VPN callbacks received: helps tell whether network chatter wakes the CPU. */
    val netEvents: Int = 0,
)

enum class Phase { Stopped, Paused, WaitingWifi, NeedCertificate, Connecting, Connected, WaitingRetry, AccountNeeded }

data class CertPrompt(
    val groupId: String,
    val groupName: String,
    val url: String,
    val fingerprint: String,
    val subject: String,
    val changed: Boolean,
)

data class EngineStatus(
    val phase: Phase = Phase.Stopped,
    val title: String = "Stopped",
    val detail: String = "",
    val groupName: String? = null,
    val accountId: String? = null,
    val accountName: String? = null,
    val lastKeepalive: Long = 0L,
    val nextCheck: Long = 0L,
    val pendingCert: CertPrompt? = null,
)

/**
 * Sign-in / keepalive state machine. Accounts are tried strictly one at a time, in list order.
 * Only a recognised quota message marks an account exhausted; network errors, timeouts and unknown
 * replies are retried with back-off and never burn an account.
 */
class Engine(private val ctx: Context, private val store: Store) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val wake = Channel<Unit>(Channel.CONFLATED)
    private val opLock = Mutex()

    private val _status = MutableStateFlow(EngineStatus(title = str(R.string.st_stopped)))
    val status: StateFlow<EngineStatus> = _status.asStateFlow()

    private val _stats = MutableStateFlow(KeepaliveStats())
    val stats: StateFlow<KeepaliveStats> = _stats.asStateFlow()

    private val _log = MutableStateFlow<List<String>>(emptyList())
    val logLines: StateFlow<List<String>> = _log.asStateFlow()

    // ---- data usage (user portal) ----
    private val usageLock = Mutex()
    private val _usageBusy = MutableStateFlow<Set<String>>(emptySet())
    /** Account ids whose usage is being fetched right now. */
    val usageBusy: StateFlow<Set<String>> = _usageBusy.asStateFlow()
    private val _usageErrors = MutableStateFlow<Map<String, String>>(emptyMap())
    /** Account id -> last fetch error (cleared on success). */
    val usageErrors: StateFlow<Map<String, String>> = _usageErrors.asStateFlow()
    private val _usageCert = MutableStateFlow<CertPrompt?>(null)
    val usageCert: StateFlow<CertPrompt?> = _usageCert.asStateFlow()

    private var noAccountsNotified = false

    private var job: Job? = null
    private var tracker: WifiTracker? = null

    @Volatile private var paused = false
    @Volatile private var userRequested = false
    /** Set by Connect: skip the "is a session already live?" probe and sign in fresh. */
    @Volatile private var forceFresh = false
    /** Set by "Use this account": the next sign-in tries only this account and reports why it failed. */
    @Volatile private var forcedAccountId: String? = null
    @Volatile private var retryAt = 0L
    private var lastSignature = ""
    private var lastMatchNote = ""
    private var failures = 0
    private var nextKeepalive = 0L
    private var lastKeepalive = 0L
    private var session: Session? = null

    private class Session(
        val groupId: String,
        val accountId: String,
        val accountName: String,
        val username: String,
        val url: String,
        val netHandle: Long,
    )

    private sealed class Outcome {
        class LoggedIn(val account: Account, val url: String, val adopted: Boolean = false) : Outcome()
        class NeedCert(val prompt: CertPrompt) : Outcome()
        object NoAccounts : Outcome()
        class Retry(val reason: String, val minSeconds: Int) : Outcome()
        /** The account the user picked cannot be used; shown on the Connection tab instead of silently using another. */
        class Problem(val title: String, val detail: String) : Outcome()
    }

    val isRunning: Boolean get() = job?.isActive == true

    // ---------- public controls ----------

    @Synchronized
    fun start() {
        if (job?.isActive == true) return
        val t = WifiTracker(ctx) { onNetworkEvent() }
        tracker = t
        paused = false
        _stats.value = KeepaliveStats()
        t.start()
        job = scope.launch { loop(t) }
        log("Service started")
    }

    @Synchronized
    fun stop() {
        job?.cancel()
        job = null
        tracker?.stop()
        tracker = null
        session = null
        _status.value = EngineStatus(title = str(R.string.st_stopped))
        log("Service stopped")
    }

    /** User pressed Connect: resume if paused, skip any back-off and try right away. */
    fun connectNow() {
        paused = false
        userRequested = true
        forceFresh = true
        failures = 0
        retryAt = 0L
        // Show the effect of the tap at once; the loop may still be finishing an earlier attempt.
        _status.update { it.copy(phase = Phase.Connecting, title = str(R.string.st_signing_in), detail = it.groupName ?: "", pendingCert = null) }
        UniLinkWidget.refresh(ctx)
        wake.trySend(Unit)
    }

    /**
     * The user picked an account: make it the default, sign out of the one in use and sign in with
     * this one right away. If it cannot be used (wrong password, no data) the Connection tab says so.
     */
    fun useAccount(groupId: String, accountId: String) {
        store.updateGroup(groupId) { g ->
            g.copy(accounts = g.accounts.moved(g.accounts.indexOfFirst { it.id == accountId }, 0))
        }
        val g = store.data.value.groups.firstOrNull { it.id == groupId } ?: return
        val acc = g.accounts.firstOrNull { it.id == accountId } ?: return
        if (session?.accountId == accountId) return // already signed in with it
        paused = false
        userRequested = true
        forceFresh = true
        forcedAccountId = accountId
        failures = 0
        retryAt = 0L
        _status.update { it.copy(phase = Phase.Connecting, title = str(R.string.st_signing_in), detail = acc.name, pendingCert = null) }
        scope.launch {
            val s = session
            session = null
            val net = tracker?.network
            val sg = s?.let { x -> store.data.value.groups.firstOrNull { it.id == x.groupId } }
            if (s != null && net != null && sg != null) {
                val ok = opLock.withLock { runCatching { PortalClient(net, sg.pins[s.url]).logout(s.url, s.username) }.getOrDefault(false) }
                log("Switching to \"${acc.name}\": " + if (ok) "signed out \"${s.accountName}\"" else "sign-out of \"${s.accountName}\" failed")
            } else log("Switching to \"${acc.name}\"")
            wake.trySend(Unit)
        }
    }

    fun kick() {
        retryAt = 0L
        wake.trySend(Unit)
    }

    fun pauseAndSignOut() {
        paused = true
        userRequested = false
        scope.launch {
            val s = session
            session = null
            val net = tracker?.network
            val g = s?.let { x -> store.data.value.groups.firstOrNull { it.id == x.groupId } }
            if (s != null && net != null && g != null) {
                val ok = opLock.withLock { PortalClient(net, g.pins[s.url]).logout(s.url, s.username) }
                log(if (ok) "Signed out" else "Sign-out request failed")
            }
            setStatus(Phase.Paused, str(R.string.st_paused), str(R.string.st_paused_d))
        }
        wake.trySend(Unit)
    }

    fun approveCertificate(p: CertPrompt) {
        store.updateGroup(p.groupId) { it.copy(pins = it.pins + (p.url to p.fingerprint)) }
        _status.update { it.copy(pendingCert = null) }
        log("Certificate approved for ${p.url} (${p.fingerprint.take(11)}…)")
        failures = 0
        retryAt = 0L
        wake.trySend(Unit)
    }

    /** Fetches the data allowance of one account, or of every usable account when [accountId] is null. */
    fun refreshUsage(groupId: String, accountId: String? = null) {
        scope.launch {
            val g = store.data.value.groups.firstOrNull { it.id == groupId } ?: return@launch
            if (g.usageUrl.isBlank()) return@launch
            val targets = g.accounts.filter { (accountId == null || it.id == accountId) && it.enabled && !it.passwordRejected }
            if (targets.isEmpty()) return@launch
            usageLock.withLock {
                _usageBusy.update { it + targets.map { a -> a.id } }
                for (acc in targets) {
                    val fresh = store.data.value.groups.firstOrNull { it.id == groupId } ?: break
                    val client = UsageClient(tracker?.network, fresh.pins[fresh.usageUrl])
                    when (val r = client.fetch(fresh.usageUrl, acc.username, acc.password)) {
                        is UsageResult.Ok -> {
                            store.updateAccount(groupId, acc.id) { it.copy(usage = r.info) }
                            _usageErrors.update { it - acc.id }
                            UniLinkWidget.refresh(ctx)
                            log("Usage for \"${acc.name}\": ${r.info.usedMb.toInt()} MB used" +
                                (r.info.remainingMb?.let { ", ${it.toInt()} MB left" } ?: ", unlimited"))
                        }
                        is UsageResult.BadCredentials -> {
                            _usageErrors.update { it + (acc.id to r.message) }
                            log("Usage for \"${acc.name}\": user portal rejected the login (${r.message})")
                        }
                        is UsageResult.CertReview -> {
                            _usageCert.value = CertPrompt(fresh.id, fresh.name, fresh.usageUrl, r.fingerprint, r.subject, r.changed)
                            _usageErrors.update { it + (acc.id to "certificate") }
                            _usageBusy.update { it - targets.map { a -> a.id }.toSet() }
                            return@withLock
                        }
                        is UsageResult.Failure -> {
                            _usageErrors.update { it + (acc.id to r.reason) }
                            log("Usage for \"${acc.name}\" failed: ${r.reason}")
                        }
                    }
                    _usageBusy.update { it - acc.id }
                }
            }
        }
    }

    fun approveUsageCertificate(p: CertPrompt) {
        store.updateGroup(p.groupId) { it.copy(pins = it.pins + (p.url to p.fingerprint)) }
        _usageCert.value = null
        log("Certificate approved for ${p.url} (${p.fingerprint.take(11)}…)")
        refreshUsage(p.groupId)
    }

    fun dismissUsageCertificate() {
        _usageCert.value = null
    }

    // ---------- main loop ----------

    private fun onNetworkEvent() {
        _stats.update { it.copy(netEvents = it.netEvents + 1) }
        val t = tracker ?: return
        val n = t.network
        val sig = "${n?.networkHandle}|${n?.let { t.hasIpv4(it) }}|${t.ssid()}|vpn=${t.vpnUp}"
        if (sig != lastSignature) {
            lastSignature = sig
            failures = 0
            retryAt = 0L
            wake.trySend(Unit)
        }
    }

    @Volatile private var wifiDetectedAt = 0L

    private suspend fun sleep(ms: Long) {
        withTimeoutOrNull(ms.coerceAtLeast(500L)) { wake.receive() }
    }

    private suspend fun loop(t: WifiTracker) {
        while (currentCoroutineContext().isActive) {
            try {
                step(t)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                log("Internal error: ${e.javaClass.simpleName}: ${e.message}")
                sleep(10_000)
            }
        }
    }

    private suspend fun step(t: WifiTracker) {
        val now = System.currentTimeMillis()
        if (paused) {
            setStatus(Phase.Paused, str(R.string.st_paused), str(R.string.st_paused_d))
            sleep(300_000)
            return
        }
        val net = t.network
        if (net == null || !t.hasIpv4(net)) {
            dropSession("Wi-Fi unavailable")
            userRequested = false
            setStatus(Phase.WaitingWifi, str(R.string.st_wait_wifi), str(R.string.st_wait_wifi_none))
            sleep(60_000)
            return
        }
        val ssid = t.ssid()
        val data = store.data.value
        val group = matchGroup(data.groups, ssid)
        val matchNote = "SSID=${ssid ?: "hidden"} group=${group?.name ?: "none"}"
        if (matchNote != lastMatchNote) {
            lastMatchNote = matchNote
            wifiDetectedAt = android.os.SystemClock.elapsedRealtime()
            log("Wi-Fi detected: $matchNote")
        }
        if (group == null) {
            dropSession(null)
            val why = if (ssid == null) str(R.string.st_wait_wifi_hidden)
            else str(R.string.st_wait_wifi_unknown, ssid)
            setStatus(Phase.WaitingWifi, str(R.string.st_wait_wifi), why)
            sleep(60_000)
            return
        }
        if (!data.settings.autoSignIn && !userRequested) {
            setStatus(Phase.WaitingWifi, str(R.string.st_auto_off), str(R.string.st_auto_off_d, group.name), group.name)
            sleep(60_000)
            return
        }

        val s = session
        if (s != null && (s.groupId != group.id || s.netHandle != net.networkHandle)) {
            log("Network changed; session discarded")
            session = null
        }

        if (session == null) {
            if (now < retryAt) {
                sleep(retryAt - now)
                return
            }
            doLogin(group, net)
            return
        }
        if (now < nextKeepalive) {
            sleep(nextKeepalive - now)
            return
        }
        doKeepalive(group, net)
    }

    private fun matchGroup(groups: List<NetworkGroup>, ssid: String?): NetworkGroup? {
        if (ssid != null) groups.firstOrNull { g -> g.ssids.any { it == ssid } }?.let { return it }
        groups.firstOrNull { it.portalOnly }?.let { return it }
        // Android sometimes hides the Wi-Fi name even with location permission. With a single
        // configured network, try its portal anyway: nothing is sent without an approved
        // (pinned) certificate, and an unreachable portal just fails and is retried later.
        if (ssid == null && groups.size == 1) return groups[0]
        return null
    }

    private fun dropSession(reason: String?) {
        if (session != null) {
            if (reason != null) log("Session dropped: $reason")
            session = null
        }
    }

    // ---------- login ----------

    private suspend fun doLogin(g: NetworkGroup, net: Network) {
        setStatus(Phase.Connecting, str(R.string.st_signing_in), g.name, g.name)
        val t0 = android.os.SystemClock.elapsedRealtime()
        val outcome = opLock.withLock { attemptLogin(g, net) }
        // Timing helps find out why a sign-in feels slow (Wi-Fi settling, portal, retries).
        val took = (android.os.SystemClock.elapsedRealtime() - t0) / 1000.0
        val sinceWifi = (android.os.SystemClock.elapsedRealtime() - wifiDetectedAt) / 1000.0
        val timing = String.format(java.util.Locale.US, "%.1f s", took) +
            if (wifiDetectedAt > 0L && sinceWifi < 600) String.format(java.util.Locale.US, ", %.1f s after Wi-Fi detected", sinceWifi) else ""
        if (paused) {
            // Pause & sign out was pressed while this attempt was running: do not come back online.
            if (outcome is Outcome.LoggedIn && !outcome.adopted) {
                runCatching { PortalClient(net, g.pins[outcome.url]).logout(outcome.url, outcome.account.username) }
            }
            log("Attempt discarded: paused")
            setStatus(Phase.Paused, str(R.string.st_paused), str(R.string.st_paused_d))
            return
        }
        when (val o = outcome) {
            is Outcome.LoggedIn -> {
                val now = System.currentTimeMillis()
                session = Session(g.id, o.account.id, o.account.name, o.account.username, o.url, net.networkHandle)
                failures = 0
                retryAt = 0L
                if (noAccountsNotified) {
                    noAccountsNotified = false
                    Notices.clearNoAccounts(ctx)
                }
                lastKeepalive = now
                nextKeepalive = now + intervalMs(g)
                log(
                    if (o.adopted) "Portal already has a live session for \"${o.account.name}\" — keeping it alive"
                    else "Signed in with \"${o.account.name}\" on ${g.name} ($timing)"
                )
                setConnected(g)
                hintNetworkWorks(net)
            }
            is Outcome.NeedCert -> {
                retryAt = System.currentTimeMillis() + 60_000
                log(
                    if (o.prompt.changed) "Certificate for ${o.prompt.url} changed — review required (no password sent)"
                    else "Certificate for ${o.prompt.url} not approved yet — review required (no password sent)"
                )
                _status.value = EngineStatus(
                    phase = Phase.NeedCertificate,
                    title = str(R.string.st_review_cert),
                    detail = if (o.prompt.changed) str(R.string.st_cert_changed_d) else str(R.string.st_cert_new_d),
                    groupName = g.name,
                    pendingCert = o.prompt,
                )
            }
            Outcome.NoAccounts -> {
                val now = System.currentTimeMillis()
                if (g.accounts.isNotEmpty() && !noAccountsNotified && store.data.value.settings.notifyProblems) {
                    noAccountsNotified = true
                    Notices.noAccounts(ctx, g.name)
                }
                _status.value = EngineStatus(
                    phase = Phase.AccountNeeded,
                    title = str(R.string.st_account_needed),
                    detail = str(R.string.st_account_needed_d, g.name),
                    groupName = g.name,
                    nextCheck = now + 60_000,
                )
                retryAt = now + 60_000
            }
            is Outcome.Problem -> {
                retryAt = System.currentTimeMillis() + 60_000
                _status.value = EngineStatus(
                    phase = Phase.AccountNeeded,
                    title = o.title,
                    detail = o.detail,
                    groupName = g.name,
                    nextCheck = retryAt,
                )
            }
            is Outcome.Retry -> {
                failures++
                val now = System.currentTimeMillis()
                var seconds = maxOf(o.minSeconds, backoffSeconds(failures))
                // Right after joining the Wi-Fi the portal often needs a few seconds before it answers:
                // retry quickly for the first two minutes instead of backing off.
                val sinceWifi = (android.os.SystemClock.elapsedRealtime() - wifiDetectedAt) / 1000
                if (wifiDetectedAt > 0L && sinceWifi < 120 && o.reason != PortalClient.VPN_BLOCKED) {
                    val early = intArrayOf(3, 5, 8, 12, 20, 30)
                    seconds = maxOf(o.minSeconds, minOf(seconds, early[(failures - 1).coerceIn(0, early.lastIndex)]))
                }
                // A VPN that blocks Wi-Fi access can change at any moment; keep checking often.
                if (o.reason == PortalClient.VPN_BLOCKED) seconds = minOf(seconds, 60)
                retryAt = now + seconds * 1000L
                log("Retry in ${seconds}s: ${o.reason} (attempt took $timing)")
                _status.value = EngineStatus(
                    phase = Phase.WaitingRetry,
                    title = str(R.string.st_wait_retry),
                    detail = o.reason,
                    groupName = g.name,
                    nextCheck = retryAt,
                )
            }
        }
    }

    private suspend fun attemptLogin(g: NetworkGroup, net: Network): Outcome {
        val now = System.currentTimeMillis()
        val forced = forcedAccountId?.let { id -> g.accounts.firstOrNull { it.id == id } }
        forcedAccountId = null
        if (forced != null && !forced.isUsable(g, now)) {
            val why = when {
                !forced.enabled -> str(R.string.st_use_disabled)
                forced.passwordRejected -> str(R.string.st_use_rejected)
                else -> str(R.string.st_use_quota)
            }
            log("\"${forced.name}\" cannot be used: $why")
            return Outcome.Problem(str(R.string.st_use_failed_title, forced.name), why)
        }
        val candidates = if (forced != null) listOf(forced) else LoginOrder.order(
            g.accounts.filter { it.isUsable(g, now) }, now, store.data.value.settings.preferMostData
        )
        if (candidates.isEmpty()) return Outcome.NoAccounts

        // Before sending any password, ask the portal whether one of our accounts is already live
        // (signed in by hand in a browser, or before the app was restarted). Only the user name is
        // sent. Pressing Connect skips this and always signs in fresh.
        val fresh = forceFresh
        forceFresh = false
        if (!fresh) {
            probe@ for (acc in candidates) {
                for (url in g.portalUrls) {
                    when (val r = PortalClient(net, g.pins[url], fast = true).keepalive(url, acc.username, g.quotaPhrases)) {
                        is PortalResult.Success -> return Outcome.LoggedIn(acc, url, adopted = true)
                        is PortalResult.CertReview ->
                            return Outcome.NeedCert(CertPrompt(g.id, g.name, url, r.fingerprint, r.subject, r.changed))
                        is PortalResult.Failure -> {
                            // The portal could not even be reached (Wi-Fi just joined, roaming, congestion):
                            // a login would only wait out the same timeout, so go straight to a quick retry.
                            // A reply that merely times out (connection fine, live check slow) must NOT stop the
                            // sign-in: the portal often answers logins instantly while ignoring live checks.
                            if (isNetworkNotReady(r.reason)) {
                                log("Portal $url not reachable yet: ${r.reason}")
                                return Outcome.Retry(r.reason, 2)
                            }
                            break@probe
                        }
                        else -> Unit
                    }
                }
            }
        }

        for (acc in candidates) {
            var result: PortalResult? = null
            var usedUrl = ""
            var lastFailure = "No portal reachable"
            for (url in g.portalUrls) {
                val client = PortalClient(net, g.pins[url])
                val r = client.login(url, acc.username, acc.password, g.quotaPhrases)
                noteReplyShape("login", client)
                if (r is PortalResult.Failure) {
                    lastFailure = r.reason
                    log("Portal $url: ${r.reason}")
                    continue
                }
                result = r
                usedUrl = url
                break
            }
            when (val r = result) {
                null -> return Outcome.Retry(lastFailure, 5)
                is PortalResult.Success -> return Outcome.LoggedIn(acc, usedUrl)
                is PortalResult.Quota -> {
                    markExhausted(g, acc)
                    if (forced != null) {
                        log("\"${acc.name}\": data allowance used up this month")
                        return Outcome.Problem(str(R.string.st_use_failed_title, acc.name), str(R.string.st_use_quota))
                    }
                    log("\"${acc.name}\": data allowance used up this month — trying next account")
                    delay(3_000)
                }
                is PortalResult.BadCredentials -> {
                    store.updateAccount(g.id, acc.id) { it.copy(passwordRejected = true) }
                    log("\"${acc.name}\": password rejected — account blocked until edited")
                    if (forced != null) {
                        return Outcome.Problem(str(R.string.st_use_failed_title, acc.name), str(R.string.st_use_rejected))
                    }
                }
                is PortalResult.CertReview ->
                    return Outcome.NeedCert(CertPrompt(g.id, g.name, usedUrl, r.fingerprint, r.subject, r.changed))
                is PortalResult.Rejected ->
                    return Outcome.Retry("Unrecognized portal reply: ${r.message.take(120)}", 300)
                is PortalResult.Failure -> return Outcome.Retry(r.reason, 5)
            }
        }
        return Outcome.NoAccounts
    }

    private fun markExhausted(g: NetworkGroup, acc: Account) {
        val now = System.currentTimeMillis()
        val month = YearMonth.from(Instant.ofEpochMilli(now).atZone(g.zone())).toString()
        store.updateAccount(g.id, acc.id) { it.copy(exhaustedMonth = month, exhaustedAt = now) }
    }

    // ---------- keepalive ----------

    private suspend fun doKeepalive(g: NetworkGroup, net: Network) {
        val s = session ?: return
        val client = PortalClient(net, g.pins[s.url])
        val r = opLock.withLock { client.keepalive(s.url, s.username, g.quotaPhrases) }
        noteReplyShape("keepalive", client)
        val now = System.currentTimeMillis()
        // A keepalive that runs much later than scheduled means the CPU or radio was asleep.
        val lateSec = (now - nextKeepalive) / 1000
        if (lateSec > 45) {
            log("Keepalive ran ${lateSec}s late (the phone was probably asleep)")
            _stats.update { it.copy(late = it.late + 1, maxLateSec = maxOf(it.maxLateSec, lateSec)) }
        }
        when (r) {
            is PortalResult.Success -> {
                _stats.update { it.copy(ok = it.ok + 1) }
                failures = 0
                lastKeepalive = now
                nextKeepalive = now + intervalMs(g)
                setConnected(g)
            }
            is PortalResult.Quota -> {
                g.accounts.firstOrNull { it.id == s.accountId }?.let { markExhausted(g, it) }
                log("\"${s.accountName}\": data allowance used up — switching account")
                session = null
                retryAt = now + 3_000
            }
            is PortalResult.CertReview -> {
                session = null
                retryAt = now + 60_000
                log("Certificate changed during keepalive — review required")
                _status.value = EngineStatus(
                    phase = Phase.NeedCertificate,
                    title = str(R.string.st_review_cert),
                    detail = str(R.string.st_cert_changed_d),
                    groupName = g.name,
                    pendingCert = CertPrompt(g.id, g.name, s.url, r.fingerprint, r.subject, r.changed),
                )
            }
            is PortalResult.Rejected, is PortalResult.BadCredentials -> {
                log("Session is no longer live — signing in again")
                session = null
                retryAt = 0L
            }
            is PortalResult.Failure -> {
                _stats.update { it.copy(failed = it.failed + 1) }
                failures++
                val wait = minOf(backoffSeconds(failures) * 1000L, intervalMs(g))
                nextKeepalive = now + wait
                log("Keepalive failed (#$failures): ${r.reason}")
                if (failures >= 5) {
                    session = null
                    retryAt = 0L
                    log("Too many keepalive failures — signing in again")
                } else {
                    _status.value = EngineStatus(
                        phase = Phase.WaitingRetry,
                        title = str(R.string.st_wait_retry),
                        detail = r.reason,
                        groupName = g.name,
                        accountId = s.accountId,
                        accountName = s.accountName,
                        lastKeepalive = lastKeepalive,
                        nextCheck = nextKeepalive,
                    )
                }
            }
        }
    }

    // ---------- helpers ----------

    private val shapesLogged = mutableSetOf<String>()

    /** Logs each distinct set of reply element names once (names only, never values). */
    private fun noteReplyShape(kind: String, c: PortalClient) {
        val fields = c.replyFields ?: return
        if (shapesLogged.add("$kind:$fields")) log("Portal $kind reply fields: $fields")
    }

    /** UI text in the language chosen in Settings (log lines stay English on purpose). */
    private fun str(@StringRes id: Int, vararg args: Any): String = Lang.wrap(ctx).getString(id, *args)

    private fun intervalMs(g: NetworkGroup) = g.keepaliveSec.coerceIn(30, 180) * 1000L

    /** True only when the portal could not be reached at all (not when it was slow to answer). */
    private fun isNetworkNotReady(reason: String): Boolean {
        val r = reason.lowercase()
        return "failed to connect" in r || "unable to resolve" in r || "no route" in r || "unreachable" in r
    }

    /**
     * The sign-in happened inside the app, so Android never saw the captive-portal page and may keep
     * the Wi-Fi marked "no internet" (which makes phones prefer mobile data and can stop Wi-Fi
     * tethering). Tell the system the network works so it re-checks, and log what it concludes.
     */
    private fun hintNetworkWorks(net: Network) {
        runCatching {
            val cm = ctx.getSystemService(android.net.ConnectivityManager::class.java)
            fun validated() = cm.getNetworkCapabilities(net)
                ?.hasCapability(android.net.NetworkCapabilities.NET_CAPABILITY_VALIDATED)
            if (validated() == false) {
                cm.reportNetworkConnectivity(net, true)
                scope.launch {
                    delay(8_000)
                    log("Android network check after sign-in: validated=${validated()}")
                }
            }
        }
    }

    private fun backoffSeconds(n: Int): Int {
        val table = intArrayOf(5, 15, 30, 60, 120, 300, 600, 900)
        return table[(n - 1).coerceIn(0, table.lastIndex)]
    }

    private fun setConnected(g: NetworkGroup) {
        val s = session ?: return
        _status.value = EngineStatus(
            phase = Phase.Connected,
            title = str(R.string.st_connected),
            detail = str(R.string.st_connected_d, s.accountName, g.name),
            groupName = g.name,
            accountId = s.accountId,
            accountName = s.accountName,
            lastKeepalive = lastKeepalive,
            nextCheck = nextKeepalive,
        )
    }

    private fun setStatus(phase: Phase, title: String, detail: String, group: String? = null) {
        val cur = _status.value
        if (cur.phase == phase && cur.title == title && cur.detail == detail && cur.groupName == group) return
        _status.value = EngineStatus(phase = phase, title = title, detail = detail, groupName = group)
    }

    private val timeFmt = SimpleDateFormat("HH:mm:ss", Locale.US)

    fun log(message: String) {
        val line = "${timeFmt.format(Date())}  $message"
        _log.update { (it + line).takeLast(300) }
    }

    fun clearLog() {
        _log.value = emptyList()
    }
}
