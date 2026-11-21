package ir.unilink.android.ui

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.spring
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.animation.togetherWith
import androidx.compose.animation.animateContentSize
import androidx.compose.foundation.background
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import ir.unilink.android.CertPrompt
import ir.unilink.android.KeepAliveService
import ir.unilink.android.Phase
import ir.unilink.android.R
import ir.unilink.android.UniLinkApp

@Composable
fun ConnectionScreen(app: UniLinkApp, goToTab: (Int) -> Unit) {
    val st by app.engine.status.collectAsState()
    val stats by app.engine.stats.collectAsState()
    val ctx = LocalContext.current
    val appData by app.store.data.collectAsState()
    val usageBusy by app.engine.usageBusy.collectAsState()
    val usageErrors by app.engine.usageErrors.collectAsState()
    val inUse = appData.groups.firstNotNullOfOrNull { g ->
        g.accounts.firstOrNull { it.id == st.accountId }?.let { a -> g to a }
    }
    val color = when (st.phase) {
        Phase.Connected -> StatusGreen
        Phase.WaitingRetry, Phase.NeedCertificate, Phase.AccountNeeded -> StatusAmber
        Phase.Connecting -> AccentBlue
        else -> StatusGrey
    }

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp)
            .padding(bottom = LocalBarPad.current),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(Modifier.staggerIn(0)) { ScreenTitle(stringResource(R.string.tab_connection)) }

        StatusHero(
            modifier = Modifier.staggerIn(1),
            phaseKey = st.phase.ordinal,
            color = color,
            icon = when (st.phase) {
                Phase.Connected -> Icons.Default.Check
                Phase.Connecting, Phase.WaitingRetry -> Icons.Default.Refresh
                Phase.NeedCertificate, Phase.AccountNeeded -> Icons.Default.Warning
                else -> Icons.Default.Info
            },
            pulsing = st.phase == Phase.Connecting || st.phase == Phase.WaitingRetry,
            title = st.title,
            detail = st.detail,
        )

        Panel(Modifier.staggerIn(2)) {
            InfoRow(stringResource(R.string.conn_network), st.groupName ?: "—")
            InfoRow(stringResource(R.string.conn_account), st.accountName ?: "—")
            InfoRow(stringResource(R.string.conn_last_keepalive), formatTime(st.lastKeepalive))
            InfoRow(stringResource(R.string.conn_next_check), formatTime(st.nextCheck))
            InfoRow(
                stringResource(R.string.conn_keepalives),
                stringResource(R.string.conn_keepalives_value, stats.ok, stats.failed),
            )
            InfoRow(stringResource(R.string.conn_net_events), stats.netEvents.toString())
            if (stats.late > 0) {
                InfoRow(
                    stringResource(R.string.conn_late),
                    stringResource(R.string.conn_late_value, stats.late, stats.maxLateSec),
                )
            }
        }

        val rejected = appData.groups.flatMap { g -> g.accounts.filter { it.enabled && it.passwordRejected } }
        if (rejected.isNotEmpty()) {
            Panel(Modifier.staggerIn(2)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Default.Warning, contentDescription = null, tint = StatusRed, modifier = Modifier.size(20.dp))
                    Spacer(Modifier.width(8.dp))
                    Text(stringResource(R.string.conn_badpw_title), style = MaterialTheme.typography.titleSmall, color = StatusRed)
                }
                Text(
                    stringResource(R.string.conn_badpw_body, rejected.joinToString(", ") { it.name }),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                TextButton(onClick = { goToTab(1) }) { Text(stringResource(R.string.conn_badpw_open)) }
            }
        }

        inUse?.let { (g, acc) ->
            if (g.usageUrl.isNotBlank()) {
                // Refresh once when the tab opens with old numbers (only while the app is on screen).
                LaunchedEffect(acc.id) {
                    val age = System.currentTimeMillis() - (acc.usage?.fetchedAt ?: 0L)
                    if (age > 30 * 60_000L) app.engine.refreshUsage(g.id, acc.id)
                }
                Panel(Modifier.staggerIn(2)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            stringResource(R.string.usage_title, acc.name),
                            style = MaterialTheme.typography.titleSmall,
                            modifier = Modifier.weight(1f),
                        )
                        TextButton(
                            onClick = { app.engine.refreshUsage(g.id, acc.id) },
                            enabled = acc.id !in usageBusy,
                        ) { Text(stringResource(R.string.usage_refresh_short)) }
                    }
                    UsageBlock(
                        usage = acc.usage,
                        loading = acc.id in usageBusy,
                        error = usageErrors[acc.id],
                        onRefresh = {},
                    )
                }
            }
        }

        st.pendingCert?.let { prompt ->
            CertificateCard(prompt) { app.engine.approveCertificate(prompt) }
        }

        Box(Modifier.staggerIn(3)) { SetupChecklist(app, goToTab) }

        val tick = rememberTick()
        val connectSrc = remember { MutableInteractionSource() }
        val pauseSrc = remember { MutableInteractionSource() }
        Row(Modifier.padding(top = 4.dp).staggerIn(4), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(
                onClick = {
                    tick()
                    KeepAliveService.start(ctx)
                    app.engine.connectNow()
                    // Wi-Fi is off: bring up the system switch so one tap is enough to join campus Wi-Fi.
                    if (!wifiRadioOn(ctx)) openWifiPanel(ctx)
                },
                interactionSource = connectSrc,
                modifier = Modifier.weight(1f).height(52.dp).pressScale(connectSrc),
                shape = RoundedCornerShape(26.dp),
            ) { Text(stringResource(R.string.conn_connect)) }
            OutlinedButton(
                onClick = {
                    tick()
                    app.engine.pauseAndSignOut()
                },
                interactionSource = pauseSrc,
                modifier = Modifier.weight(1f).height(52.dp).pressScale(pauseSrc),
                shape = RoundedCornerShape(26.dp),
                border = androidx.compose.foundation.BorderStroke(1.dp, MaterialTheme.colorScheme.primary.copy(alpha = if (st.phase == Phase.Connected || st.phase == Phase.Connecting || st.phase == Phase.WaitingRetry) 0.6f else 0.25f)),
            ) { Text(stringResource(R.string.conn_pause), modifier = Modifier.alpha(if (st.phase == Phase.Connected || st.phase == Phase.Connecting || st.phase == Phase.WaitingRetry) 1f else 0.55f)) }
        }
    }
}

