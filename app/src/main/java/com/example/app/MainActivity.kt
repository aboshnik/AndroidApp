package com.example.app

import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Bundle
import android.os.Build
import android.util.Log
import androidx.appcompat.app.AppCompatActivity
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import androidx.core.app.NotificationManagerCompat
import android.widget.Button
import android.widget.EditText
import android.text.InputType
import android.text.method.PasswordTransformationMethod
import androidx.appcompat.widget.AppCompatImageButton
import android.widget.Toast
import com.example.app.api.ApiClient
import com.example.app.api.EmployeeLoginRequest
import com.google.firebase.messaging.FirebaseMessaging
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.util.UUID

class MainActivity : BaseActivity() {
    private val logTag = "MainActivityNet"

    private val requestNotifPermission = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (!granted) {
            promptEnableNotificationsInSettings()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Auto-login if session is still alive (5 minutes)
        if (SessionManager.isActive(this)) {
            SessionManager.startHomeWithSavedSession(this)
            return
        }

        setContentView(R.layout.activity_main)

        ensureNotificationsPermissionOnStart()

        val etLogin = findViewById<EditText>(R.id.etLogin)
        val etPassword = findViewById<EditText>(R.id.etPassword)
        val btnTogglePassword = findViewById<AppCompatImageButton>(R.id.btnTogglePassword)
        val btnLogin = findViewById<Button>(R.id.btnLogin)

        var isPasswordVisible = false
        fun applyPasswordVisibility() {
            etPassword.transformationMethod = if (isPasswordVisible) null else PasswordTransformationMethod.getInstance()
            etPassword.inputType = if (isPasswordVisible) {
                InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD
            } else {
                InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
            }
            etPassword.setSelection(etPassword.text?.length ?: 0)
        }

        btnTogglePassword.setOnClickListener {
            isPasswordVisible = !isPasswordVisible
            btnTogglePassword.setImageResource(
                if (isPasswordVisible) R.drawable.ic_visibility else R.drawable.ic_visibility_off
            )
            applyPasswordVisibility()
        }

        btnLogin.setOnClickListener {
            val login = etLogin.text.toString().trim()
            val password = etPassword.text.toString().trim()

            etLogin.error = null
            etPassword.error = null

            when {
                login.isEmpty() -> {
                    etLogin.error = getString(R.string.error_field_required)
                    etLogin.requestFocus()
                }
                password.isEmpty() -> {
                    etPassword.error = getString(R.string.error_field_required)
                    etPassword.requestFocus()
                }
                else -> {
                    btnLogin.isEnabled = false
                    scope.launch {
                        try {
                            val response = withContext(Dispatchers.IO) {
                                val deviceId = getSharedPreferences("auth", MODE_PRIVATE)
                                    .getString("device_id", null)
                                    ?: UUID.randomUUID().toString().also { newId ->
                                        getSharedPreferences("auth", MODE_PRIVATE).edit().putString("device_id", newId).apply()
                                    }
                                val deviceName = "${Build.MANUFACTURER} ${Build.MODEL}".trim()
                                ApiClient.employeeApi.login(
                                    EmployeeLoginRequest(
                                        login = login,
                                        password = password,
                                        deviceId = deviceId,
                                        deviceName = deviceName
                                    )
                                )
                            }

                            val body = response.body()
                            if (!response.isSuccessful || body == null) {
                                Toast.makeText(
                                    this@MainActivity,
                                    getString(R.string.error_network),
                                    Toast.LENGTH_LONG
                                ).show()
                                return@launch
                            }

                            if (!body.success || body.result == null) {
                                Toast.makeText(
                                    this@MainActivity,
                                    body.message,
                                    Toast.LENGTH_LONG
                                ).show()
                                return@launch
                            }

                            val r = body.result
                            // Save session (for auto-login + 5 min timeout)
                            SessionManager.saveFromLogin(
                                context = this@MainActivity,
                                login = login,
                                employeeId = r.employeeId,
                                lastName = r.lastName,
                                firstName = r.firstName,
                                phone = r.phone,
                                canCreatePosts = r.canCreatePosts,
                                canUseDevConsole = r.canUseDevConsole
                            )

                            // Register FCM token on backend for real push notifications.
                            runCatching {
                                FirebaseMessaging.getInstance().token.addOnSuccessListener { token ->
                                    val deviceId = getSharedPreferences("auth", MODE_PRIVATE)
                                        .getString("device_id", "")?.trim().orEmpty()
                                    val deviceName = "${Build.MANUFACTURER} ${Build.MODEL}".trim()
                                    scope.launch(Dispatchers.IO) {
                                        runCatching {
                                            ApiClient.pushTokenApi.register(
                                                com.example.app.api.RegisterPushTokenRequest(
                                                    login = login,
                                                    token = token,
                                                    deviceId = deviceId,
                                                    deviceName = deviceName
                                                )
                                            )
                                        }
                                    }
                                }
                            }

                            ensureNotificationsPermissionOnStart()
                            NotificationsWorker.schedule(this@MainActivity)

                            // Go to Home as main tab
                            SessionManager.startHomeWithSavedSession(this@MainActivity)
                        } catch (e: Exception) {
                            val details = buildErrorDetails(e)
                            Log.e(logTag, details, e)
                            Toast.makeText(
                                this@MainActivity,
                                "${getString(R.string.error_network)} $details",
                                Toast.LENGTH_LONG
                            ).show()
                            showNetworkErrorDialog(details)
                        } finally {
                            btnLogin.isEnabled = true
                        }
                    }
                }
            }
        }

        // Кнопка "Регистрация" открывает экран регистрации нового сотрудника
        findViewById<Button>(R.id.btnRegister).setOnClickListener {
            startActivity(Intent(this, NewEmployeeActivity::class.java))
        }
    }

