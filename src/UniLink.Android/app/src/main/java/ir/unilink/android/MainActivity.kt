package ir.unilink.android

import android.content.Context
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import ir.unilink.android.ui.AppRoot
import ir.unilink.android.ui.UniLinkTheme

class MainActivity : ComponentActivity() {
    /** Apply the language chosen in Settings (and its text direction) to this screen. */
    override fun attachBaseContext(newBase: Context) {
        super.attachBaseContext(Lang.wrap(newBase))
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        ir.unilink.android.ui.ThemePrefs.load(this)
        val app = application as UniLinkApp
        if (app.store.data.value.settings.autoSignIn) {
            runCatching { KeepAliveService.start(this) }
        }
        setContent {
            UniLinkTheme {
                AppRoot(app)
            }
        }
    }
}
