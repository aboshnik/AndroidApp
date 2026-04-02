package com.example.app

import android.content.Context

object NotificationSettingsManager {
    private const val PREFS = "notification_settings"
    private const val KEY_POST = "allow_post"
    private const val KEY_SECURITY = "allow_security"
    private const val KEY_TEST = "allow_test"

    fun isAllowed(context: Context, type: String?): Boolean {
        val t = type?.trim()?.lowercase().orEmpty()
        val p = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        return when (t) {
            "post" -> p.getBoolean(KEY_POST, true)
            "security" -> p.getBoolean(KEY_SECURITY, true)
            "test" -> p.getBoolean(KEY_TEST, true)
            else -> true
        }
    }

    fun getStates(context: Context): BooleanArray {
        val p = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        return booleanArrayOf(
            p.getBoolean(KEY_POST, true),
            p.getBoolean(KEY_SECURITY, true),
            p.getBoolean(KEY_TEST, true)
        )
    }

    fun saveStates(context: Context, states: BooleanArray) {
        if (states.size < 3) return
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putBoolean(KEY_POST, states[0])
            .putBoolean(KEY_SECURITY, states[1])
            .putBoolean(KEY_TEST, states[2])
            .apply()
    }
}

