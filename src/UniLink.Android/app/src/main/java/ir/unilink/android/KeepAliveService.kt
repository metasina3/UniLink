package ir.unilink.android

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.wifi.WifiManager
import android.os.IBinder
import android.os.PowerManager
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch

/** Foreground service that hosts the [Engine] so sign-in and keepalive survive screen-off and VPNs. */
class KeepAliveService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var wakeLock: PowerManager.WakeLock? = null
    private var wifiLock: WifiManager.WifiLock? = null

    private val app get() = application as UniLinkApp

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        createChannel()
        ServiceCompat.startForeground(
            this, NOTIF_ID, buildNotification(app.engine.status.value),
            ServiceInfo.FOREGROUND_SERVICE_TYPE_MANIFEST
        )
        app.engine.start()

        scope.launch { updateWatcher() }

        scope.launch {
            app.engine.status.combine(app.store.data) { st, d -> Triple(st, d.settings.holdWakeLock, d.settings.holdWifiLock) }
                .collect { (st, cpu, wifi) ->
                    // Hold the locks only while there is campus Wi-Fi work to do. Without it (no campus
                    // Wi-Fi, paused, waiting for the user) nothing needs the CPU or the radio awake.
                    val working = st.phase == Phase.Connecting || st.phase == Phase.Connected ||
                        st.phase == Phase.WaitingRetry
                    applyLocks(cpu && working, wifi && working)
                    getSystemService(NotificationManager::class.java)
                        .notify(NOTIF_ID, buildNotification(st))
                    UniLinkWidget.refresh(this@KeepAliveService)
                }
        }
    }

    /**
     * Quiet update notice: at most one GitHub request per day, and a notification only when a version
     * newer than the installed one exists and has not been announced yet. Failed checks (for example
     * before the portal sign-in gives internet) are retried in 30 minutes.
     */
    private suspend fun updateWatcher() {
        delay(90_000)
        while (true) {
            val s = app.store.data.value.settings
            val now = System.currentTimeMillis()
            var wait = 6 * 3_600_000L
            if (s.updateNotify && now - s.lastUpdateCheck >= 24 * 3_600_000L) {
                when (val r = UpdateCheck.check(BuildConfig.VERSION_NAME)) {
                    is UpdateCheck.Result.Failed -> wait = 30 * 60_000L
                    is UpdateCheck.Result.UpToDate ->
                        app.store.updateSettings { it.copy(lastUpdateCheck = System.currentTimeMillis()) }
                    is UpdateCheck.Result.Available -> {
                        app.store.updateSettings { it.copy(lastUpdateCheck = System.currentTimeMillis()) }
                        if (r.latest != s.notifiedVersion) {
                            notifyUpdate(r.latest, r.url)
                            app.store.updateSettings { it.copy(notifiedVersion = r.latest) }
                        }
                    }
                }
            }
            delay(wait)
        }
    }

    private fun notifyUpdate(version: String, url: String) {
        val ui = Lang.wrap(this)
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(
            NotificationChannel(UPDATE_CHANNEL, ui.getString(R.string.notif_update_channel), NotificationManager.IMPORTANCE_LOW)
        )
        val open = PendingIntent.getActivity(
            this, 2, Intent(Intent.ACTION_VIEW, android.net.Uri.parse(url)),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        val n = NotificationCompat.Builder(this, UPDATE_CHANNEL)
            .setSmallIcon(R.drawable.ic_stat)
            .setContentTitle(ui.getString(R.string.notif_update_title, version))
            .setContentText(ui.getString(R.string.notif_update_text))
            .setContentIntent(open)
            .setAutoCancel(true)
            .setSilent(true)
            .build()
        runCatching { nm.notify(UPDATE_NOTIF_ID, n) } // no-op if notifications are blocked
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_EXIT) {
            app.engine.stop()
            stopSelf()
            return START_NOT_STICKY
        }
        return START_STICKY
    }

    override fun onDestroy() {
        scope.cancel()
        releaseLocks()
        app.engine.stop()
        UniLinkWidget.refresh(this)
        super.onDestroy()
    }

    /** [cpu] keeps timers firing with the screen off; [wifi] keeps the Wi-Fi radio at full power. */
    private fun applyLocks(cpu: Boolean, wifi: Boolean) {
        if (!cpu) {
            runCatching { wakeLock?.takeIf { it.isHeld }?.release() }
            wakeLock = null
        } else if (wakeLock == null) {
            val pm = getSystemService(Context.POWER_SERVICE) as PowerManager
            wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "UniLink:keepalive").apply {
                setReferenceCounted(false)
                acquire()
            }
        }
        if (!wifi) {
            runCatching { wifiLock?.takeIf { it.isHeld }?.release() }
            wifiLock = null
        } else if (wifiLock == null) {
            val wm = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
            @Suppress("DEPRECATION")
            wifiLock = wm.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "UniLink:wifi").apply {
                setReferenceCounted(false)
                acquire()
            }
        }
    }

    private fun releaseLocks() {
        runCatching { wakeLock?.takeIf { it.isHeld }?.release() }
        runCatching { wifiLock?.takeIf { it.isHeld }?.release() }
        wakeLock = null
        wifiLock = null
    }

    private fun createChannel() {
        val ui = Lang.wrap(this)
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(
            NotificationChannel(CHANNEL_ID, ui.getString(R.string.notif_channel_name), NotificationManager.IMPORTANCE_LOW).apply {
                description = ui.getString(R.string.notif_channel_desc)
                setShowBadge(false)
            }
        )
    }

    private fun buildNotification(st: EngineStatus): Notification {
        val open = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        val exit = PendingIntent.getService(
            this, 1, Intent(this, KeepAliveService::class.java).setAction(ACTION_EXIT),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        val text = when {
            st.phase == Phase.Connected && st.accountName != null -> "${st.title} · ${st.accountName}"
            else -> st.title
        }
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_stat)
            .setContentTitle("UniLink")
            .setContentText(text)
            .setContentIntent(open)
            .addAction(0, Lang.wrap(this).getString(R.string.notif_exit), exit)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .build()
    }

    companion object {
        private const val CHANNEL_ID = "unilink_status"
        private const val NOTIF_ID = 1
        private const val UPDATE_CHANNEL = "unilink_update"
        private const val UPDATE_NOTIF_ID = 2
        private const val ACTION_EXIT = "ir.unilink.android.EXIT"

        fun start(context: Context) {
            ContextCompat.startForegroundService(context, Intent(context, KeepAliveService::class.java))
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, KeepAliveService::class.java))
        }
    }
}
