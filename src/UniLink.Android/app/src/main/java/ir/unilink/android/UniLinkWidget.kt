package ir.unilink.android

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.widget.RemoteViews

/**
 * Small home-screen widget (plain RemoteViews, no extra library): the current status plus one
 * button that connects or pauses, like the Quick Settings tile.
 */
class UniLinkWidget : AppWidgetProvider() {
    override fun onUpdate(context: Context, manager: AppWidgetManager, ids: IntArray) {
        refresh(context)
    }

    companion object {
        fun refresh(context: Context) {
            val manager = AppWidgetManager.getInstance(context)
            val ids = manager.getAppWidgetIds(ComponentName(context, UniLinkWidget::class.java))
            if (ids.isEmpty()) return
            val app = context.applicationContext as UniLinkApp
            val st = app.engine.status.value
            val ui = Lang.wrap(context)
            val active = st.phase == Phase.Connected || st.phase == Phase.Connecting

            val dot = when (st.phase) {
                Phase.Connected -> 0xFF3DDC84.toInt()
                Phase.Connecting -> 0xFF6FA8FF.toInt()
                Phase.WaitingRetry, Phase.NeedCertificate, Phase.AccountNeeded -> 0xFFFFB74D.toInt()
                else -> 0xFF8A97AD.toInt()
            }
            val subtitle = when {
                st.phase == Phase.Connected -> {
                    val name = st.accountName ?: st.groupName ?: ""
                    val left = app.store.data.value.groups.flatMap { it.accounts }
                        .firstOrNull { it.id == st.accountId }?.usage?.remainingMb
                    if (left != null) "$name · " + ui.getString(R.string.widget_left, formatMb(left)) else name
                }
                st.phase == Phase.Stopped || st.phase == Phase.Paused -> ui.getString(R.string.tile_tap_connect)
                else -> st.detail
            }

            val toggle = PendingIntent.getActivity(
                context, 0,
                Intent(context, WidgetActionActivity::class.java)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_NO_ANIMATION or Intent.FLAG_ACTIVITY_NO_HISTORY),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            val open = PendingIntent.getActivity(
                context, 1, Intent(context, MainActivity::class.java),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )

            val views = RemoteViews(context.packageName, R.layout.widget).apply {
                setTextViewText(R.id.widget_title, st.title)
                setTextViewText(R.id.widget_subtitle, subtitle)
                setTextColor(R.id.widget_dot, dot)
                setTextViewText(
                    R.id.widget_action,
                    ui.getString(if (active) R.string.widget_pause else R.string.widget_connect),
                )
                setOnClickPendingIntent(R.id.widget_action, toggle)
                setOnClickPendingIntent(R.id.widget_root, open)
                setTextColor(R.id.widget_title, Color.WHITE)
            }
            manager.updateAppWidget(ids, views)
        }
    }
}

/**
 * The widget button opens this invisible activity, does the action and closes at once. An activity
 * started from a widget tap may always start the keep-alive service and open the Wi-Fi switch, which a
 * plain broadcast receiver cannot do reliably on recent Android versions (the button then looked dead).
 */
class WidgetActionActivity : android.app.Activity() {
    override fun onCreate(savedInstanceState: android.os.Bundle?) {
        super.onCreate(savedInstanceState)
        val app = applicationContext as UniLinkApp
        val st = app.engine.status.value
        val phase = st.phase
        // No campus Wi-Fi to sign in to: say why, so the tap never looks dead.
        if (phase == Phase.WaitingWifi || phase == Phase.AccountNeeded) {
            android.widget.Toast.makeText(this, st.title + (if (st.detail.isNotBlank()) ": " + st.detail else ""), android.widget.Toast.LENGTH_LONG).show()
        }
        if (phase == Phase.Connected || phase == Phase.Connecting) {
            app.engine.pauseAndSignOut()
        } else {
            runCatching { KeepAliveService.start(this) }
            app.engine.connectNow()
            // Wi-Fi is off: open the system switch so one more tap joins campus Wi-Fi.
            if (!ir.unilink.android.ui.wifiRadioOn(this)) ir.unilink.android.ui.openWifiPanel(this)
        }
        UniLinkWidget.refresh(this)
        finish()
        @Suppress("DEPRECATION")
        overridePendingTransition(0, 0)
    }
}
