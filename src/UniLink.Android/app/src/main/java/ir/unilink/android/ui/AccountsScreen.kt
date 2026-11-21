package ir.unilink.android.ui

import androidx.compose.foundation.horizontalScroll
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
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import android.text.format.DateUtils
import ir.unilink.android.UsageInfo
import ir.unilink.android.formatMb
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Button
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.background
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import ir.unilink.android.Account
import ir.unilink.android.NetworkGroup
import ir.unilink.android.R
import ir.unilink.android.UniLinkApp
import ir.unilink.android.isExhaustedNow
import ir.unilink.android.isUsable
import ir.unilink.android.moved

private enum class AccountAction { Edit, Delete, SetDefault, Up, Down, Toggle, ClearBlock }

@Composable
fun AccountsScreen(app: UniLinkApp, goToTab: (Int) -> Unit) {
    val data by app.store.data.collectAsState()
    var selectedId by rememberSaveable { mutableStateOf<String?>(null) }
    val group = data.groups.firstOrNull { it.id == selectedId } ?: data.groups.firstOrNull()

    Column(Modifier.fillMaxSize().padding(horizontal = 16.dp).padding(top = 16.dp)) {
        ScreenTitle(stringResource(R.string.tab_accounts))
        if (group == null) {
            Text(
                stringResource(R.string.acc_no_groups),
                modifier = Modifier.padding(top = 12.dp),
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        } else {
            if (data.groups.size > 1) {
                Row(
                    Modifier.padding(top = 8.dp).horizontalScroll(rememberScrollState()),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    data.groups.forEach { g ->
                        FilterChip(
                            selected = g.id == group.id,
                            onClick = { selectedId = g.id },
                            label = { Text(g.name) },
                        )
                    }
                }
            }
            AccountsContent(app, group, goToTab)
        }
    }
}

@Composable
private fun AccountsContent(app: UniLinkApp, group: NetworkGroup, goToTab: (Int) -> Unit) {
    val status by app.engine.status.collectAsState()
    val busy by app.engine.usageBusy.collectAsState()
    val usageErrors by app.engine.usageErrors.collectAsState()
    val usageCert by app.engine.usageCert.collectAsState()
    var editing by remember { mutableStateOf<Account?>(null) }
    var adding by remember { mutableStateOf(false) }
    var deleting by remember { mutableStateOf<Account?>(null) }
    val now = System.currentTimeMillis()
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val switchOnUse = app.store.data.collectAsState().value.settings.switchOnUse

    fun use(acc: Account) {
        if (switchOnUse) {
            ir.unilink.android.KeepAliveService.start(ctx)
            app.engine.useAccount(group.id, acc.id)
            goToTab(0)
        } else {
            val i = group.accounts.indexOfFirst { it.id == acc.id }
            app.store.updateGroup(group.id) { g -> g.copy(accounts = g.accounts.moved(i, 0)) }
            app.engine.kick()
        }
    }

    fun act(acc: Account, action: AccountAction) {
        val i = group.accounts.indexOfFirst { it.id == acc.id }
        when (action) {
            AccountAction.Edit -> editing = acc
            AccountAction.Delete -> deleting = acc
            AccountAction.SetDefault ->
                app.store.updateGroup(group.id) { g -> g.copy(accounts = g.accounts.moved(i, 0)) }
            AccountAction.Up ->
                app.store.updateGroup(group.id) { g -> g.copy(accounts = g.accounts.moved(i, i - 1)) }
            AccountAction.Down ->
                app.store.updateGroup(group.id) { g -> g.copy(accounts = g.accounts.moved(i, i + 1)) }
            AccountAction.Toggle ->
                app.store.updateAccount(group.id, acc.id) { it.copy(enabled = !it.enabled) }
            AccountAction.ClearBlock ->
                app.store.updateAccount(group.id, acc.id) {
                    it.copy(passwordRejected = false, exhaustedMonth = null, exhaustedAt = 0L)
                }
        }
        app.engine.kick()
    }

    if (group.usageUrl.isNotBlank() && group.accounts.isNotEmpty()) {
        Row(Modifier.fillMaxWidth().padding(top = 4.dp), horizontalArrangement = Arrangement.End) {
            TextButton(
                onClick = { app.engine.refreshUsage(group.id) },
                enabled = busy.isEmpty(),
            ) {
                Icon(Icons.Default.Refresh, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(6.dp))
                Text(stringResource(R.string.usage_refresh))
            }
        }
    }
    usageCert?.takeIf { it.groupId == group.id }?.let { p ->
        Box(Modifier.padding(top = 4.dp, bottom = 4.dp)) {
            CertificateCard(p) { app.engine.approveUsageCertificate(p) }
        }
    }

    Box(Modifier.fillMaxSize()) {
        if (group.accounts.isEmpty()) {
            Text(
                stringResource(R.string.acc_none, group.name),
                modifier = Modifier.padding(top = 16.dp),
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        LazyColumn(
            Modifier.fillMaxSize(),
            contentPadding = PaddingValues(top = 12.dp, bottom = 88.dp + LocalBarPad.current),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            items(group.accounts, key = { it.id }) { acc ->
                val index = group.accounts.indexOfFirst { it.id == acc.id }
                Panel(Modifier.animateItem()) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Box(
                            Modifier.size(44.dp).background(AccentBlue.copy(alpha = 0.20f), CircleShape),
                            contentAlignment = Alignment.Center,
                        ) {
                            Text(
                                acc.name.trim().take(2).uppercase().ifEmpty { "?" },
                                color = AccentBlue,
                                style = MaterialTheme.typography.titleSmall,
                            )
                        }
                        Spacer(Modifier.width(12.dp))
                        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                Text(acc.name, style = MaterialTheme.typography.titleMedium)
                                if (index == 0) Tag(stringResource(R.string.tag_default), AccentBlue)
                            }
                            Text(
                                acc.username,
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                        RowMenu(
                            index = index,
                            count = group.accounts.size,
                            enabled = acc.enabled,
                            blocked = acc.passwordRejected || acc.isExhaustedNow(group, now),
                            onAction = { act(acc, it) },
                        )
                    }
                    if (status.accountId == acc.id || !acc.enabled || acc.passwordRejected || acc.isExhaustedNow(group, now)) Row(
                        Modifier.horizontalScroll(rememberScrollState()),
                        horizontalArrangement = Arrangement.spacedBy(6.dp),
                    ) {
                        if (status.accountId == acc.id) Tag(stringResource(R.string.tag_in_use), StatusGreen)
                        if (!acc.enabled) Tag(stringResource(R.string.tag_disabled), StatusGrey)
                        if (acc.passwordRejected) Tag(stringResource(R.string.tag_password_rejected), StatusRed)
                        if (acc.isExhaustedNow(group, now)) Tag(stringResource(R.string.tag_quota_used), StatusAmber)
                    }
                    if (group.usageUrl.isNotBlank()) UsageBlock(
                        usage = acc.usage,
                        loading = acc.id in busy,
                        error = usageErrors[acc.id],
                        onRefresh = { app.engine.refreshUsage(group.id, acc.id) },
                    )
                    if (acc.passwordRejected) {
                        Button(
                            onClick = { editing = acc },
                            colors = androidx.compose.material3.ButtonDefaults.buttonColors(containerColor = StatusRed),
                        ) { Text(stringResource(R.string.acc_fix_password)) }
                    } else if (acc.isUsable(group, now) && status.accountId != acc.id) {
                        OutlinedButton(onClick = { use(acc) }) { Text(stringResource(R.string.acc_use)) }
                    }
                }
            }
        }
        FloatingActionButton(
            onClick = { adding = true },
            modifier = Modifier.align(Alignment.BottomEnd).padding(bottom = 16.dp + LocalBarPad.current),
            containerColor = MaterialTheme.colorScheme.primary,
            contentColor = MaterialTheme.colorScheme.onPrimary,
        ) { Icon(Icons.Default.Add, contentDescription = stringResource(R.string.acc_add_cd)) }
    }

    if (adding) {
        AccountDialog(
            initial = null,
            onDismiss = { adding = false },
            onSave = {
                app.store.upsertAccount(group.id, it)
                adding = false
                app.engine.kick()
            },
        )
    }
    editing?.let { acc ->
        AccountDialog(
            initial = acc,
            onDismiss = { editing = null },
            onSave = {
                app.store.upsertAccount(group.id, it)
                editing = null
                app.engine.kick()
            },
        )
    }
    deleting?.let { acc ->
        AlertDialog(
            onDismissRequest = { deleting = null },
            title = { Text(stringResource(R.string.acc_delete_title)) },
            text = { Text(stringResource(R.string.acc_delete_text, acc.name, group.name)) },
            confirmButton = {
                TextButton(onClick = {
                    app.store.updateGroup(group.id) { g -> g.copy(accounts = g.accounts.filterNot { it.id == acc.id }) }
                    deleting = null
                    app.engine.kick()
                }) { Text(stringResource(R.string.common_delete)) }
            },
            dismissButton = { TextButton(onClick = { deleting = null }) { Text(stringResource(R.string.common_cancel)) } },
        )
    }
}

@Composable
private fun RowMenu(
    index: Int,
    count: Int,
    enabled: Boolean,
    blocked: Boolean,
    onAction: (AccountAction) -> Unit,
) {
    var open by remember { mutableStateOf(false) }
    Box {
        IconButton(onClick = { open = true }) {
            Icon(Icons.Default.MoreVert, contentDescription = stringResource(R.string.acc_more_cd))
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            @Composable
            fun item(label: String, action: AccountAction) = DropdownMenuItem(
                text = { Text(label) },
                onClick = { open = false; onAction(action) },
            )
            item(stringResource(R.string.menu_edit), AccountAction.Edit)
            if (index > 0) item(stringResource(R.string.menu_set_default), AccountAction.SetDefault)
            if (index > 0) item(stringResource(R.string.menu_move_up), AccountAction.Up)
            if (index < count - 1) item(stringResource(R.string.menu_move_down), AccountAction.Down)
            item(
                stringResource(if (enabled) R.string.menu_disable else R.string.menu_enable),
                AccountAction.Toggle,
            )
            if (blocked) item(stringResource(R.string.menu_clear_block), AccountAction.ClearBlock)
            item(stringResource(R.string.common_delete), AccountAction.Delete)
        }
    }
}

@Composable
private fun AccountDialog(initial: Account?, onDismiss: () -> Unit, onSave: (Account) -> Unit) {
    var name by remember { mutableStateOf(initial?.name ?: "") }
    var user by remember { mutableStateOf(initial?.username ?: "") }
    var pass by remember { mutableStateOf(initial?.password ?: "") }
    var show by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(if (initial == null) R.string.acc_dialog_add else R.string.acc_dialog_edit)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = name, onValueChange = { name = it },
                    label = { Text(stringResource(R.string.acc_field_name)) }, singleLine = true,
                )
                OutlinedTextField(
                    value = user, onValueChange = { user = it },
                    label = { Text(stringResource(R.string.acc_field_user)) }, singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Ascii),
                )
                OutlinedTextField(
                    value = pass, onValueChange = { pass = it },
                    label = { Text(stringResource(R.string.acc_field_pass)) }, singleLine = true,
                    visualTransformation = if (show) VisualTransformation.None else PasswordVisualTransformation(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                    trailingIcon = {
                        TextButton(onClick = { show = !show }) {
                            Text(stringResource(if (show) R.string.acc_hide else R.string.acc_show))
                        }
                    },
                )
            }
        },
        confirmButton = {
            TextButton(
                enabled = user.isNotBlank() && pass.isNotEmpty(),
                onClick = {
                    val base = initial ?: Account(name = "", username = "", password = "")
                    val credsChanged = initial != null &&
                        (initial.username != user.trim() || initial.password != pass)
                    onSave(
                        base.copy(
                            name = name.trim().ifEmpty { user.trim() },
                            username = user.trim(),
                            password = pass,
                            passwordRejected = if (credsChanged) false else base.passwordRejected,
                        )
                    )
                },
            ) { Text(stringResource(R.string.common_save)) }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.common_cancel)) } },
    )
}


