package ir.unilink.android.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import ir.unilink.android.NetworkGroup
import ir.unilink.android.R
import ir.unilink.android.UniLinkApp
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import java.time.ZoneId

@Composable
fun NetworksScreen(app: UniLinkApp) {
    val data by app.store.data.collectAsState()
    var editing by remember { mutableStateOf<NetworkGroup?>(null) }
    var adding by remember { mutableStateOf(false) }
    var deleting by remember { mutableStateOf<NetworkGroup?>(null) }

    Box(Modifier.fillMaxSize().padding(horizontal = 16.dp)) {
        Column(Modifier.fillMaxSize().padding(top = 16.dp)) {
            ScreenTitle(stringResource(R.string.tab_networks))
            LazyColumn(
                Modifier.fillMaxSize(),
                contentPadding = PaddingValues(top = 12.dp, bottom = 88.dp + LocalBarPad.current),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                items(data.groups, key = { it.id }) { g ->
                    Panel(Modifier.animateItem()) {
                        Text(g.name, style = MaterialTheme.typography.titleMedium)
                        InfoRow(stringResource(R.string.net_wifi_names), g.ssids.joinToString().ifEmpty { "—" })
                        InfoRow(stringResource(R.string.net_accounts), g.accounts.size.toString())
                        InfoRow(stringResource(R.string.net_keepalive), stringResource(R.string.net_seconds, g.keepaliveSec))
                        InfoRow(stringResource(R.string.net_timezone), g.timeZone)
                        g.portalUrls.forEach { url ->
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Text(url, Modifier.weight(1f), style = MaterialTheme.typography.bodySmall)
                                if (g.pins.containsKey(url)) Tag(stringResource(R.string.net_pinned), StatusGreen)
                                else Tag(stringResource(R.string.net_not_pinned), StatusAmber)
                            }
                        }
                        if (g.portalOnly) {
                            Text(
                                stringResource(R.string.net_portal_only_note),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                        Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                            TextButton(onClick = { editing = g }) { Text(stringResource(R.string.menu_edit)) }
                            if (g.pins.isNotEmpty()) {
                                TextButton(onClick = {
                                    app.store.updateGroup(g.id) { it.copy(pins = emptyMap()) }
                                    app.engine.log("Pinned certificates cleared for ${g.name}")
                                    app.engine.kick()
                                }) { Text(stringResource(R.string.net_forget_certs)) }
                            }
                            TextButton(
                                onClick = { deleting = g },
                                colors = androidx.compose.material3.ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.error),
                            ) { Text(stringResource(R.string.common_delete)) }
                        }
                    }
                }
            }
        }
        FloatingActionButton(
            onClick = { adding = true },
            modifier = Modifier.align(Alignment.BottomEnd).padding(bottom = 16.dp + LocalBarPad.current),
            containerColor = MaterialTheme.colorScheme.primary,
            contentColor = MaterialTheme.colorScheme.onPrimary,
        ) { Icon(Icons.Default.Add, contentDescription = stringResource(R.string.net_add_cd)) }
    }

    if (adding) {
        GroupDialog(
            initial = null,
            onDismiss = { adding = false },
            onSave = { app.store.upsertGroup(it); adding = false; app.engine.kick() },
        )
    }
    editing?.let { g ->
        GroupDialog(
            initial = g,
            onDismiss = { editing = null },
            onSave = { app.store.upsertGroup(it); editing = null; app.engine.kick() },
        )
    }
    deleting?.let { g ->
        AlertDialog(
            onDismissRequest = { deleting = null },
            title = { Text(stringResource(R.string.net_delete_title)) },
            text = { Text(stringResource(R.string.net_delete_text, g.name, g.accounts.size)) },
            confirmButton = {
                TextButton(onClick = { app.store.deleteGroup(g.id); deleting = null; app.engine.kick() }) {
                    Text(stringResource(R.string.common_delete))
                }
            },
            dismissButton = { TextButton(onClick = { deleting = null }) { Text(stringResource(R.string.common_cancel)) } },
        )
    }
}

