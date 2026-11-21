package ir.unilink.android

import android.content.Context
import android.content.res.Configuration
import java.util.Locale

/** In-app language choice: "system" (follow the phone), "en" or "fa". Stored in plain prefs. */
object Lang {
    const val SYSTEM = "system"
    const val EN = "en"
    const val FA = "fa"
    private const val PREFS = "unilink_ui"
    private const val KEY = "lang"

    fun get(ctx: Context): String =
        ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(KEY, SYSTEM) ?: SYSTEM

    fun set(ctx: Context, value: String) {
        ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(KEY, value).apply()
    }

    /** A context whose resources use the chosen language (and its text direction). */
    fun wrap(ctx: Context): Context {
        val tag = get(ctx)
        if (tag == SYSTEM) return ctx
        val locale = Locale.forLanguageTag(tag)
        val cfg = Configuration(ctx.resources.configuration)
        cfg.setLocale(locale)
        cfg.setLayoutDirection(locale)
        return ctx.createConfigurationContext(cfg)
    }
}
