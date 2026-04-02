package com.example.app.utils

import java.text.SimpleDateFormat
import java.util.Locale
import java.util.TimeZone

object DateTimeFormatUtils {
    private val out = SimpleDateFormat("dd.MM.yyyy HH:mm", Locale("ru"))

    private val parserWithOffset = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ssXXX", Locale.US)
    private val parserNoOffset = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.US).apply {
        timeZone = TimeZone.getDefault()
    }
    private val parserZulu = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US).apply {
        timeZone = TimeZone.getTimeZone("UTC")
    }

    fun formatRuDateTime(isoLike: String?): String {
        if (isoLike.isNullOrBlank()) return ""

        // Handles: 2026-03-17T16:43:16.0166667 / 2026-03-17T16:43:16Z / with offsets
        val raw = isoLike.trim()
        val s = normalize(raw)

        val date = runCatching {
            when {
                s.endsWith("Z") -> parserZulu.parse(s)
                s.contains("+") || (s.lastIndexOf('-') > "yyyy-MM-dd".length) -> parserWithOffset.parse(s)
                else -> parserNoOffset.parse(s)
            }
        }.getOrNull()

        return if (date != null) out.format(date) else raw
    }

    private fun normalize(s: String): String {
        // Strip fractional seconds (any length), keep timezone part if exists.
        // Examples:
        // 2026-03-17T16:43:16.0166667      -> 2026-03-17T16:43:16
        // 2026-03-17T16:43:16.016Z        -> 2026-03-17T16:43:16Z
        // 2026-03-17T16:43:16.016+03:00   -> 2026-03-17T16:43:16+03:00
        val dot = s.indexOf('.')
        if (dot < 0) return s

        val after = s.substring(dot + 1)
        val tzIdx = after.indexOfAny(charArrayOf('Z', '+', '-'))
        return if (tzIdx < 0) {
            s.substring(0, dot)
        } else {
            val tz = after.substring(tzIdx)
            s.substring(0, dot) + tz
        }
    }
}

