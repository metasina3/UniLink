package ir.unilink.android

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat

/** One-off, quiet notifications (never sounds, never repeats for the same problem). */
object Notices {
    private const val CHANNEL = "unilink_problems"
    private const val ID_NO_ACCOUNTS = 3

    fun noAccounts(ctx: Context, networkName: String) {
        val ui = Lang.wrap(ctx)
        val nm = ctx.getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(
            NotificationChannel(CHANNEL, ui.getString(R.string.notif_problem_channel), NotificationManager.IMPORTANCE_LOW)
        )
        val open = PendingIntent.getActivity(
            ctx, 3, Intent(ctx, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        val n = NotificationCompat.Builder(ctx, CHANNEL)
            .setSmallIcon(R.drawable.ic_stat)
            .setContentTitle(ui.getString(R.string.notif_noacc_title))
            .setContentText(ui.getString(R.string.notif_noacc_text, networkName))
            .setStyle(NotificationCompat.BigTextStyle().bigText(ui.getString(R.string.notif_noacc_text, networkName)))
            .setContentIntent(open)
            .setAutoCancel(true)
            .setSilent(true)
            .build()
        runCatching { nm.notify(ID_NO_ACCOUNTS, n) } // no-op when notifications are blocked
    }

    fun clearNoAccounts(ctx: Context) {
        runCatching { ctx.getSystemService(NotificationManager::class.java).cancel(ID_NO_ACCOUNTS) }
    }
}
