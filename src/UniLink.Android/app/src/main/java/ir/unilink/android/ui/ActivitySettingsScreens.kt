package ir.unilink.android.ui

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import ir.unilink.android.BuildConfig
import ir.unilink.android.KeepAliveService
import ir.unilink.android.Lang
import ir.unilink.android.R
import ir.unilink.android.UniLinkApp

@Composable
fun ActivityScreen(app: UniLinkApp) {
    val log by app.engine.logLines.collectAsState()
    val ctx = LocalContext.current
    val subject = stringResource(R.string.act_share_subject)
    val chooser = stringResource(R.string.act_share_chooser)
    Column(Modifier.fillMaxSize().padding(16.dp).padding(bottom = LocalBarPad.current), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        ScreenTitle(stringResource(R.string.tab_activity))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = {
                val send = Intent(Intent.ACTION_SEND).apply {
                    type = "text/plain"
                    putExtra(Intent.EXTRA_SUBJECT, subject)
                    putExtra(Intent.EXTRA_TEXT, log.joinToString("\n"))
                }
                ctx.startActivity(Intent.createChooser(send, chooser))
            }) { Text(stringResource(R.string.act_share_log)) }
            OutlinedButton(onClick = { app.engine.clearLog() }) { Text(stringResource(R.string.act_clear)) }
        }
        Text(
            stringResource(R.string.act_note),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        GlassSurface(Modifier.fillMaxSize()) {
            if (log.isEmpty()) {
                Text(
                    stringResource(R.string.act_empty),
                    modifier = Modifier.padding(16.dp),
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            // Log lines are English: keep them left-to-right even in the Persian layout.
            CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
                LazyColumn(
                    Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(start = 14.dp, end = 14.dp, top = 8.dp, bottom = 8.dp),
                ) {
                    items(log.asReversed()) { line -> LogLine(line) }
                }
            }
        }
    }
}

@Composable
private fun SwitchRow(title: String, subtitle: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f).padding(end = 12.dp)) {
            Text(title, style = MaterialTheme.typography.bodyLarge)
            Text(subtitle, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Switch(checked = checked, onCheckedChange = onChange)
    }
}

@Composable
private fun PermissionRow(title: String, subtitle: String, granted: Boolean, onFix: () -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f).padding(end = 12.dp)) {
            Text(title, style = MaterialTheme.typography.bodyLarge)
            Text(subtitle, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        if (granted) Tag(stringResource(R.string.set_ok), StatusGreen)
        else OutlinedButton(onClick = onFix) { Text(stringResource(R.string.common_allow)) }
    }
}

