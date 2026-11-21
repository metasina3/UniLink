package ir.unilink.android.ui

import androidx.compose.animation.animateContentSize
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.clickable
import androidx.compose.ui.Alignment
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.LifecycleOwner
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@Composable
fun ScreenTitle(text: String) {
    Text(text, style = MaterialTheme.typography.headlineSmall, color = MaterialTheme.colorScheme.onBackground)
}

/** Soft gradient backdrop with two colour blobs; the glass panels sit on top of it. */
@Composable
fun GlassBackdrop(modifier: Modifier = Modifier, content: @Composable () -> Unit) {
    val g = LocalGlass.current
    Box(
        modifier
            .fillMaxSize()
            .background(Brush.verticalGradient(listOf(g.backdropTop, g.backdropBottom)))
            .drawBehind {
                val a = Offset(size.width * 0.15f, size.height * 0.12f)
                val b = Offset(size.width * 0.9f, size.height * 0.62f)
                val ra = size.maxDimension * 0.55f
                val rb = size.maxDimension * 0.5f
                                drawCircle(
                    Brush.radialGradient(listOf(g.blobA.copy(alpha = g.blobAStrength), Color.Transparent), a, ra),
                    ra, a,
                )
                drawCircle(
                    Brush.radialGradient(listOf(g.blobB.copy(alpha = g.blobBStrength), Color.Transparent), b, rb),
                    rb, b,
                )
            },
    ) { content() }
}

/** A frosted-glass surface: translucent fill and a bright top-left edge. */
@Composable
fun GlassSurface(
    modifier: Modifier = Modifier,
    strong: Boolean = false,
    shape: RoundedCornerShape = RoundedCornerShape(24.dp),
    content: @Composable () -> Unit,
) {
    val g = LocalGlass.current
    Box(
        modifier
            .clip(shape)
            .background(if (strong) g.glassFillStrong else g.glassFill)
            .border(1.dp, Brush.linearGradient(listOf(g.glassEdgeHi, g.glassEdgeLo)), shape),
    ) {
        // Without a Surface/Card nothing sets the text colour, and it would default to black.
        CompositionLocalProvider(LocalContentColor provides MaterialTheme.colorScheme.onSurface) { content() }
    }
}

@Composable
fun Panel(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    GlassSurface(modifier.fillMaxWidth().animateContentSize(softSpring())) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp), content = content)
    }
}

@Composable
fun InfoRow(label: String, value: String) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        Text(label, color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodyMedium)
        Text(value, style = MaterialTheme.typography.bodyMedium)
    }
}

@Composable
fun Tag(text: String, color: Color) {
    Surface(color = color.copy(alpha = 0.18f), shape = RoundedCornerShape(50)) {
        Text(
            text,
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 3.dp),
            color = color,
            style = MaterialTheme.typography.labelMedium,
        )
    }
}

fun formatTime(ts: Long): String =
    if (ts <= 0L) "—" else SimpleDateFormat("HH:mm:ss", Locale.US).format(Date(ts))

/** Increments every time the activity returns to the foreground; use it to re-read permissions. */
@Composable
fun rememberResumeTick(): Int {
    val ctx = LocalContext.current
    var tick by remember { mutableIntStateOf(0) }
    DisposableEffect(ctx) {
        val owner = ctx as? LifecycleOwner
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME) tick++
        }
        owner?.lifecycle?.addObserver(observer)
        onDispose { owner?.lifecycle?.removeObserver(observer) }
    }
    return tick
}

/** Height of the floating tab bar; scrolling screens add it as bottom padding so nothing is cut off. */
val LocalBarPad = compositionLocalOf { 0.dp }

/** A full-width row of equally sized choices (used for language and appearance). */
@Composable
fun ChoiceRow(options: List<Pair<String, String>>, selected: String, onSelect: (String) -> Unit) {
    val g = LocalGlass.current
    val primary = MaterialTheme.colorScheme.primary
    val tick = rememberTick()
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        options.forEach { (code, label) ->
            val on = code == selected
            val src = remember { MutableInteractionSource() }
            val shape = RoundedCornerShape(14.dp)
            Box(
                Modifier
                    .weight(1f)
                    .height(42.dp)
                    .pressScale(src, 0.95f)
                    .clip(shape)
                    .background(if (on) primary.copy(alpha = 0.25f) else Color.Transparent)
                    .border(1.dp, if (on) primary.copy(alpha = 0.6f) else g.glassEdgeHi.copy(alpha = 0.35f), shape)
                    .clickable(interactionSource = src, indication = null) {
                        if (!on) {
                            tick()
                            onSelect(code)
                        }
                    },
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    label,
                    color = if (on) primary else MaterialTheme.colorScheme.onSurfaceVariant,
                    style = MaterialTheme.typography.labelLarge,
                    textAlign = TextAlign.Center,
                    maxLines = 1,
                )
            }
        }
    }
}
