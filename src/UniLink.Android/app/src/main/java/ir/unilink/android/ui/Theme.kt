package ir.unilink.android.ui

import android.app.Activity
import android.content.Context
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color

/** Colors that are not part of Material's scheme: the glass look and the status palette. */
data class GlassColors(
    val dark: Boolean,
    val backdropTop: Color,
    val backdropBottom: Color,
    val blobA: Color,
    val blobB: Color,
    val glassFill: Color,
    val glassFillStrong: Color,
    val glassEdgeHi: Color,
    val glassEdgeLo: Color,
    val green: Color,
    val amber: Color,
    val red: Color,
    val grey: Color,
    val blue: Color,
    val blobAStrength: Float,
    val blobBStrength: Float,
)

private val DarkGlass = GlassColors(
    dark = true,
    backdropTop = Color(0xFF0B1220),
    backdropBottom = Color(0xFF101A33),
    blobA = Color(0xFF3B6FE0),
    blobB = Color(0xFF8A5BE8),
    glassFill = Color(0x14FFFFFF),
    glassFillStrong = Color(0x24FFFFFF),
    glassEdgeHi = Color(0x66FFFFFF),
    glassEdgeLo = Color(0x0FFFFFFF),
    green = Color(0xFF3DDC84),
    amber = Color(0xFFFFB74D),
    red = Color(0xFFFF6B6B),
    grey = Color(0xFF8A97AD),
    blue = Color(0xFF6FA8FF),
    blobAStrength = 0.5f,
    blobBStrength = 0.4f,
)

private val LightGlass = GlassColors(
    dark = false,
    backdropTop = Color(0xFFE9F0FF),
    backdropBottom = Color(0xFFF6F1FF),
    blobA = Color(0xFF8DB4FF),
    blobB = Color(0xFFC9A8FF),
    glassFill = Color(0x80FFFFFF),
    glassFillStrong = Color(0xB3FFFFFF),
    glassEdgeHi = Color(0xE6FFFFFF),
    glassEdgeLo = Color(0x4D3A5FA8),
    green = Color(0xFF0E9F55),
    amber = Color(0xFFC77700),
    red = Color(0xFFD93A3A),
    grey = Color(0xFF5E6B82),
    blue = Color(0xFF2A62D9),
    blobAStrength = 0.6f,
    blobBStrength = 0.48f,
)

/** Deeper, near-black version of the dark look with stronger blue and violet glow. */
private val MidnightGlass = GlassColors(
    dark = true,
    backdropTop = Color(0xFF000000),
    backdropBottom = Color(0xFF000000),
    blobA = Color(0xFF2458FF),
    blobB = Color(0xFF8B3CFF),
    glassFill = Color(0xA60A0D18),
    glassFillStrong = Color(0xE60B0E1A),
    glassEdgeHi = Color(0x66A9BEFF),
    glassEdgeLo = Color(0x1AFFFFFF),
    green = Color(0xFF5BF5AE),
    amber = Color(0xFFFFC857),
    red = Color(0xFFFF5C7A),
    grey = Color(0xFF7C88A6),
    blue = Color(0xFF7DB2FF),
    blobAStrength = 0.8f,
    blobBStrength = 0.16f,
)

val LocalGlass = staticCompositionLocalOf { DarkGlass }

val StatusGreen: Color @Composable get() = LocalGlass.current.green
val StatusAmber: Color @Composable get() = LocalGlass.current.amber
val StatusRed: Color @Composable get() = LocalGlass.current.red
val StatusGrey: Color @Composable get() = LocalGlass.current.grey
val AccentBlue: Color @Composable get() = LocalGlass.current.blue

