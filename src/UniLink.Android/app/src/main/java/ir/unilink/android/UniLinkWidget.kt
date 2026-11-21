package ir.unilink.android

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.BroadcastReceiver
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
        const val ACTION_TOGGLE = "ir.unilink.android.WIDGET_TOGGLE"

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

            val toggle = PendingIntent.getBroadcast(
                context, 0,
                Intent(context, WidgetActionReceiver::class.java).setAction(ACTION_TOGGLE),
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

/** Not exported: only the widget's own PendingIntent can reach it. */
class WidgetActionReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != UniLinkWidget.ACTION_TOGGLE) return
        val app = context.applicationContext as UniLinkApp
        val phase = app.engine.status.value.phase
        if (phase == Phase.Connected || phase == Phase.Connecting) {
            app.engine.pauseAndSignOut()
        } else {
            runCatching { KeepAliveService.start(context) }
            app.engine.connectNow()
        }
    }
}
