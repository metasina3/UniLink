package ir.unilink.android.ui

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.hapticfeedback.HapticFeedback
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay

/** Shared spring used for size/position changes so everything moves with the same feel. */
fun <T> softSpring() = spring<T>(dampingRatio = 0.8f, stiffness = Spring.StiffnessMediumLow)

/** Slightly bouncy spring for things that should "pop" (badge, selected tab). */
fun <T> bouncySpring() = spring<T>(dampingRatio = 0.55f, stiffness = Spring.StiffnessMedium)

/** Shrinks the element a little while it is pressed and springs back on release. */
@Composable
fun Modifier.pressScale(source: MutableInteractionSource, pressedScale: Float = 0.95f): Modifier {
    val pressed by source.collectIsPressedAsState()
    val s by animateFloatAsState(
        targetValue = if (pressed) pressedScale else 1f,
        animationSpec = spring(dampingRatio = 0.5f, stiffness = Spring.StiffnessMedium),
        label = "pressScale",
    )
    return graphicsLayer {
        scaleX = s
        scaleY = s
    }
}

/** Fades and lifts the element in, [index] steps after the first one. Plays once per composition. */
@Composable
fun Modifier.staggerIn(index: Int): Modifier {
    val p = remember { Animatable(0f) }
    LaunchedEffect(Unit) {
        delay(index * 45L)
        p.animateTo(1f, spring(dampingRatio = 0.85f, stiffness = Spring.StiffnessLow))
    }
    return graphicsLayer {
        alpha = p.value.coerceIn(0f, 1f)
        translationY = (1f - p.value) * 28.dp.toPx()
    }
}

/** A light tick; used for tab changes, button presses and toggles. */
fun HapticFeedback.tick() = performHapticFeedback(HapticFeedbackType.TextHandleMove)

@Composable
fun rememberTick(): () -> Unit {
    val haptic = LocalHapticFeedback.current
    return remember(haptic) { { haptic.tick() } }
}
