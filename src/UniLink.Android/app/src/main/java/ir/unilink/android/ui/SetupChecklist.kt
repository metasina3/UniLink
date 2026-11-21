package ir.unilink.android.ui

import android.Manifest
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ir.unilink.android.R
import ir.unilink.android.UniLinkApp

private class Step(
    val title: String,
    val hint: String,
    val done: Boolean,
    val actionLabel: String? = null,
    val action: (() -> Unit)? = null,
)

/**
 * First-run checklist. Shown on the Connection tab until everything is ready, then it disappears
 * on its own. [goToTab] switches the bottom tab (1 = Accounts).
 */
@Composable
fun SetupChecklist(app: UniLinkApp, goToTab: (Int) -> Unit) {
    val ctx = LocalContext.current
    val data by app.store.data.collectAsState()
    val resumeTick = rememberResumeTick()
    var refresh by remember { mutableIntStateOf(0) }

    val locLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { refresh++ }
    val notifLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        // If Android won't show the dialog again (or the app was blocked in system settings),
        // take the user straight to the notification settings instead.
        if (!granted || !notificationsEnabled(ctx)) openNotificationSettings(ctx)
        refresh++
    }

    val tAccount = stringResource(R.string.setup_account_title)
    val hAccount = stringResource(R.string.setup_account_hint)
    val bAccount = stringResource(R.string.setup_open_accounts)
    val tLoc = stringResource(R.string.setup_location_title)
    val hLoc = stringResource(R.string.setup_location_hint)
    val tNotif = stringResource(R.string.setup_notif_title)
    val hNotif = stringResource(R.string.setup_notif_hint)
    val tBatt = stringResource(R.string.setup_battery_title)
    val hBatt = stringResource(R.string.setup_battery_hint)
    val tCert = stringResource(R.string.setup_cert_title)
    val hCert = stringResource(R.string.setup_cert_hint)
    val allow = stringResource(R.string.common_allow)

    // Recomputed whenever the screen resumes or a permission dialog returns.
    val steps = remember(resumeTick, refresh, data) {
        listOf(
            Step(
                tAccount,
                hAccount,
                data.groups.any { g -> g.accounts.isNotEmpty() },
                bAccount,
            ) { goToTab(1) },
            Step(
                tLoc,
                hLoc,
                locationGranted(ctx),
                allow,
            ) { locLauncher.launch(Manifest.permission.ACCESS_FINE_LOCATION) },
            Step(
                tNotif,
                hNotif,
                notificationsEnabled(ctx),
                allow,
            ) {
                if (Build.VERSION.SDK_INT >= 33 &&
                    androidx.core.content.ContextCompat.checkSelfPermission(ctx, Manifest.permission.POST_NOTIFICATIONS) !=
                    android.content.pm.PackageManager.PERMISSION_GRANTED
                ) notifLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                else openNotificationSettings(ctx)
            },
            Step(
                tBatt,
                hBatt,
                batteryUnrestricted(ctx),
                allow,
            ) { requestBatteryUnrestricted(ctx) },
            Step(
                tCert,
                hCert,
                data.groups.any { it.pins.isNotEmpty() },
            ),
        )
    }

    if (steps.all { it.done }) return
    val doneCount = steps.count { it.done }

    Panel {
        Text(stringResource(R.string.setup_title, doneCount, steps.size), style = MaterialTheme.typography.titleMedium)
        Column(verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.padding(top = 4.dp)) {
            steps.forEach { s ->
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f).padding(end = 8.dp)) {
                        Text(
                            (if (s.done) "✓  " else "○  ") + s.title,
                            style = MaterialTheme.typography.bodyLarge,
                            color = if (s.done) StatusGreen else MaterialTheme.colorScheme.onSurface,
                        )
                        if (!s.done) {
                            Text(
                                s.hint,
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                    if (!s.done && s.action != null && s.actionLabel != null) {
                        OutlinedButton(onClick = s.action) { Text(s.actionLabel) }
                    }
                }
            }
        }
    }
}
