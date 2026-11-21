package ir.unilink.android.ui

import androidx.activity.compose.BackHandler
import androidx.annotation.StringRes
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.graphics.BlendMode
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.CompositingStrategy
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.graphics.graphicsLayer
import kotlinx.coroutines.launch
import kotlin.math.abs
import androidx.compose.animation.expandHorizontally
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkHorizontally
import androidx.compose.animation.animateColorAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.List
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.Place
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import ir.unilink.android.R
import ir.unilink.android.UniLinkApp

/** Material "wifi" glyph (Apache-2.0 path data); the core icon set has none. */
private val WifiIcon: ImageVector by lazy {
    ImageVector.Builder(
        name = "Wifi", defaultWidth = 24.dp, defaultHeight = 24.dp, viewportWidth = 24f, viewportHeight = 24f,
    ).addPath(
        pathData = PathParser().parsePathString(
            "M1 9l2 2c4.97-4.97 13.03-4.97 18 0l2-2C16.93 2.93 7.08 2.93 1 9zm8 8l3 3 3-3c-1.65-1.66-4.34-1.66-6 0zm-4-4l2 2c2.76-2.76 7.24-2.76 10 0l2-2C15.14 9.14 8.87 9.14 5 13z",
        ).toNodes(),
        fill = SolidColor(Color.Black),
    ).build()
}

private data class TabItem(@StringRes val label: Int, val icon: ImageVector)

private val tabs = listOf(
    TabItem(R.string.tab_connection, WifiIcon),
    TabItem(R.string.tab_accounts, Icons.Default.Person),
    TabItem(R.string.tab_networks, Icons.Default.Place),
    TabItem(R.string.tab_activity, Icons.AutoMirrored.Filled.List),
    TabItem(R.string.tab_settings, Icons.Default.Settings),
)

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun AppRoot(app: UniLinkApp) {
    val pager = rememberPagerState(initialPage = 0, pageCount = { tabs.size })
    val scope = rememberCoroutineScope()
    val tick = rememberTick()
    val selected = pager.targetPage
    val appData by app.store.data.collectAsState()

    fun goTo(i: Int) {
        scope.launch { pager.animateScrollToPage(i, animationSpec = spring(dampingRatio = 0.85f, stiffness = Spring.StiffnessLow)) }
    }

    // Back from any other tab returns to the first one before the app closes.
    BackHandler(enabled = pager.currentPage != 0) { goTo(0) }

    GlassBackdrop {
        Scaffold(
            containerColor = Color.Transparent,
            contentColor = MaterialTheme.colorScheme.onBackground,
            bottomBar = {
                Box(Modifier.navigationBarsPadding().padding(horizontal = 12.dp, vertical = 8.dp)) {
                    val g = LocalGlass.current
                    Box(Modifier.fillMaxWidth().clip(RoundedCornerShape(30.dp)).background(g.backdropBottom.copy(alpha = 0.94f))) {
                    GlassSurface(Modifier.fillMaxWidth(), strong = true, shape = RoundedCornerShape(30.dp)) {
                        Row(
                            Modifier.fillMaxWidth().height(64.dp).padding(horizontal = 6.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(4.dp),
                        ) {
                            tabs.forEachIndexed { i, t ->
                                NavPill(
                                    item = t,
                                    selected = selected == i,
                                    modifier = Modifier.weight(
                                        animateFloatAsState(
                                            if (selected == i) 2.1f else 1f,
                                            bouncySpring(),
                                            label = "tabWeight",
                                        ).value,
                                    ),
                                ) {
                                    if (selected != i) {
                                        tick()
                                        goTo(i)
                                    }
                                }
                            }
                        }
                    }
                    }
                }
            },
        ) { padding ->
            // Swipe left/right between tabs; the page under your finger shrinks and fades slightly.
            HorizontalPager(
                state = pager,
                modifier = Modifier
                    .padding(top = padding.calculateTopPadding())
                    .fillMaxSize()
                    .graphicsLayer { compositingStrategy = CompositingStrategy.Offscreen }
                    .drawWithContent {
                        drawContent()
                        val f = 16.dp.toPx() / size.height
                        drawRect(
                            Brush.verticalGradient(0f to Color.Transparent, f to Color.Black, 1f to Color.Black),
                            blendMode = BlendMode.DstIn,
                        )
                    },
                beyondViewportPageCount = tabs.size,
                key = { it },
            ) { page ->
                Box(
                    Modifier.fillMaxSize().graphicsLayer {
                        val off = abs((pager.currentPage - page) + pager.currentPageOffsetFraction).coerceIn(0f, 1f)
                        alpha = 1f - 0.55f * off
                        val sc = 1f - 0.06f * off
                        scaleX = sc
                        scaleY = sc
                    },
                ) {
                    CompositionLocalProvider(LocalBarPad provides padding.calculateBottomPadding()) {
                        when (page) {
                            0 -> ConnectionScreen(app) { goTo(it) }
                            1 -> AccountsScreen(app) { goTo(it) }
                            2 -> NetworksScreen(app)
                            3 -> ActivityScreen(app)
                            else -> SettingsScreen(app)
                        }
                    }
                }
            }
        }
        if (!appData.settings.tourDone) {
            TourOverlay { app.store.updateSettings { it.copy(tourDone = true) } }
        }
    }
}

/** One tab: an icon, and a label that slides out next to it while the tab is selected. */
@Composable
private fun NavPill(item: TabItem, selected: Boolean, modifier: Modifier, onClick: () -> Unit) {
    val label = stringResource(item.label)
    val primary = MaterialTheme.colorScheme.primary
    val bg by animateColorAsState(
        if (selected) primary.copy(alpha = 0.22f) else Color.Transparent,
        tween(220),
        label = "pillBg",
    )
    val fg by animateColorAsState(
        if (selected) primary else MaterialTheme.colorScheme.onSurfaceVariant,
        tween(220),
        label = "pillFg",
    )
    val source = remember { MutableInteractionSource() }
    Box(
        modifier
            .height(48.dp)
            .clip(RoundedCornerShape(24.dp))
            .background(bg)
            .selectable(selected = selected, interactionSource = source, indication = null, role = Role.Tab, onClick = onClick)
            .pressScale(source, 0.92f),
        contentAlignment = Alignment.Center,
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(item.icon, contentDescription = if (selected) null else label, tint = fg, modifier = Modifier.size(24.dp))
            AnimatedVisibility(
                visible = selected,
                enter = fadeIn(tween(200, delayMillis = 60)) + expandHorizontally(bouncySpring()),
                exit = fadeOut(tween(90)) + shrinkHorizontally(softSpring()),
            ) {
                Text(
                    label,
                    color = fg,
                    maxLines = 1,
                    overflow = TextOverflow.Clip,
                    style = MaterialTheme.typography.labelLarge,
                    modifier = Modifier.padding(start = 6.dp),
                )
            }
        }
    }
}