private val DarkScheme = darkColorScheme(
    primary = Color(0xFF6FA8FF),
    onPrimary = Color(0xFF00255C),
    primaryContainer = Color(0xFF1F3F7A),
    onPrimaryContainer = Color(0xFFD8E6FF),
    secondary = Color(0xFF9DB8E8),
    background = Color(0xFF0B1220),
    onBackground = Color(0xFFE9EEF7),
    surface = Color(0xFF0B1220),
    onSurface = Color(0xFFE9EEF7),
    surfaceVariant = Color(0xFF18233A),
    onSurfaceVariant = Color(0xFFB4BFD2),
    surfaceContainer = Color(0xFF16213A),
    surfaceContainerHigh = Color(0xFF1B2742),
    surfaceContainerHighest = Color(0xFF21304F),
    outline = Color(0xFF3A4A68),
    outlineVariant = Color(0x33FFFFFF),
    error = Color(0xFFFF6B6B),
)

private val LightScheme = lightColorScheme(
    primary = Color(0xFF2A62D9),
    onPrimary = Color(0xFFFFFFFF),
    primaryContainer = Color(0xFFD8E6FF),
    onPrimaryContainer = Color(0xFF0A2A66),
    secondary = Color(0xFF4A5F8A),
    background = Color(0xFFE9F0FF),
    onBackground = Color(0xFF111A2B),
    surface = Color(0xFFF3F6FD),
    onSurface = Color(0xFF111A2B),
    surfaceVariant = Color(0xFFE2E9F6),
    onSurfaceVariant = Color(0xFF45526B),
    surfaceContainer = Color(0xFFEAF0FB),
    surfaceContainerHigh = Color(0xFFE2EAF8),
    surfaceContainerHighest = Color(0xFFD9E3F5),
    outline = Color(0xFF8593AC),
    outlineVariant = Color(0x33000000),
    error = Color(0xFFD93A3A),
)

private val MidnightScheme = DarkScheme.copy(
    primary = Color(0xFF7DB2FF),
    onPrimary = Color(0xFF001B4D),
    primaryContainer = Color(0xFF16336E),
    background = Color(0xFF000000),
    surface = Color(0xFF000000),
    surfaceVariant = Color(0xFF111628),
    outlineVariant = Color(0x55B8C8FF),
    outline = Color(0xFF5D6C94),
    surfaceContainer = Color(0xFF0D1224),
    surfaceContainerHigh = Color(0xFF121933),
    surfaceContainerHighest = Color(0xFF18213F),
)

/** Look chosen in Settings. Kept in SharedPreferences and observable, so switching is instant. */
object ThemePrefs {
    const val SYSTEM = "system"
    const val LIGHT = "light"
    const val DARK = "dark"
    const val MIDNIGHT = "midnight"
    private const val FILE = "ui_prefs"
    private const val KEY = "theme"

    var mode by mutableStateOf(SYSTEM)
        private set

    fun load(ctx: Context) {
        mode = runCatching { ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE).getString(KEY, SYSTEM) }
            .getOrNull() ?: SYSTEM
    }

    fun set(ctx: Context, value: String) {
        mode = value
        runCatching { ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE).edit().putString(KEY, value).apply() }
    }
}

@Composable
fun UniLinkTheme(content: @Composable () -> Unit) {
    val system = isSystemInDarkTheme()
    val mode = ThemePrefs.mode
    val dark = when (mode) {
        ThemePrefs.LIGHT -> false
        ThemePrefs.DARK, ThemePrefs.MIDNIGHT -> true
        else -> system
    }
    val glass = when {
        mode == ThemePrefs.MIDNIGHT -> MidnightGlass
        dark -> DarkGlass
        else -> LightGlass
    }
    val scheme = when {
        mode == ThemePrefs.MIDNIGHT -> MidnightScheme
        dark -> DarkScheme
        else -> LightScheme
    }
    // Make the status/navigation bar icons match the chosen look, not only the phone's setting.
    val view = LocalView.current
    SideEffect {
        val window = (view.context as? Activity)?.window ?: return@SideEffect
        val c = WindowCompat.getInsetsController(window, view)
        c.isAppearanceLightStatusBars = !dark
        c.isAppearanceLightNavigationBars = !dark
    }
    CompositionLocalProvider(LocalGlass provides glass) {
        MaterialTheme(colorScheme = scheme, content = content)
    }
}
