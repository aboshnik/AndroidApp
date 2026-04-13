package com.example.app

import android.content.Context
import androidx.appcompat.app.AppCompatDelegate

object UserSettings {
    private const val PREFS = "user_settings"
    private const val KEY_NOTIFICATIONS_ENABLED = "notifications_enabled"
    private const val KEY_THEME_MODE = "theme_mode" // light | dark

    fun areNotificationsEnabled(context: Context): Boolean {
        return context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .getBoolean(KEY_NOTIFICATIONS_ENABLED, true)
    }

    fun setNotificationsEnabled(context: Context, enabled: Boolean) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putBoolean(KEY_NOTIFICATIONS_ENABLED, enabled)
            .apply()
    }

    fun currentThemeMode(context: Context): String {
        return context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .getString(KEY_THEME_MODE, "light")
            ?.trim()
            .orEmpty()
            .ifBlank { "light" }
    }

    fun setThemeMode(context: Context, mode: String) {
        val normalized = if (mode.equals("dark", ignoreCase = true)) "dark" else "light"
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putString(KEY_THEME_MODE, normalized)
            .apply()
        applyTheme(context)
    }

    fun applyTheme(context: Context) {
        when (currentThemeMode(context)) {
            "dark" -> AppCompatDelegate.setDefaultNightMode(AppCompatDelegate.MODE_NIGHT_YES)
            else -> AppCompatDelegate.setDefaultNightMode(AppCompatDelegate.MODE_NIGHT_NO)
        }
    }
}