@Composable
fun SettingsScreen(app: UniLinkApp) {
    val data by app.store.data.collectAsState()
    val s = data.settings
    val ctx = LocalContext.current
    val tick = rememberResumeTick()
    var lang by remember { mutableStateOf(Lang.get(ctx)) }

    val notifLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { }
    val locLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { }

    val notifGranted = remember(tick) { notificationsEnabled(ctx) }
    val locGranted = remember(tick) { locationGranted(ctx) }
    val batteryOk = remember(tick) { batteryUnrestricted(ctx) }

    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp).padding(bottom = LocalBarPad.current),
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        ScreenTitle(stringResource(R.string.tab_settings))

        Panel {
            Text(stringResource(R.string.set_lang_title), style = MaterialTheme.typography.titleMedium)
            ChoiceRow(
                listOf(
                    Lang.SYSTEM to stringResource(R.string.set_lang_system),
                    Lang.EN to stringResource(R.string.set_lang_en),
                    Lang.FA to stringResource(R.string.set_lang_fa),
                ),
                lang,
            ) { code ->
                lang = code
                Lang.set(ctx, code)
                (ctx as? Activity)?.recreate()
            }
        }

        Panel {
            Text(stringResource(R.string.set_theme_title), style = MaterialTheme.typography.titleMedium)
            ChoiceRow(
                listOf(
                    ThemePrefs.SYSTEM to stringResource(R.string.set_theme_system),
                    ThemePrefs.LIGHT to stringResource(R.string.set_theme_light),
                    ThemePrefs.DARK to stringResource(R.string.set_theme_dark),
                    ThemePrefs.MIDNIGHT to stringResource(R.string.set_theme_midnight),
                ),
                ThemePrefs.mode,
            ) { code -> ThemePrefs.set(ctx, code) }
        }

        Panel {
            SwitchRow(
                stringResource(R.string.set_auto_title),
                stringResource(R.string.set_auto_sub),
                s.autoSignIn,
            ) { v -> app.store.updateSettings { it.copy(autoSignIn = v) }; app.engine.kick() }
            SwitchRow(
                stringResource(R.string.set_boot_title),
                stringResource(R.string.set_boot_sub),
                s.startOnBoot,
            ) { v -> app.store.updateSettings { it.copy(startOnBoot = v) } }
            SwitchRow(
                stringResource(R.string.set_wake_title),
                stringResource(R.string.set_wake_sub),
                s.holdWakeLock,
            ) { v -> app.store.updateSettings { it.copy(holdWakeLock = v) } }
            SwitchRow(
                stringResource(R.string.set_wifi_title),
                stringResource(R.string.set_wifi_sub),
                s.holdWifiLock,
            ) { v -> app.store.updateSettings { it.copy(holdWifiLock = v) } }
            SwitchRow(
                stringResource(R.string.set_prefer_title),
                stringResource(R.string.set_prefer_sub),
                s.preferMostData,
            ) { v -> app.store.updateSettings { it.copy(preferMostData = v) } }
            SwitchRow(
                stringResource(R.string.set_switch_title),
                stringResource(R.string.set_switch_sub),
                s.switchOnUse,
            ) { v -> app.store.updateSettings { it.copy(switchOnUse = v) } }
            SwitchRow(
                stringResource(R.string.set_problems_title),
                stringResource(R.string.set_problems_sub),
                s.notifyProblems,
            ) { v -> app.store.updateSettings { it.copy(notifyProblems = v) } }
            SwitchRow(
                stringResource(R.string.set_update_title),
                stringResource(R.string.set_update_sub),
                s.updateNotify,
            ) { v -> app.store.updateSettings { it.copy(updateNotify = v) } }
        }

        Panel {
            Text(stringResource(R.string.set_perm_title), style = MaterialTheme.typography.titleMedium)
            PermissionRow(
                stringResource(R.string.set_loc_title), stringResource(R.string.set_loc_sub), locGranted,
            ) { locLauncher.launch(Manifest.permission.ACCESS_FINE_LOCATION) }
            PermissionRow(
                stringResource(R.string.set_notif_title), stringResource(R.string.set_notif_sub), notifGranted,
            ) {
                if (Build.VERSION.SDK_INT >= 33 &&
                    ContextCompat.checkSelfPermission(ctx, Manifest.permission.POST_NOTIFICATIONS) !=
                    PackageManager.PERMISSION_GRANTED
                ) notifLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                else openNotificationSettings(ctx)
            }
            PermissionRow(
                stringResource(R.string.set_batt_title), stringResource(R.string.set_batt_sub), batteryOk,
            ) { requestBatteryUnrestricted(ctx) }
        }

        BatteryGuidePanel()

        OutlinedButton(
            onClick = { app.store.updateSettings { it.copy(tourDone = false) } },
            modifier = Modifier.padding(top = 6.dp),
        ) { Text(stringResource(R.string.set_tour_again)) }

        AboutPanel()

        Button(
            onClick = {
                KeepAliveService.stop(ctx)
                (ctx as? Activity)?.finishAffinity()
            },
            modifier = Modifier.padding(top = 6.dp),
        ) { Text(stringResource(R.string.set_exit)) }
    }
}

private val timePrefix = Regex("^(\\d\\d:\\d\\d:\\d\\d)\\s+(.*)$", RegexOption.DOT_MATCHES_ALL)

/** One log entry: dim monospace time, message coloured by kind, thin divider below. */
@Composable
private fun LogLine(line: String) {
    val m = timePrefix.matchEntire(line)
    val time = m?.groupValues?.get(1)
    val msg = m?.groupValues?.get(2) ?: line
    val low = msg.lowercase()
    val color = when {
        low.startsWith("signed in") -> StatusGreen
        "dropped" in low || "failed" in low || "rejected" in low || "error" in low || "blocked" in low -> StatusAmber
        else -> MaterialTheme.colorScheme.onSurface
    }
    Column {
        Row(Modifier.padding(vertical = 7.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            if (time != null) {
                Text(
                    time,
                    fontFamily = FontFamily.Monospace,
                    fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 1.dp),
                )
            }
            Text(msg, fontSize = 13.sp, lineHeight = 17.sp, color = color, modifier = Modifier.weight(1f))
        }
        androidx.compose.material3.HorizontalDivider(color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.07f))
    }
}
