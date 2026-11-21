package ir.unilink.android.ui

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ir.unilink.android.BuildConfig
import ir.unilink.android.R
import ir.unilink.android.UpdateCheck
import kotlinx.coroutines.launch

private fun openUrl(ctx: Context, url: String) {
    runCatching {
        ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    }
}

private sealed interface UpdateUi {
    data object Idle : UpdateUi
    data object Checking : UpdateUi
    data class Done(val result: UpdateCheck.Result) : UpdateUi
}

/** Version, author, a link to the GitHub project, and a manual "Check for update" button. */
@Composable
fun AboutPanel() {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    val tick = rememberTick()
    var ui by remember { mutableStateOf<UpdateUi>(UpdateUi.Idle) }

    Panel {
        Text(stringResource(R.string.set_about_title), style = MaterialTheme.typography.titleMedium)
        Text(
            stringResource(R.string.set_about_text, BuildConfig.VERSION_NAME),
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        InfoRow(stringResource(R.string.about_author), UpdateCheck.OWNER)

        AnimatedContent(
            targetState = ui,
            transitionSpec = { fadeIn() togetherWith fadeOut() },
            label = "updateStatus",
        ) { state ->
            val (text, color) = when (state) {
                UpdateUi.Idle -> "" to MaterialTheme.colorScheme.onSurfaceVariant
                UpdateUi.Checking -> stringResource(R.string.upd_checking) to MaterialTheme.colorScheme.onSurfaceVariant
                is UpdateUi.Done -> when (val r = state.result) {
                    is UpdateCheck.Result.UpToDate -> stringResource(R.string.upd_up_to_date, r.current) to StatusGreen
                    is UpdateCheck.Result.Available -> stringResource(R.string.upd_available, r.latest) to AccentBlue
                    is UpdateCheck.Result.Failed -> stringResource(R.string.upd_failed, r.reason) to StatusAmber
                }
            }
            if (text.isNotEmpty()) Text(text, color = color, style = MaterialTheme.typography.bodyMedium)
        }

        Row(
            Modifier.fillMaxWidth().padding(top = 6.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            val available = (ui as? UpdateUi.Done)?.result as? UpdateCheck.Result.Available
            if (available != null) {
                Button(
                    onClick = { tick(); openUrl(ctx, available.url) },
                    modifier = Modifier.weight(1f).height(44.dp),
                    shape = RoundedCornerShape(22.dp),
                ) { Text(stringResource(R.string.upd_download)) }
            } else {
                Button(
                    onClick = {
                        tick()
                        if (ui != UpdateUi.Checking) {
                            ui = UpdateUi.Checking
                            scope.launch { ui = UpdateUi.Done(UpdateCheck.check(BuildConfig.VERSION_NAME)) }
                        }
                    },
                    enabled = ui != UpdateUi.Checking,
                    modifier = Modifier.weight(1f).height(44.dp),
                    shape = RoundedCornerShape(22.dp),
                ) { Text(stringResource(R.string.upd_check)) }
            }
            OutlinedButton(
                onClick = { tick(); openUrl(ctx, UpdateCheck.REPO_URL) },
                modifier = Modifier.weight(1f).height(44.dp),
                shape = RoundedCornerShape(22.dp),
            ) { Text(stringResource(R.string.about_github)) }
        }
        Text(
            stringResource(R.string.upd_privacy),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