@Composable
internal fun UsageBlock(usage: UsageInfo?, loading: Boolean, error: String?, onRefresh: () -> Unit) {
    Column(Modifier.padding(top = 10.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        if (usage == null) {
            Text(
                stringResource(if (loading) R.string.usage_loading else R.string.usage_never),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        } else {
            // The bar shows what is LEFT: it shrinks as data is used and turns amber, then red.
            val leftShare = usage.fraction?.let { 1f - it }
            val shown by animateFloatAsState(leftShare ?: 0f, label = "usageBar")
            val barColor = when {
                leftShare == null -> StatusGreen
                leftShare > 0.30f -> StatusGreen
                leftShare > 0.10f -> StatusAmber
                else -> StatusRed
            }
            if (leftShare != null) {
                LinearProgressIndicator(
                    progress = { shown },
                    modifier = Modifier.fillMaxWidth().height(8.dp).clip(RoundedCornerShape(4.dp)),
                    color = barColor,
                    trackColor = barColor.copy(alpha = 0.18f),
                )
            }
            val line = if (usage.allottedMb != null && usage.remainingMb != null)
                stringResource(R.string.usage_left, formatMb(usage.remainingMb), formatMb(usage.allottedMb))
            else stringResource(R.string.usage_unlimited, formatMb(usage.usedMb))
            Text(line, style = MaterialTheme.typography.bodyMedium)
            val age = System.currentTimeMillis() - usage.fetchedAt
            val updated = if (age < 60_000L) stringResource(R.string.usage_just_now)
            else DateUtils.getRelativeTimeSpanString(
                usage.fetchedAt, System.currentTimeMillis(), DateUtils.MINUTE_IN_MILLIS
            ).toString()
            val tail = buildString {
                if (usage.renewal.isNotBlank()) append(stringResource(R.string.usage_renews, usage.renewal)).append("  ·  ")
                append(stringResource(R.string.usage_updated, updated))
            }
            Text(tail, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        if (loading && usage != null) {
            Text(stringResource(R.string.usage_loading), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        if (error != null && !loading) {
            Text(
                stringResource(R.string.usage_error, error),
                style = MaterialTheme.typography.bodySmall,
                color = StatusAmber,
            )
        }
    }
}
