package com.example.app.chats

import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.Locale

/** Время сообщений из API (UTC / ISO) → часы:минуты в часовом поясе телефона. */
object ChatTimeFormat {
    private val timeFmt: DateTimeFormatter
        get() = DateTimeFormatter.ofPattern("HH:mm", Locale.getDefault())

    fun format(raw: String?): String {
        val s = raw?.trim().orEmpty()
        if (s.isEmpty()) return ""
        val deviceZone = ZoneId.systemDefault()
        runCatching {
            return OffsetDateTime.parse(s).atZoneSameInstant(deviceZone).format(timeFmt)
        }
        runCatching {
            val n = s.replace(' ', 'T')
            val ldt = LocalDateTime.parse(n, DateTimeFormatter.ISO_LOCAL_DATE_TIME)
            return ldt.atZone(ZoneOffset.UTC).withZoneSameInstant(deviceZone).format(timeFmt)
        }
        return ""
    }
}
