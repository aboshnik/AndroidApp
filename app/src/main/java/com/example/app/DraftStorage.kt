package com.example.app

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

object DraftStorage {
    private const val PREFS = "drafts_prefs"
    private const val KEY = "drafts_json"
    private const val MAX_DRAFTS = 50

    fun list(context: Context): List<Draft> {
        val json = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(KEY, "[]") ?: "[]"
        val arr = runCatching { JSONArray(json) }.getOrElse { JSONArray() }
        val out = mutableListOf<Draft>()
        for (i in 0 until arr.length()) {
            val o = arr.optJSONObject(i) ?: continue
            val id = o.optLong("id", 0L)
            val content = o.optString("content", "")
            val updatedAt = o.optLong("updatedAt", 0L)
            val urisArr = o.optJSONArray("uris") ?: JSONArray()
            val uris = buildList {
                for (j in 0 until urisArr.length()) {
                    val u = urisArr.optString(j, "")
                    if (u.isNotBlank()) add(u)
                }
            }
            if (id != 0L) out.add(Draft(id = id, content = content, attachmentUris = uris, updatedAt = updatedAt))
        }
        return out.sortedByDescending { it.updatedAt }
    }

    fun upsert(context: Context, draft: Draft) {
        val current = list(context).toMutableList()
        val idx = current.indexOfFirst { it.id == draft.id }
        if (idx >= 0) current[idx] = draft else current.add(0, draft)
        val trimmed = current.sortedByDescending { it.updatedAt }.take(MAX_DRAFTS)
        saveAll(context, trimmed)
    }

    fun delete(context: Context, id: Long) {
        val filtered = list(context).filterNot { it.id == id }
        saveAll(context, filtered)
    }

    fun get(context: Context, id: Long): Draft? = list(context).firstOrNull { it.id == id }

    private fun saveAll(context: Context, drafts: List<Draft>) {
        val arr = JSONArray()
        drafts.sortedByDescending { it.updatedAt }.forEach { d ->
            val o = JSONObject()
            o.put("id", d.id)
            o.put("content", d.content)
            o.put("updatedAt", d.updatedAt)
            val urisArr = JSONArray()
            d.attachmentUris.forEach { urisArr.put(it) }
            o.put("uris", urisArr)
            arr.put(o)
        }
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .edit()
            .putString(KEY, arr.toString())
            .apply()
    }
}