    private fun ensureNotificationsPermissionOnStart() {
        // Android 13+ runtime permission
        if (Build.VERSION.SDK_INT >= 33) {
            val granted = ContextCompat.checkSelfPermission(
                this,
                android.Manifest.permission.POST_NOTIFICATIONS
            ) == PackageManager.PERMISSION_GRANTED
            if (!granted) {
                requestNotifPermission.launch(android.Manifest.permission.POST_NOTIFICATIONS)
                return
            }
        }

        // All versions: user may have disabled notifications in system settings
        if (!NotificationManagerCompat.from(this).areNotificationsEnabled()) {
            promptEnableNotificationsInSettings()
        }
    }

    private fun promptEnableNotificationsInSettings() {
        // Keep it simple and reliable (no extra deps): open app notification settings.
        val i = Intent().apply {
            action = "android.settings.APP_NOTIFICATION_SETTINGS"
            putExtra("android.provider.extra.APP_PACKAGE", packageName)
            data = Uri.fromParts("package", packageName, null)
        }
        safeShowDialog(
            androidx.appcompat.app.AlertDialog.Builder(this)
                .setTitle(getString(R.string.notif_perm_title))
                .setMessage(getString(R.string.notif_perm_text))
                .setPositiveButton(getString(R.string.notif_perm_open_settings)) { _, _ ->
                    runCatching { startActivity(i) }
                }
                .setNegativeButton(getString(R.string.notif_perm_later), null)
        )
    }

    private fun buildErrorDetails(e: Throwable): String {
        var t: Throwable? = e
        val chain = mutableListOf<String>()
        var guard = 0
        while (t != null && guard < 5) {
            val name = t.javaClass.simpleName
            val msg = t.message?.trim().orEmpty()
            chain += if (msg.isNotBlank()) "$name: $msg" else name
            t = t.cause
            guard++
        }
        return chain.joinToString(" -> ")
    }

    private fun showNetworkErrorDialog(details: String) {
        safeShowDialog(
            androidx.appcompat.app.AlertDialog.Builder(this)
                .setTitle("Подробности ошибки сети")
                .setMessage(details)
                .setPositiveButton("OK", null)
        )
    }

}

