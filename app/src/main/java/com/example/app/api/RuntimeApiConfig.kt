package com.example.app.api

import android.content.Context
import java.net.URI
import java.util.Locale

object RuntimeApiConfig {
    private const val PREFS = "runtime_api_config"
    private const val KEY_BASE_URL = "base_url"
    private const val AUTH_PREFS = "auth"
    private const val LEGACY_KEY = "api_base_url"

    fun normalizeOrNull(raw: String?): String? {
        val value = raw?.trim().orEmpty()
        if (value.isBlank()) return null
        val withScheme = if (value.startsWith("http://", true) || value.startsWith("https://", true)) {
            value
        } else {
            "http://$value"
        }

        val parsed = runCatching { URI(withScheme) }.getOrNull() ?: return null
        val scheme = parsed.scheme?.lowercase(Locale.ROOT) ?: return null
        if (scheme != "http" && scheme != "https") return null
        val host = parsed.host?.trim().orEmpty()
        if (host.isBlank()) return null

        val normalizedPath = when {
            parsed.path.isNullOrBlank() -> "/"
            parsed.path.endsWith("/") -> parsed.path
            else -> "${parsed.path}/"
        }

        val rebuilt = URI(
            scheme,
            parsed.userInfo,
            parsed.host,
            parsed.port,
            normalizedPath,
            parsed.query,
            parsed.fragment
        )
        return rebuilt.toString()
    }

    fun resolveBaseUrl(context: Context, buildConfigBaseUrl: String): String {
        val stored = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .getString(KEY_BASE_URL, null)
        val normalizedStored = normalizeOrNull(stored)
        if (!normalizedStored.isNullOrBlank()) return normalizedStored

        val normalizedBuild = normalizeOrNull(buildConfigBaseUrl)
        return normalizedBuild ?: "http://10.0.2.2:5000/"
    }

    fun saveBaseUrl(context: Context, raw: String): String {
        val normalized = normalizeOrNull(raw)
            ?: throw IllegalArgumentException("Укажите корректный адрес (http://... или https://...)")
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_BASE_URL, normalized)
            .apply()
        return normalized
    }

    fun clearBaseUrl(context: Context) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .edit()
            .remove(KEY_BASE_URL)
            .apply()
    }

    fun refreshFromServer(context: Context) {
        // Legacy migration: some builds stored runtime API in auth prefs.
        val legacy = context.getSharedPreferences(AUTH_PREFS, Context.MODE_PRIVATE)
            .getString(LEGACY_KEY, null)
        val normalized = normalizeOrNull(legacy) ?: return
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY_BASE_URL, normalized)
            .apply()
    }
}

