package ir.unilink.android.ui

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.PowerManager
import android.provider.Settings
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat

fun locationGranted(ctx: Context) =
    ContextCompat.checkSelfPermission(ctx, Manifest.permission.ACCESS_FINE_LOCATION) ==
        PackageManager.PERMISSION_GRANTED

/** True only if the user has not blocked UniLink's notifications (permission, app switch or channel). */
fun notificationsEnabled(ctx: Context): Boolean {
    val nm = NotificationManagerCompat.from(ctx)
    if (!nm.areNotificationsEnabled()) return false
    val channel = nm.getNotificationChannel("unilink_status")
    return channel == null || channel.importance != android.app.NotificationManager.IMPORTANCE_NONE
}

fun batteryUnrestricted(ctx: Context) =
    (ctx.getSystemService(Context.POWER_SERVICE) as PowerManager).isIgnoringBatteryOptimizations(ctx.packageName)

fun openNotificationSettings(ctx: Context) {
    runCatching {
        ctx.startActivity(
            Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
                .putExtra(Settings.EXTRA_APP_PACKAGE, ctx.packageName)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        )
    }
}

fun requestBatteryUnrestricted(ctx: Context) {
    runCatching {
        ctx.startActivity(
            Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS)
                .setData(Uri.parse("package:${ctx.packageName}"))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        )
    }
}

/**
 * Opens something useful whatever the state: the system "allow" dialog when battery use is still
 * restricted, otherwise the app's own settings page (the per-app Battery entry lives there), and
 * the system battery-optimisation list if even that page cannot open.
 */
fun openBatterySettings(ctx: Context) {
    val flags = Intent.FLAG_ACTIVITY_NEW_TASK
    if (!batteryUnrestricted(ctx)) {
        val ok = runCatching {
            ctx.startActivity(
                Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS)
                    .setData(Uri.parse("package:${ctx.packageName}")).addFlags(flags)
            )
        }.isSuccess
        if (ok) return
    }
    val ok = runCatching {
        ctx.startActivity(
            Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS)
                .setData(Uri.parse("package:${ctx.packageName}")).addFlags(flags)
        )
    }.isSuccess
    if (!ok) runCatching {
        ctx.startActivity(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS).addFlags(flags))
    }
}

/** True when the phone's Wi-Fi radio is switched on. */
fun wifiRadioOn(ctx: Context): Boolean = runCatching {
    (ctx.applicationContext.getSystemService(Context.WIFI_SERVICE) as android.net.wifi.WifiManager).isWifiEnabled
}.getOrDefault(true)

/**
 * Android 10+ no longer lets apps switch Wi-Fi on silently, so open the system panel
 * that has the Wi-Fi switch (one tap for the user); older versions get the Wi-Fi settings page.
 */
fun openWifiPanel(ctx: Context) {
    val action = if (android.os.Build.VERSION.SDK_INT >= 29) Settings.Panel.ACTION_INTERNET_CONNECTIVITY
    else Settings.ACTION_WIFI_SETTINGS
    runCatching { ctx.startActivity(Intent(action).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
}
