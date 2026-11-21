package ir.unilink.android.ui

import android.os.Build
import androidx.activity.compose.BackHandler
import androidx.annotation.StringRes
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import ir.unilink.android.R

private class TourStep(val icon: ImageVector, @StringRes val title: Int, @StringRes val body: Int)

private val tourSteps = listOf(
    TourStep(Icons.Default.Info, R.string.tour1_title, R.string.tour1_body),
    TourStep(Icons.Default.Person, R.string.tour2_title, R.string.tour2_body),
    TourStep(Icons.Default.Lock, R.string.tour3_title, R.string.tour3_body),
    TourStep(Icons.Default.Check, R.string.tour4_title, R.string.tour4_body),
    TourStep(Icons.Default.Refresh, R.string.tour5_title, R.string.tour5_body),
    TourStep(Icons.Default.Settings, R.string.tour6_title, R.string.tour6_body),
)

/**
 * First-run tour: a few cards on top of the app. Skip (or Back) closes it at any point and it is
 * never shown again; Settings can bring it back.
 */
@Composable
fun TourOverlay(onFinish: () -> Unit) {
    var step by remember { mutableIntStateOf(0) }
    val last = tourSteps.lastIndex
    val primary = MaterialTheme.colorScheme.primary
    val dim = MaterialTheme.colorScheme.onSurfaceVariant
    BackHandler { onFinish() }
    Box(
        Modifier
            .fillMaxSize()
            .background(Color.Black.copy(alpha = 0.62f))
            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { },
        contentAlignment = Alignment.Center,
    ) {
        GlassSurface(Modifier.padding(20.dp).fillMaxWidth(), strong = true) {
            Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        stringResource(R.string.tour_progress, step + 1, tourSteps.size),
                        style = MaterialTheme.typography.bodySmall,
                        color = dim,
                        modifier = Modifier.weight(1f),
                    )
                    TextButton(onClick = onFinish) { Text(stringResource(R.string.tour_skip)) }
                }
                AnimatedContent(
                    targetState = step,
                    transitionSpec = { fadeIn(tween(220)) togetherWith fadeOut(tween(120)) },
                    label = "tourStep",
                ) { i ->
                    val s = tourSteps[i]
                    Column(
                        Modifier.fillMaxWidth(),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        Box(
                            Modifier.size(64.dp).clip(CircleShape).background(primary.copy(alpha = 0.2f)),
                            contentAlignment = Alignment.Center,
                        ) { Icon(s.icon, contentDescription = null, tint = primary, modifier = Modifier.size(32.dp)) }
                        Text(stringResource(s.title), style = MaterialTheme.typography.titleLarge, textAlign = TextAlign.Center)
                        Text(
                            stringResource(s.body),
                            style = MaterialTheme.typography.bodyMedium,
                            color = dim,
                            textAlign = TextAlign.Center,
                        )
                    }
                }
                Row(
                    Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(6.dp, Alignment.CenterHorizontally),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    for (i in 0..last) {
                        Box(
                            Modifier.size(if (i == step) 10.dp else 6.dp).clip(CircleShape)
                                .background(if (i == step) primary else dim.copy(alpha = 0.35f)),
                        )
                    }
                }
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    if (step > 0) {
                        OutlinedButton(onClick = { step-- }, modifier = Modifier.weight(1f)) {
                            Text(stringResource(R.string.tour_back))
                        }
                    }
                    Button(
                        onClick = { if (step == last) onFinish() else step++ },
                        modifier = Modifier.weight(1f),
                    ) { Text(stringResource(if (step == last) R.string.tour_done else R.string.tour_next)) }
                }
            }
        }
    }
}

private class Brand(val key: String, val name: String, @StringRes val steps: Int, val match: List<String>)

private val brands = listOf(
    Brand("samsung", "Samsung", R.string.batt_samsung, listOf("samsung")),
    Brand("xiaomi", "Xiaomi · Redmi · POCO", R.string.batt_xiaomi, listOf("xiaomi", "redmi", "poco")),
    Brand("huawei", "Huawei · Honor", R.string.batt_huawei, listOf("huawei", "honor")),
    Brand("oppo", "Oppo · Realme · OnePlus", R.string.batt_oppo, listOf("oppo", "realme", "oneplus")),
    Brand("vivo", "Vivo · iQOO", R.string.batt_vivo, listOf("vivo", "iqoo")),
    Brand("other", "Pixel · Motorola · others", R.string.batt_other, listOf("google", "motorola", "nokia", "sony", "asus")),
)

/** Per-brand steps for letting UniLink run in the background; the phone's own brand opens first. */
@Composable
fun BatteryGuidePanel() {
    val ctx = LocalContext.current
    val maker = remember { Build.MANUFACTURER.lowercase() }
    val mine = remember { brands.firstOrNull { b -> b.match.any { it in maker } } ?: brands.last() }
    val ordered = remember { listOf(mine) + brands.filter { it !== mine } }
    var open by remember { mutableStateOf(mine.key) }
    Panel {
        Text(stringResource(R.string.batt_guide_title), style = MaterialTheme.typography.titleMedium)
        Text(
            stringResource(R.string.batt_guide_intro),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        ordered.forEach { b ->
            val isOpen = open == b.key
            Column(
                Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(12.dp))
                    .clickable { open = if (isOpen) "" else b.key }
                    .padding(vertical = 8.dp),
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        if (b === mine) b.name + "  ·  " + stringResource(R.string.batt_your_phone) else b.name,
                        style = MaterialTheme.typography.titleSmall,
                        modifier = Modifier.weight(1f),
                    )
                    Text(if (isOpen) "−" else "+", style = MaterialTheme.typography.titleMedium)
                }
                if (isOpen) {
                    Text(
                        stringResource(b.steps),
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(top = 6.dp),
                    )
                }
            }
        }
        OutlinedButton(onClick = { openBatterySettings(ctx) }) {
            Text(stringResource(R.string.batt_open_settings))
        }
        Text(
            stringResource(R.string.batt_guide_note),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
