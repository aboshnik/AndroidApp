package com.example.app

import android.os.Bundle
import android.view.KeyEvent
import android.widget.*
import androidx.appcompat.app.AppCompatActivity
import com.example.app.api.ApiClient
import com.example.app.api.NotifyTestRequest
import com.example.app.api.NotifyUpdateRequest
import com.example.app.api.SetPermissionsRequest
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class DeveloperConsoleActivity : AppCompatActivity() {
    private val scope = CoroutineScope(Dispatchers.Main + Job())

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_developer_console)

        val etTerminal = findViewById<EditText>(R.id.etTerminal)
        val btnRun = findViewById<Button>(R.id.btnRun)
        initTerminal(etTerminal)
        btnRun.setOnClickListener { runCurrentCommand(etTerminal) }

        etTerminal.setOnKeyListener { _, keyCode, event ->
            if (keyCode == KeyEvent.KEYCODE_ENTER && event.action == KeyEvent.ACTION_DOWN) {
                runCurrentCommand(etTerminal)
                true
            } else {
                false
            }
        }
    }

    private fun runCurrentCommand(etTerminal: EditText) {
        val raw = getCurrentCommand(etTerminal).trim()
        if (raw.isBlank()) {
            appendPrompt(etTerminal)
            return
        }
        handleCommand(raw, etTerminal)
    }

    private fun handleCommand(raw: String, etTerminal: EditText) {
        val parts = raw.split(" ").filter { it.isNotBlank() }
        if (parts.isEmpty()) return

        when (parts[0].lowercase()) {
            "help", "?" -> {
                appendLine(
                    etTerminal,
                    """
                    Доступные команды:
                      grant-posts <login>
                      revoke-posts <login>
                      notify-test <login>
                      notify-test-all
                      notify-update <versionCode?>
                      notify-update-push <versionCode?>
                    """.trimIndent()
                )
                appendPrompt(etTerminal)
            }

            "grant-posts" -> {
                val login = parts.getOrNull(1)?.trim().orEmpty()
                if (login.isBlank()) {
                    appendLine(etTerminal, "ERR: укажи login: grant-posts test123")
                    appendPrompt(etTerminal)
                    return
                }
                callApi(etTerminal, "grant-posts $login") {
                    ApiClient.developerApi.setPermissions(SetPermissionsRequest(login, true))
                }
            }

            "revoke-posts" -> {
                val login = parts.getOrNull(1)?.trim().orEmpty()
                if (login.isBlank()) {
                    appendLine(etTerminal, "ERR: укажи login: revoke-posts test123")
                    appendPrompt(etTerminal)
                    return
                }
                callApi(etTerminal, "revoke-posts $login") {
                    ApiClient.developerApi.setPermissions(SetPermissionsRequest(login, false))
                }
            }

            "notify-test" -> {
                val login = parts.getOrNull(1)?.trim()
                callApi(etTerminal, if (login.isNullOrBlank()) "notify-test-all" else "notify-test $login") {
                    NotifyTestRequest(login?.ifBlank { null })
                        .let { ApiClient.developerApi.notifyTest(it) }
                }
            }

            "notify-test-all" -> {
                callApi(etTerminal, "notify-test-all") {
                    ApiClient.developerApi.notifyTest(NotifyTestRequest(null))
                }
            }

            "notify-update" -> {
                val version = parts.getOrNull(1)?.trim().takeUnless { it.isNullOrBlank() }
                callApi(etTerminal, "notify-update ${version ?: ""}".trim()) {
                    ApiClient.developerApi.notifyUpdate(NotifyUpdateRequest(version, false))
                }
            }

            "notify-update-push" -> {
                val version = parts.getOrNull(1)?.trim().takeUnless { it.isNullOrBlank() }
                callApi(etTerminal, "notify-update-push ${version ?: ""}".trim()) {
                    ApiClient.developerApi.notifyUpdate(NotifyUpdateRequest(version, true))
                }
            }

            else -> {
                appendLine(etTerminal, "Неизвестная команда. Напечатай 'help' для списка.")
                appendPrompt(etTerminal)
            }
        }
    }

    private fun callApi(
        etTerminal: EditText,
        label: String,
        block: suspend () -> retrofit2.Response<com.example.app.api.AdminActionResponse>
    ) {
        scope.launch {
            try {
                appendLine(etTerminal, "Выполняю: $label ...")
                val response = withContext(Dispatchers.IO) { block() }
                val body = response.body()
                val text = when {
                    !response.isSuccessful -> "HTTP ${response.code()}"
                    body == null -> "Пустой ответ"
                    else -> "${if (body.success) "OK" else "ERR"}: ${body.message}"
                }
                appendLine(etTerminal, text)
                appendPrompt(etTerminal)
                Toast.makeText(this@DeveloperConsoleActivity, text, Toast.LENGTH_SHORT).show()
            } catch (e: Exception) {
                val text = "Ошибка: ${e.message}"
                appendLine(etTerminal, text)
                appendPrompt(etTerminal)
                Toast.makeText(this@DeveloperConsoleActivity, text, Toast.LENGTH_LONG).show()
            }
        }
    }

    private fun initTerminal(etTerminal: EditText) {
        etTerminal.setText("PS> ")
        etTerminal.setSelection(etTerminal.text.length)
    }

    private fun getCurrentCommand(etTerminal: EditText): String {
        val full = etTerminal.text?.toString().orEmpty()
        val idx = full.lastIndexOf("PS> ")
        return if (idx >= 0) full.substring(idx + 4) else full
    }

    private fun appendPrompt(etTerminal: EditText) {
        val current = etTerminal.text?.toString().orEmpty()
        val normalized = if (current.endsWith("\n")) current else "$current\n"
        etTerminal.setText("${normalized}PS> ")
        etTerminal.setSelection(etTerminal.text.length)
    }

    private fun appendLine(etTerminal: EditText, line: String) {
        val current = etTerminal.text?.toString().orEmpty()
        val normalized = if (current.endsWith("\n")) current else "$current\n"
        etTerminal.setText("$normalized$line")
        etTerminal.setSelection(etTerminal.text.length)
    }
}