@Composable
internal fun CertificateCard(p: CertPrompt, onTrust: () -> Unit) {
    Panel {
        Text(
            stringResource(if (p.changed) R.string.cert_changed_title else R.string.cert_review_title),
            style = MaterialTheme.typography.titleMedium,
            color = StatusAmber,
        )
        Text(
            stringResource(R.string.cert_body),
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        InfoRow(stringResource(R.string.cert_network), p.groupName)
        Text(p.url, style = MaterialTheme.typography.bodySmall)
        Text(
            stringResource(R.string.cert_sha256),
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Text(p.fingerprint, fontFamily = FontFamily.Monospace, fontSize = 12.sp)
        if (p.subject.isNotBlank()) {
            Text(p.subject, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Button(onClick = onTrust, modifier = Modifier.padding(top = 6.dp)) {
            Text(stringResource(R.string.cert_trust))
        }
    }
}

/**
 * Big status badge. The colour cross-fades, the icon morphs between states, the badge pops with a
 * spring whenever the phase changes, and a ring pulses while work is in progress.
 */
@Composable
private fun StatusHero(
    modifier: Modifier,
    phaseKey: Int,
    color: Color,
    icon: ImageVector,
    pulsing: Boolean,
    title: String,
    detail: String,
) {
    val transition = rememberInfiniteTransition(label = "pulse")
    val pulse by transition.animateFloat(
        initialValue = 0.55f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(1100), RepeatMode.Reverse),
        label = "pulseAlpha",
    )
    val ring by transition.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(1600)),
        label = "ring",
    )
    val glow = if (pulsing) pulse else 1f
    val glowBase = if (LocalGlass.current.dark) 0.40f else 0.16f
    val c by animateColorAsState(color, tween(450), label = "heroColor")

    val pop = remember { Animatable(1f) }
    LaunchedEffect(phaseKey) {
        pop.snapTo(0.82f)
        pop.animateTo(1f, spring(dampingRatio = 0.4f, stiffness = Spring.StiffnessMedium))
    }

    GlassSurface(modifier.fillMaxWidth().animateContentSize(softSpring()), strong = true) {
        Column(
            Modifier.fillMaxWidth().padding(vertical = 24.dp, horizontal = 16.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Box(
                Modifier
                    .size(96.dp)
                    .graphicsLayer {
                        scaleX = pop.value
                        scaleY = pop.value
                    }
                    .drawBehind {
                        val ctr = Offset(size.width / 2, size.height / 2)
                        val r = size.minDimension * 0.95f
                        drawCircle(
                            Brush.radialGradient(listOf(c.copy(alpha = glowBase * glow), Color.Transparent), ctr, r),
                            r, ctr,
                        )
                        if (pulsing) {
                            val rr = size.minDimension * (0.36f + 0.22f * ring)
                            drawCircle(c.copy(alpha = 0.5f * (1f - ring)), rr, ctr, style = androidx.compose.ui.graphics.drawscope.Stroke(2.dp.toPx()))
                        }
                    }
                    .padding(14.dp)
                    .background(c.copy(alpha = 0.18f), CircleShape)
                    .border(2.dp, c.copy(alpha = 0.55f + 0.45f * glow), CircleShape),
                contentAlignment = Alignment.Center,
            ) {
                AnimatedContent(
                    targetState = icon,
                    transitionSpec = {
                        (scaleIn(bouncySpring(), initialScale = 0.4f) + fadeIn(tween(180))) togetherWith
                            (scaleOut(tween(120), targetScale = 0.4f) + fadeOut(tween(100)))
                    },
                    label = "heroIcon",
                ) { ic -> Icon(ic, contentDescription = null, tint = c, modifier = Modifier.size(34.dp)) }
            }
            AnimatedContent(
                targetState = title,
                transitionSpec = {
                    (slideInVertically(tween(260)) { it / 3 } + fadeIn(tween(220, delayMillis = 60))) togetherWith
                        (slideOutVertically(tween(160)) { -it / 3 } + fadeOut(tween(110)))
                },
                label = "heroTitle",
            ) { tx -> Text(tx, style = MaterialTheme.typography.headlineSmall, textAlign = TextAlign.Center) }
            if (detail.isNotEmpty()) {
                Text(
                    detail,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                    style = MaterialTheme.typography.bodyMedium,
                )
            }
        }
    }
}
