package ir.unilink.android

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != Intent.ACTION_BOOT_COMPLETED) return
        val app = context.applicationContext as UniLinkApp
        val s = app.store.data.value.settings
        if (s.startOnBoot && s.autoSignIn) {
            runCatching { KeepAliveService.start(context) }
        }
    }
}