private fun lines(s: String) = s.lines().map { it.trim() }.filter { it.isNotEmpty() }

@Composable
private fun GroupDialog(initial: NetworkGroup?, onDismiss: () -> Unit, onSave: (NetworkGroup) -> Unit) {
    var name by remember { mutableStateOf(initial?.name ?: "") }
    var ssids by remember { mutableStateOf(initial?.ssids?.joinToString("\n") ?: "") }
    var urls by remember { mutableStateOf(initial?.portalUrls?.joinToString("\n") ?: "https://") }
    var portalOnly by remember { mutableStateOf(initial?.portalOnly ?: false) }
    var keep by remember { mutableStateOf((initial?.keepaliveSec ?: 180).toString()) }
    var tz by remember { mutableStateOf(initial?.timeZone ?: NetworkGroup.DEFAULT_TZ) }
    var phrases by remember { mutableStateOf(initial?.quotaPhrases?.joinToString("\n") ?: "") }
    var error by remember { mutableStateOf<String?>(null) }

    val errName = stringResource(R.string.err_name)
    val errUrlEmpty = stringResource(R.string.err_url_empty)
    val errUrlHttps = stringResource(R.string.err_url_https)
    val errKeepalive = stringResource(R.string.err_keepalive)
    val errTz = stringResource(R.string.err_tz)
    val errSsid = stringResource(R.string.err_ssid)

    fun trySave() {
        val urlList = lines(urls)
        val keepInt = keep.trim().toIntOrNull()
        error = when {
            name.isBlank() -> errName
            urlList.isEmpty() -> errUrlEmpty
            urlList.any { it.toHttpUrlOrNull()?.isHttps != true } -> errUrlHttps
            keepInt == null || keepInt !in 30..180 -> errKeepalive
            runCatching { ZoneId.of(tz.trim()) }.isFailure -> errTz
            lines(ssids).isEmpty() && !portalOnly -> errSsid
            else -> null
        }
        if (error != null) return
        val base = initial ?: NetworkGroup(name = "", ssids = emptyList(), portalUrls = emptyList())
        onSave(
            base.copy(
                name = name.trim(),
                ssids = lines(ssids),
                portalUrls = urlList,
                portalOnly = portalOnly,
                keepaliveSec = keepInt ?: 180,
                timeZone = tz.trim(),
                quotaPhrases = lines(phrases),
                pins = base.pins.filterKeys { it in urlList || it == base.usageUrl },
            )
        )
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(if (initial == null) R.string.net_dialog_add else R.string.net_dialog_edit)) },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = name, onValueChange = { name = it },
                    label = { Text(stringResource(R.string.net_field_name)) }, singleLine = true,
                )
                OutlinedTextField(
                    value = ssids, onValueChange = { ssids = it },
                    label = { Text(stringResource(R.string.net_field_ssids)) }, minLines = 2,
                )
                OutlinedTextField(
                    value = urls, onValueChange = { urls = it },
                    label = { Text(stringResource(R.string.net_field_urls)) }, minLines = 2,
                )
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(checked = portalOnly, onCheckedChange = { portalOnly = it })
                    Text(stringResource(R.string.net_portal_only_check))
                }
                OutlinedTextField(
                    value = keep, onValueChange = { keep = it },
                    label = { Text(stringResource(R.string.net_field_keepalive)) }, singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                )
                OutlinedTextField(
                    value = tz, onValueChange = { tz = it },
                    label = { Text(stringResource(R.string.net_field_tz)) }, singleLine = true,
                )
                OutlinedTextField(
                    value = phrases, onValueChange = { phrases = it },
                    label = { Text(stringResource(R.string.net_field_phrases)) }, minLines = 2,
                    supportingText = { Text(stringResource(R.string.net_phrases_support)) },
                )
                error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = { TextButton(onClick = { trySave() }) { Text(stringResource(R.string.common_save)) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.common_cancel)) } },
    )
}
